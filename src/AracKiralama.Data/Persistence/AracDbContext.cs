using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Persistence;

/// <summary>
/// EF Core context of the car rental database. Table and column names follow the original schema
/// (Kullanicilar, Araclar, Kiralama) and its Turkish naming for the new parts (KurulumAnahtarlari, AracBakim), so an existing database
/// keeps its tables; the code itself uses English names.
/// </summary>
public sealed class AracDbContext(DbContextOptions<AracDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Car> Cars => Set<Car>();

    public DbSet<Rental> Rentals => Set<Rental>();

    public DbSet<SetupKey> SetupKeys => Set<SetupKey>();

    public DbSet<MaintenanceRecord> Maintenance => Set<MaintenanceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AracDbContext).Assembly);
    }
}
