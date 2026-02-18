using Microsoft.EntityFrameworkCore;
using ScaleBlazor.Shared;

namespace ScaleBlazor.Server.Data;

public class ScaleDbContext : DbContext
{
    public ScaleDbContext(DbContextOptions<ScaleDbContext> options) : base(options)
    {
    }

    public DbSet<ScaleReading> ScaleReadings { get; set; }
    public DbSet<Pallet> Pallets { get; set; }
    public DbSet<AppSettings> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ScaleReading>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Weight).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();
        });

        modelBuilder.Entity<Pallet>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PalletId).IsRequired();
            entity.Property(e => e.TotalWeight).IsRequired();
            entity.Property(e => e.ReadingCount).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.IsCompleted).IsRequired();
        });

        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReadingsPerPallet).IsRequired();
            entity.Property(e => e.ScalePortName).IsRequired(false);
        });
    }
}
