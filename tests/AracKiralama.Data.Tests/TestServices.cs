using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AracKiralama.Business;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AracKiralama.Data.Tests;

/// <summary>Shared registrations and sample values for tests that run the real services on a real database.</summary>
internal static class TestServices
{
    /// <summary>Monday 2 March 2026, 10:10; the fake clock's local time zone is UTC.</summary>
    public static readonly DateTimeOffset Start = new(2026, 3, 2, 10, 10, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 3, 2);

    public const string Password = "Gizli.Sifre1";

    /// <summary>The settings the tests configure: 1 to 30 days, at most 60 days ahead, no discount.</summary>
    public static RentalPolicy Policy { get; } = new() { MinRentalDays = 1, MaxRentalDays = 30, MaxAdvanceDays = 60 };

    /// <summary>
    /// Adds the business services with a fake clock and a cheaper password hash (new hashes only; the
    /// hashes of the original database keep their own format).
    /// </summary>
    public static IServiceCollection AddTestBusinessServices(this IServiceCollection services, FakeTimeProvider clock)
    {
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPasswordHasher>(new PasswordHasher(iterations: 1_000));
        services.AddSingleton(new SetupKeyThrottle(TimeSpan.Zero));
        services.AddSingleton(new SetupKeyEnvironment(null));
        services.AddBusinessServices(Policy);
        return services;
    }

    /// <summary>Creates the first admin ("yonetici") through the first-run setup of an empty database.</summary>
    public static Task<UserSession> SetUpAdminAsync(IServiceProvider provider, string name = "yonetici") =>
        provider.GetRequiredService<ISetupService>().CreateFirstAdminAsync(null, name, Password, Password, TestContext.Current.CancellationToken);

    /// <summary>Registers a customer through the real service and returns its session.</summary>
    public static async Task<UserSession> RegisterAsync(IServiceProvider provider, string name, int number)
    {
        var ct = TestContext.Current.CancellationToken;
        var phone = "554" + number.ToString("0000000", CultureInfo.InvariantCulture);
        await provider.GetRequiredService<IAuthService>().RegisterAsync(name, Password, name + "@example.com", phone, ct);
        return await provider.GetRequiredService<IAuthService>().SignInAsync(name, Password, ct);
    }

    /// <summary>Adds a car through the real service and returns its id.</summary>
    public static Task<int> AddCarAsync(IServiceProvider provider, UserSession admin, string plate, string dailyPrice = "100") =>
        provider.GetRequiredService<ICarService>().AddAsync(
            admin,
            new CarInput("Anka", "Deneme", "2022", "Manuel", "Benzin", "1000", "Gri", plate, dailyPrice, null, true),
            TestContext.Current.CancellationToken);

    /// <summary>The account's session version as stored now, as the admin's form loads it with the account.</summary>
    public static async Task<int> AccountVersionAsync(IServiceProvider provider, int userId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await provider.GetRequiredService<IDbContextFactory<AracDbContext>>().CreateDbContextAsync(ct);
        return await db.Users.Where(u => u.Id == userId).Select(u => u.SessionVersion).SingleAsync(ct);
    }

    /// <summary>The car's version as stored now, as the admin's form loads it with the car.</summary>
    public static async Task<int> CarVersionAsync(IServiceProvider provider, int carId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = await provider.GetRequiredService<IDbContextFactory<AracDbContext>>().CreateDbContextAsync(ct);
        return await db.Cars.Where(c => c.Id == carId).Select(c => c.Version).SingleAsync(ct);
    }

    /// <summary>Books <paramref name="carId"/> from today plus <paramref name="startOffset"/> days for <paramref name="days"/> days.</summary>
    public static async Task<RentalReceipt> RentAsync(IServiceProvider provider, UserSession session, int carId, int startOffset = 0, int days = 3)
    {
        var ct = TestContext.Current.CancellationToken;
        var car = await provider.GetRequiredService<ICarService>().GetAsync(session, carId, ct);
        var rentals = provider.GetRequiredService<IRentalService>();
        return await rentals.RentAsync(session, rentals.Quote(carId, car.DailyPrice, rentals.Today.AddDays(startOffset), days), ct);
    }

    /// <summary>
    /// Stores a new setup key as the database owner's script does (16 random bytes, SHA-256 of the 32
    /// hexadecimal characters, 24 hours from <paramref name="now"/>) and returns it in groups of four.
    /// On SQL Server the tests run the script itself; this stands in for it where there is no T-SQL.
    /// </summary>
    public static async Task<string> AddSetupKeyAsync(IServiceProvider provider, DateTimeOffset now)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await using var db = await provider.GetRequiredService<IDbContextFactory<AracDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.SetupKeys.Add(new SetupKey
        {
            KeyHash = SHA256.HashData(Encoding.ASCII.GetBytes(key)),
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.UtcDateTime + SetupKeyPolicy.Lifetime,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return SetupKeyPolicy.Format(key);
    }
}
