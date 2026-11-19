using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Persistence;

/// <summary>
/// SQL Server's UTC clock (SYSUTCDATETIME), the one the setup key script also uses. Other providers
/// (the tests' SQLite) have no server clock and use <see cref="TimeProvider"/> instead.
/// </summary>
internal sealed class DatabaseClock(IDbContextFactory<AracDbContext> contextFactory, TimeProvider timeProvider) : IDatabaseClock
{
    public async Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(db, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The clock read on <paramref name="db"/>'s own connection, so a write can stamp its rows with the
    /// time inside its transaction, after it got its locks.
    /// </summary>
    internal static async Task<DateTime> ReadAsync(AracDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            return timeProvider.GetUtcNow().UtcDateTime;
        }

        var now = await db.Database
            .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        return DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    /// <summary>
    /// Today's date by the clock read on <paramref name="db"/>'s own connection, in the computer's time zone
    /// (rental and maintenance days are local calendar days). A write that decides with today's date reads
    /// it here, inside its transaction and after it got its locks, so a request that waited for a lock
    /// across midnight decides with the date of its write.
    /// </summary>
    internal static async Task<DateOnly> ReadTodayAsync(AracDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var utc = await ReadAsync(db, timeProvider, cancellationToken).ConfigureAwait(false);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeProvider.LocalTimeZone));
    }
}
