using System.Data;
using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Persistence;

/// <summary>
/// Optional demonstration data, inserted only when it is asked for explicitly (ARAC_DEMO=1 or
/// <c>Seed:Demo</c> = true) and only into a database without any account, car or rental.
/// </summary>
/// <remarks>
/// Only cars are added, never an account: neither the migrations nor the demonstration data create an
/// account, so no installation has a default or documented password. The first admin is created by the
/// first-run setup with a password of the installer's own choosing, and customers sign up themselves.
/// The brands and models are made up, the plates use the province code 00 that no real plate has, and
/// there are no pictures.
/// </remarks>
internal static class DemoData
{
    /// <summary>
    /// Adds the demonstration cars when the database is empty. Returns <see langword="false"/> when it
    /// already holds data; nothing is changed then. The emptiness check and the inserts share one
    /// serializable transaction (with update locks on SQL Server), so concurrent starts add the rows once
    /// and a failure leaves none of them behind.
    /// </summary>
    public static async Task<bool> SeedIfEmptyAsync(AracDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            db,
            static async (context, token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                if (!await IsEmptyAsync(context, token).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return false;
                }

                // One row per save keeps the identity values in this order on a new database.
                foreach (var car in Cars())
                {
                    context.Cars.Add(car);
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The demonstration cars, in id order.</summary>
    internal static IEnumerable<Car> Cars()
    {
        yield return Car("Anka", "Kent", 2021, "Otomatik", "Benzin", 32_000, "Beyaz", "00 DMO 001", 850m);
        yield return Car("Anka", "Yol", 2019, "Manuel", "Dizel", 61_000, "Gri", "00 DMO 002", 700m);
        yield return Car("Kartal", "Kasa", 2022, "Otomatik", "Dizel", 18_500, "Siyah", "00 DMO 003", 1_250m);
        yield return Car("Poyraz", "Mini", 2020, "Manuel", "Benzin", 45_000, "Kırmızı", "00 DMO 004", 650m);
        yield return Car("Toros", "Volt", 2023, "Otomatik", "Elektrik", 9_000, "Mavi", "00 DMO 005", 1_400m);
    }

    private static async Task<bool> IsEmptyAsync(AracDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            return !await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false)
                && !await db.Cars.AnyAsync(cancellationToken).ConfigureAwait(false)
                && !await db.Rentals.AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        var rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT (SELECT COUNT(*) FROM dbo.Kullanicilar WITH (UPDLOCK, HOLDLOCK))
                     + (SELECT COUNT(*) FROM dbo.Araclar WITH (UPDLOCK, HOLDLOCK))
                     + (SELECT COUNT(*) FROM dbo.Kiralama WITH (UPDLOCK, HOLDLOCK)) AS [Value]
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows == 0;
    }

    private static Car Car(
        string brand,
        string model,
        int year,
        string transmission,
        string fuelType,
        int mileage,
        string color,
        string plate,
        decimal dailyPrice) =>
        new()
        {
            Brand = brand,
            Model = model,
            Year = year,
            Transmission = transmission,
            FuelType = fuelType,
            Mileage = mileage,
            Color = color,
            Plate = plate,
            DailyPrice = dailyPrice,
            IsListed = true,
        };
}
