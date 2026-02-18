using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Server.Data;
using ScaleBlazor.Shared;

namespace ScaleBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ScaleDbContext _context;
    private readonly IConfiguration _configuration;

    public SettingsController(ScaleDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<ActionResult<AppSettings>> GetSettings()
    {
        var settings = await _context.Settings.AsNoTracking().FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new AppSettings
            {
                ReadingsPerPallet = 10,
                ScalePortName = _configuration["Scale:PortName"]
            };
            _context.Settings.Add(settings);
            await _context.SaveChangesAsync();
        }
        return settings;
    }

    [HttpPut]
    public async Task<ActionResult<AppSettings>> UpdateSettings(AppSettings settings)
    {
        var existing = await _context.Settings.FirstOrDefaultAsync();
        if (existing == null)
        {
            _context.Settings.Add(settings);
        }
        else
        {
            existing.ReadingsPerPallet = settings.ReadingsPerPallet;
            existing.AutoCaptureEnabled = settings.AutoCaptureEnabled;
            existing.AutoCaptureThresholdPercent = settings.AutoCaptureThresholdPercent;
            existing.ScalePortName = settings.ScalePortName;
        }

        await _context.SaveChangesAsync();
        return settings;
    }

    [HttpPost("purge")]
    public async Task<ActionResult<PurgeResult>> PurgeData(PurgeRequest request)
    {
        var cutoffDate = request.CutoffDate.Date;

        var readingsToDelete = await _context.ScaleReadings
            .Where(r => r.Timestamp < cutoffDate)
            .ToListAsync();

        var palletsToDelete = await _context.Pallets
            .Where(p => p.CreatedAt < cutoffDate)
            .ToListAsync();

        _context.ScaleReadings.RemoveRange(readingsToDelete);
        _context.Pallets.RemoveRange(palletsToDelete);

        await _context.SaveChangesAsync();

        return new PurgeResult
        {
            CutoffDate = cutoffDate,
            DeletedReadings = readingsToDelete.Count,
            DeletedPallets = palletsToDelete.Count
        };
    }
}
