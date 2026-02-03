using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Server.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddDbContext<ScaleDbContext>(options =>
    options.UseSqlite("Data Source=scale.db"));

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
    
    // In development, delete and recreate the database to ensure schema is up to date
    if (app.Environment.IsDevelopment())
    {
        db.Database.EnsureDeleted();
    }
    
    db.Database.EnsureCreated();

    if (!db.Pallets.Any())
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
            AutoCaptureThresholdPercent = 1.0
        });
        db.SaveChanges();
    }
}

app.Run();


