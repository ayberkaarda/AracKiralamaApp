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
}
