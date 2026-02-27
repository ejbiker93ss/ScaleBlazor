using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Server.Data;
using ScaleBlazor.Server.Services;
using ScaleBlazor.Shared;

namespace ScaleBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScaleController : ControllerBase
{
    private readonly ScaleDbContext _context;
    private readonly ScaleReaderService _scaleService;
    private readonly IConfiguration _configuration;
    private static readonly Random _random = new();

    public ScaleController(ScaleDbContext context, ScaleReaderService scaleService, IConfiguration configuration)
    {
        _context = context;
        _scaleService = scaleService;
        _configuration = configuration;
    }

    [HttpGet("current")]
    public ActionResult<ScaleReading> GetCurrentReading()
    {
        var scaleEnabled = _configuration.GetValue<bool>("Scale:Enabled", false);

        var reading = new ScaleReading
        {
            Weight = scaleEnabled && _scaleService.IsConnected 
                ? _scaleService.CurrentWeight 
                : GenerateSimulatedWeight(),
            Timestamp = DateTime.Now
        };

        return reading;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ScaleStatus>> GetScaleStatus()
    {
        var scaleEnabled = _configuration.GetValue<bool>("Scale:Enabled", false);
        var settings = await _context.Settings.AsNoTracking().FirstOrDefaultAsync();

        return new ScaleStatus
        {
            IsConnected = _scaleService.IsConnected,
            IsEnabled = scaleEnabled,
            PortName = settings?.ScalePortName ?? _configuration["Scale:PortName"] ?? "COM3",
            CurrentWeight = _scaleService.CurrentWeight
        };
    }

    [HttpGet("ports")]
    public ActionResult<List<string>> GetAvailablePorts()
    {
        return _scaleService.GetAvailablePorts();
    }

    [HttpGet("raw-readings")]
    public async Task<ActionResult<List<string>>> GetRawReadings([FromQuery] int count = 50)
    {
        try
        {
            var lines = await _scaleService.CaptureRawLinesAsync(count, TimeSpan.FromSeconds(10), HttpContext.RequestAborted);
            return lines.ToList();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpGet("speed-test")]
    public async Task<ActionResult<string>> RunSpeedTest([FromQuery] int count = 20)
    {
        try
        {
            var result = await _scaleService.RunReadingSpeedTestAsync(count);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("auto-detect")]
    public async Task<ActionResult<PortDetectionResult>> AutoDetectPort()
    {
        var detectedPort = await _scaleService.AutoDetectPortAsync();
        if (string.IsNullOrWhiteSpace(detectedPort))
        {
            return NotFound(new PortDetectionResult
            {
                Success = false,
                Message = "No scale data detected on any available COM port."
            });
        }

        var settings = await _context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new AppSettings { ReadingsPerPallet = 10 };
            _context.Settings.Add(settings);
        }

        settings.ScalePortName = detectedPort;
        await _context.SaveChangesAsync();

        if (_configuration.GetValue<bool>("Scale:Enabled", false))
        {
            _scaleService.Restart();
        }

        return new PortDetectionResult
        {
            Success = true,
            PortName = detectedPort,
            Message = $"Detected scale on {detectedPort}."
        };
    }

    [HttpGet("readings")]
    public async Task<ActionResult<List<ScaleReading>>> GetLastReadings([FromQuery] int count = 10)
    {
        return await _context.ScaleReadings
            .AsNoTracking()
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    [HttpGet("readings/today")]
    public async Task<ActionResult<List<ScaleReading>>> GetTodayReadings()
    {
        var today = DateTime.Today;
        return await _context.ScaleReadings
            .AsNoTracking()
            .Where(r => r.Timestamp >= today)
            .OrderBy(r => r.Timestamp)
            .ToListAsync();
    }

    [HttpGet("daily-averages")]
    public async Task<ActionResult<List<DailyAverage>>> GetDailyAverages([FromQuery] int days = 10)
    {
        var startDate = DateTime.Today.AddDays(-days);

        var averages = await _context.ScaleReadings
            .AsNoTracking()
            .Where(r => r.Timestamp >= startDate)
            .GroupBy(r => r.Timestamp.Date)
            .Select(g => new DailyAverage
            {
                Date = g.Key,
                AverageWeight = g.Average(r => r.Weight),
                PalletCount = g.Select(r => r.PalletId)
                    .Where(palletId => palletId != null && palletId != "")
                    .Distinct()
                    .Count()
            })
            .OrderByDescending(d => d.Date)
            .Take(days)
            .ToListAsync();

        return averages;
    }

    [HttpPost("capture")]
    public async Task<ActionResult<ScaleReading>> CaptureReading()
    {
        var scaleEnabled = _configuration.GetValue<bool>("Scale:Enabled", false);
        var settings = await _context.Settings.FirstOrDefaultAsync();
        var readingsPerPallet = settings?.ReadingsPerPallet ?? 10;

        var activePallet = await _context.Pallets
            .Where(p => !p.IsCompleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        // Get current weight from scale or generate simulated
        var reading = new ScaleReading
        {
            Weight = scaleEnabled && _scaleService.IsConnected 
                ? _scaleService.CurrentWeight 
                : GenerateSimulatedWeight(),
            Timestamp = DateTime.Now
        };

        if (activePallet != null)
        {
            reading.PalletId = activePallet.PalletId;
            activePallet.ReadingCount++;

            var palletReadings = await _context.ScaleReadings
                .Where(r => r.PalletId == activePallet.PalletId)
                .ToListAsync();
            palletReadings.Add(reading);
            activePallet.TotalWeight = palletReadings.Average(r => r.Weight);

            // Auto-complete pallet if it reaches the limit
            if (activePallet.ReadingCount >= readingsPerPallet)
            {
                activePallet.IsCompleted = true;

                // Create new pallet
                var nextPalletNumber = await _context.Pallets.CountAsync() + 1;
                var newPallet = new Pallet
                {
                    PalletId = $"P{nextPalletNumber:D3}",
                    CreatedAt = DateTime.Now,
                    IsCompleted = false,
                    ReadingCount = 0,
                    TotalWeight = 0
                };
                _context.Pallets.Add(newPallet);
            }
        }

        _context.ScaleReadings.Add(reading);
        await _context.SaveChangesAsync();

        return reading;
    }

    [HttpDelete("readings/{id}")]
    public async Task<IActionResult> DeleteReading(int id)
    {
        var reading = await _context.ScaleReadings.FindAsync(id);
        if (reading == null)
        {
            return NotFound();
        }

        _context.ScaleReadings.Remove(reading);

        if (!string.IsNullOrEmpty(reading.PalletId))
        {
            var pallet = await _context.Pallets.FirstOrDefaultAsync(p => p.PalletId == reading.PalletId);
            if (pallet != null)
            {
                pallet.ReadingCount--;

                var remainingReadings = await _context.ScaleReadings
                    .Where(r => r.PalletId == pallet.PalletId && r.Id != id)
                    .ToListAsync();

                if (remainingReadings.Any())
                {
                    pallet.TotalWeight = remainingReadings.Average(r => r.Weight);
                }
                else
                {
                    pallet.TotalWeight = 0;
                }
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    private double GenerateSimulatedWeight()
    {
        return Math.Round(45.0 + (_random.NextDouble() * 1.5 - 0.75), 2);
    }
}

public class ScaleStatus
{
    public bool IsConnected { get; set; }
    public bool IsEnabled { get; set; }
    public string PortName { get; set; } = "";
    public double CurrentWeight { get; set; }
}