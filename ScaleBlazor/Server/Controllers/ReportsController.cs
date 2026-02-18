using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Server.Data;
using ScaleBlazor.Shared;
using System.Text;

namespace ScaleBlazor.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ScaleDbContext _context;

    public ReportsController(ScaleDbContext context)
    {
        _context = context;
    }

    [HttpGet("statistics")]
    public async Task<ActionResult<ReportStatistics>> GetStatistics()
    {
        var totalReadings = await _context.ScaleReadings.CountAsync();
        var totalPallets = await _context.Pallets.CountAsync();
        var averageWeight = await _context.ScaleReadings.AverageAsync(r => (double?)r.Weight) ?? 0;
        var today = DateTime.Today;
        var todayReadings = await _context.ScaleReadings
            .Where(r => r.Timestamp.Date == today)
            .CountAsync();

        return new ReportStatistics
        {
            TotalReadings = totalReadings,
            TotalPallets = totalPallets,
            AverageWeight = averageWeight,
            TodayReadings = todayReadings
        };
    }

    [HttpGet("average-daily")]
    public async Task<ActionResult<List<AverageData>>> GetDailyAverages([FromQuery] int days = 30)
    {
        var startDate = DateTime.Now.AddDays(-days);

        var readings = await _context.ScaleReadings
            .AsNoTracking()
            .Where(r => r.Timestamp >= startDate)
            .GroupBy(r => r.Timestamp.Date)
            .Select(g => new AverageData
            {
                Date = g.Key,
                Label = g.Key.ToString("MMM dd"),
                AverageWeight = g.Average(r => r.Weight)
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return readings;
    }

    [HttpGet("average-weekly")]
    public async Task<ActionResult<List<AverageData>>> GetWeeklyAverages([FromQuery] int weeks = 12)
    {
        var startDate = DateTime.Now.AddDays(-weeks * 7);

        var readings = await _context.ScaleReadings
            .AsNoTracking()
            .Where(r => r.Timestamp >= startDate)
            .ToListAsync();

        var weeklyData = readings
            .GroupBy(r => new
            {
                Year = r.Timestamp.Year,
                Week = System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                    r.Timestamp,
                    System.Globalization.CalendarWeekRule.FirstDay,
                    DayOfWeek.Sunday)
            })
            .Select(g => new AverageData
            {
                Date = g.Min(r => r.Timestamp.Date),
                Label = $"Week {g.Key.Week}",
                AverageWeight = g.Average(r => r.Weight)
            })
            .OrderBy(x => x.Date)
            .ToList();

        return weeklyData;
    }

    [HttpGet("trends-daily")]
    public async Task<ActionResult<List<AverageData>>> GetTrendsDaily([FromQuery] int days = 30)
    {
        var startDate = DateTime.Now.AddDays(-days).Date;

        var readings = await _context.ScaleReadings
            .AsNoTracking()
            .Where(r => r.Timestamp >= startDate)
            .GroupBy(r => r.Timestamp.Date)
            .Select(g => new AverageData
            {
                Date = g.Key,
                Label = g.Key.ToString("MMM dd"),
                AverageWeight = g.Average(r => r.Weight)
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return readings;
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportData(
        [FromQuery] string type,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        var csv = new StringBuilder();

        if (type == "readings" || type == "all")
        {
            csv.AppendLine("Reading ID,Weight (lbs),Timestamp,Pallet ID");
            
            var readings = await _context.ScaleReadings
                .AsNoTracking()
                .Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate)
                .OrderByDescending(r => r.Timestamp)
                .ToListAsync();

            foreach (var reading in readings)
            {
                csv.AppendLine($"{reading.Id},{reading.Weight:F2},{reading.Timestamp:yyyy-MM-dd HH:mm:ss},{reading.PalletId}");
            }

            if (type == "all")
            {
                csv.AppendLine();
                csv.AppendLine("=== PALLET DATA ===");
                csv.AppendLine();
            }
        }

        if (type == "pallets" || type == "all")
        {
            if (type == "pallets")
            {
                csv.AppendLine("Pallet ID,Reading Count,Total Weight (lbs),Average Weight (lbs),Created Date,Status");
            }
            else
            {
                csv.AppendLine("Pallet ID,Reading Count,Total Weight (lbs),Average Weight (lbs),Created Date,Status");
            }

            var pallets = await _context.Pallets
                .AsNoTracking()
                .Where(p => p.CreatedAt >= startDate && p.CreatedAt <= endDate)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            foreach (var pallet in pallets)
            {
                var avgWeight = pallet.ReadingCount > 0 ? pallet.TotalWeight / pallet.ReadingCount : 0;
                var status = pallet.IsCompleted ? "Completed" : "Active";
                csv.AppendLine($"{pallet.PalletId},{pallet.ReadingCount},{pallet.TotalWeight:F2},{avgWeight:F2},{pallet.CreatedAt:yyyy-MM-dd},{status}");
            }
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", $"scale-data-{type}-{DateTime.Now:yyyyMMdd}.csv");
    }
}

public class ReportStatistics
{
    public int TotalReadings { get; set; }
    public int TotalPallets { get; set; }
    public double AverageWeight { get; set; }
    public int TodayReadings { get; set; }
}

public class AverageData
{
    public string Label { get; set; } = "";
    public double AverageWeight { get; set; }
    public DateTime Date { get; set; }
}
