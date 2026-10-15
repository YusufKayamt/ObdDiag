using Microsoft.EntityFrameworkCore;
using ObdDiag.Data.Entities;

namespace ObdDiag.Data;

/// <summary>Yerel SQLite veritabanının tanımı (tablolar ve aralarındaki ilişkiler).</summary>
public class ObdDbContext : DbContext
{
    public ObdDbContext(DbContextOptions<ObdDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<RepairRecord> RepairRecords => Set<RepairRecord>();
    public DbSet<DtcRecord> DtcRecords => Set<DtcRecord>();
    public DbSet<LiveDataSnapshot> LiveDataSnapshots => Set<LiveDataSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(1000);

            // Aracı olan müşteri yanlışlıkla silinip tamir geçmişi kaybolmasın.
            e.HasMany(x => x.Vehicles).WithOne(x => x.Customer!)
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vehicle>(e =>
        {
            e.Property(x => x.Vin).IsRequired().HasMaxLength(17);
            e.HasIndex(x => x.Vin).IsUnique(); // aynı VIN iki kez kaydedilemez
            e.Property(x => x.Plate).HasMaxLength(20);
            e.Property(x => x.Make).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(1000);

            // Tamir geçmişi olan araç silinemez.
            e.HasMany(x => x.RepairRecords).WithOne(x => x.Vehicle!)
                .HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RepairRecord>(e =>
        {
            e.Property(x => x.Description).IsRequired().HasMaxLength(2000);

            // Bir tamir kaydı silinirse ona bağlı arıza kodları ve canlı veri görüntüleri de silinir.
            e.HasMany(x => x.DtcRecords).WithOne(x => x.RepairRecord!)
                .HasForeignKey(x => x.RepairRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.LiveDataSnapshots).WithOne(x => x.RepairRecord!)
                .HasForeignKey(x => x.RepairRecordId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DtcRecord>(e =>
        {
            e.Property(x => x.Code).IsRequired().HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(500);
        });
    }
}
