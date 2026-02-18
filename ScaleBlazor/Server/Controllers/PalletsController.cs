using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Server.Data;
using ScaleBlazor.Shared;

namespace ScaleBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PalletsController : ControllerBase
{
    private readonly ScaleDbContext _context;

    public PalletsController(ScaleDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<Pallet>>> GetPallets([FromQuery] int count = 10)
    {
        return await _context.Pallets
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    [HttpGet("active")]
    public async Task<ActionResult<Pallet?>> GetActivePallet()
    {
        return await _context.Pallets
            .AsNoTracking()
            .Where(p => !p.IsCompleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
    }

    [HttpPost("complete")]
    public async Task<ActionResult> CompletePallet()
    {
        var activePallet = await _context.Pallets
            .Where(p => !p.IsCompleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (activePallet != null)
        {
            activePallet.IsCompleted = true;
            await _context.SaveChangesAsync();
        }

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
        await _context.SaveChangesAsync();

        return Ok(newPallet);
    }
}
