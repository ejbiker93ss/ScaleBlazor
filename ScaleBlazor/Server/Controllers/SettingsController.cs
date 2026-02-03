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

    public SettingsController(ScaleDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<AppSettings>> GetSettings()
    {
        var settings = await _context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new AppSettings { ReadingsPerPallet = 10 };
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
        }

        await _context.SaveChangesAsync();
        return settings;
    }
}
