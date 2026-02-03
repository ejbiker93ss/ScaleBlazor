using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Server.Data;
using ScaleBlazor.Shared;

namespace ScaleBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScaleController : ControllerBase
{
    private readonly ScaleDbContext _context;
    private static readonly Random _random = new();

    public ScaleController(ScaleDbContext context)
    {
        _context = context;
    }

    [HttpGet("current")]
    public async Task<ActionResult<ScaleReading>> GetCurrentReading()
    {
        var reading = await _context.ScaleReadings
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefaultAsync();

        if (reading == null)
        {
            var newReading = GenerateReading();
            _context.ScaleReadings.Add(newReading);
            await _context.SaveChangesAsync();
            return newReading;
        }

        return reading;
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
        var settings = await _context.Settings.FirstOrDefaultAsync();
        var readingsPerPallet = settings?.ReadingsPerPallet ?? 10;

        var activePallet = await _context.Pallets
            .Where(p => !p.IsCompleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        var reading = GenerateReading();

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

    private ScaleReading GenerateReading()
    {
        return new ScaleReading
        {
            Weight = Math.Round(45.0 + (_random.NextDouble() * 1.5 - 0.75), 2),
            Timestamp = DateTime.Now
        };
    }
}
