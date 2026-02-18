using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.IO;
using ScaleBlazor.Server.Data;
using ScaleBlazor.Server.Logging;
using ScaleBlazor.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFile(Path.Combine(builder.Environment.ContentRootPath, "Logs", "scale.log"));

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddDbContext<ScaleDbContext>(options =>
    options.UseSqlite("Data Source=scale.db"));

// Register Scale Reader Service as Singleton
builder.Services.AddSingleton<ScaleReaderService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

        if (exception != null)
        {
            logger.LogError(exception, "Unhandled server exception");
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected server error occurred." });
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseCors("AllowAll");

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ScaleDbContext>();
    
    db.Database.EnsureCreated();

    ApplyPendingSchemaUpdates(db);

    if (app.Environment.IsDevelopment() && !db.Pallets.Any())
    {
        var random = new Random();

        for (int i = 1; i <= 3; i++)
        {
            var pallet = new ScaleBlazor.Shared.Pallet
            {
                PalletId = $"P{i:D3}",
                CreatedAt = DateTime.Now.AddDays(-i),
                IsCompleted = i < 3,
                ReadingCount = 10,
                TotalWeight = Math.Round(45.0 + (random.NextDouble() * 1.5), 2)
            };
            db.Pallets.Add(pallet);
        }

        for (int i = 0; i < 30; i++)
        {
            var reading = new ScaleBlazor.Shared.ScaleReading
            {
                Weight = Math.Round(45.0 + (random.NextDouble() * 1.5), 2),
                Timestamp = DateTime.Now.AddMinutes(-i * 15),
                PalletId = i < 10 ? "P001" : (i < 20 ? "P002" : "P003")
            };
            db.ScaleReadings.Add(reading);
        }

        db.SaveChanges();
    }

    // Initialize settings if not exist
    if (!db.Settings.Any())
    {
        db.Settings.Add(new ScaleBlazor.Shared.AppSettings
        {
            ReadingsPerPallet = 10,
            AutoCaptureEnabled = false,
            AutoCaptureThresholdPercent = 1.0,
            ScalePortName = app.Configuration["Scale:PortName"]
        });
        db.SaveChanges();
    }
}

static void ApplyPendingSchemaUpdates(ScaleDbContext db)
{
    using var connection = db.Database.GetDbConnection();
    if (connection.State != ConnectionState.Open)
    {
        connection.Open();
    }

    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA table_info('Settings');";

    var hasScalePortName = false;
    using (var reader = command.ExecuteReader())
    {
        while (reader.Read())
        {
            var columnName = reader.GetString(1);
            if (string.Equals(columnName, "ScalePortName", StringComparison.OrdinalIgnoreCase))
            {
                hasScalePortName = true;
                break;
            }
        }
    }

    if (!hasScalePortName)
    {
        command.CommandText = "ALTER TABLE Settings ADD COLUMN ScalePortName TEXT";
        command.ExecuteNonQuery();
    }
}

// Start the scale reader service if enabled
var scaleEnabled = app.Configuration.GetValue<bool>("Scale:Enabled", false);
if (scaleEnabled)
{
    var scaleService = app.Services.GetRequiredService<ScaleReaderService>();
    try
    {
        scaleService.Start();
    }
    catch (Exception)
    {
        // Running in simulation mode when start fails.
    }
}

app.Run();


