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
    public ActionResult<ScaleStatus> GetScaleStatus()
    {
        var scaleEnabled = _configuration.GetValue<bool>("Scale:Enabled", false);

        return new ScaleStatus
        {
            IsConnected = _scaleService.IsConnected,
            IsEnabled = scaleEnabled,
            PortName = _configuration["Scale:PortName"] ?? "COM3",
            CurrentWeight = _scaleService.CurrentWeight
        };
    }

    [HttpGet("ports")]
    public ActionResult<List<string>> GetAvailablePorts()
    {
        return _scaleService.GetAvailablePorts();
    }

    [HttpGet("readings")]
    public async Task<ActionResult<List<ScaleReading>>> GetLastReadings([FromQuery] int count = 10)
    {
        return await _context.ScaleReadings
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    [HttpGet("readings/today")]
    public async Task<ActionResult<List<ScaleReading>>> GetTodayReadings()
    {
        var today = DateTime.Today;
        return await _context.ScaleReadings
            .Where(r => r.Timestamp >= today)
            .OrderBy(r => r.Timestamp)
            .ToListAsync();
    }

    [HttpGet("daily-averages")]
    public async Task<ActionResult<List<DailyAverage>>> GetDailyAverages([FromQuery] int days = 10)
    {
        var startDate = DateTime.Today.AddDays(-days);

        var averages = await _context.ScaleReadings
            .Where(r => r.Timestamp >= startDate)
            .GroupBy(r => r.Timestamp.Date)
            .Select(g => new DailyAverage
            {
                Date = g.Key,
                AverageWeight = g.Average(r => r.Weight)
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