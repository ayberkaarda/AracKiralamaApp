using AracKiralama.Data.Persistence;
using AracKiralama.Data.Repositories;
using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AracKiralama.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Environment variable that overrides the configured connection string.</summary>
    public const string ConnectionEnvironmentVariable = "ARAC_CONNECTION";

    /// <summary>Environment variable that turns the demonstration data on ("1" or "true").</summary>
    public const string DemoEnvironmentVariable = "ARAC_DEMO";

    /// <summary>
    /// Command timeout on SQL Server, in seconds. It is longer than every application lock wait (30
    /// seconds for a car or the admin roles, 60 for the legacy upgrade), so a command waiting for a lock
    /// ends with the lock's own refusal, which the screens show as a busy message, never with a command
    /// timeout.
    /// </summary>
    public const int CommandTimeoutSeconds = 90;

    /// <summary>Registers the data access layer on SQL Server.</summary>
    public static IServiceCollection AddSqlServerDataAccess(
        this IServiceCollection services,
        string connectionString,
        DataAccessOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return services.AddDataAccess(UseSqlServer(connectionString), options);
    }

    /// <summary>The SQL Server provider with the application's command timeout.</summary>
    public static Action<DbContextOptionsBuilder> UseSqlServer(string connectionString) =>
        builder => builder.UseSqlServer(connectionString, sql => sql.CommandTimeout(CommandTimeoutSeconds));

    /// <summary>
    /// Registers the data access layer with a caller supplied provider configuration, so tests can plug
    /// in another provider.
    /// </summary>
    public static IServiceCollection AddDataAccess(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureProvider,
        DataAccessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddDbContextFactory<AracDbContext>(configureProvider);
        services.AddSingleton(options ?? new DataAccessOptions());
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<ICarRepository, CarRepository>();
        services.AddSingleton<IRentalRepository, RentalRepository>();
        return services;
    }
}
