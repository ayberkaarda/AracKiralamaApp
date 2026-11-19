using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AracKiralama.Data.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only. Reads the connection string from ARAC_CONNECTION and falls back to a
/// local SQL Server Express instance.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AracDbContext>
{
    private const string FallbackConnectionString =
        "Server=localhost\\SQLEXPRESS;Database=AracKiralama;Trusted_Connection=True;TrustServerCertificate=True;";

    public AracDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DataServiceCollectionExtensions.ConnectionEnvironmentVariable);
        var options = new DbContextOptionsBuilder<AracDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connectionString) ? FallbackConnectionString : connectionString)
            .Options;
        return new AracDbContext(options);
    }
}
