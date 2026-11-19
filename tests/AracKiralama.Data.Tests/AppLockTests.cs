using AracKiralama.Data.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AracKiralama.Data.Tests;

/// <summary>sp_getapplock return codes; no database is needed.</summary>
public sealed class AppLockTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EnsureGranted_AcceptsGrantedCodes(int code)
    {
        AppLock.EnsureGranted(code, "AracKiralama.Car.1");
    }

    [Theory]
    [InlineData(-1, "timed out")]
    [InlineData(-2, "canceled")]
    [InlineData(-3, "deadlock")]
    [InlineData(-999, "invalid")]
    [InlineData(null, "no result")]
    public void EnsureGranted_RejectsFailureCodes(int? code, string reason)
    {
        var ex = Assert.Throws<AppLockNotGrantedException>(() => AppLock.EnsureGranted(code, "AracKiralama.Car.1"));

        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
        Assert.Contains("No changes were made", ex.Message, StringComparison.Ordinal);
    }
}

/// <summary>The command timeout of a lock request is always longer than the lock's own wait.</summary>
public sealed class AppLockTimeoutTests
{
    [Theory]
    [InlineData(null, 30_000, 60)] // SqlClient's default of 30 seconds is too short for a 30 second wait.
    [InlineData(5, 30_000, 60)]
    [InlineData(59, 30_000, 60)]
    [InlineData(60, 30_000, null)]
    [InlineData(90, 60_000, null)]
    [InlineData(0, 60_000, null)] // No limit.
    [InlineData(1, 3_000, 33)]
    [InlineData(1, 2_500, 33)]
    public void CommandTimeoutFor_RaisesOnlyATooShortTimeout(int? configured, int lockWait, int? expected) =>
        Assert.Equal(expected, AppLock.CommandTimeoutFor(configured, lockWait));

    /// <summary>
    /// Another session holds a car's lock. A request with a one second command timeout and a three second
    /// lock wait ends with the server's refusal after the wait, not with a client side command timeout,
    /// and the context gets its own timeout back.
    /// </summary>
    [Fact]
    public async Task Acquire_BusyLockWithAShortCommandTimeout_EndsWithTheLocksRefusal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync(ct);
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync(ct);
        await using (var take = new SqlCommand("EXEC sp_getapplock @Resource = N'AracKiralama.Car.7', @LockMode = 'Exclusive', @LockOwner = 'Transaction';", holder, held))
        {
            await take.ExecuteNonQueryAsync(ct);
        }

        await using var services = database.BuildServices();
        var factory = services.GetRequiredService<IDbContextFactory<AracDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(1);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<AppLockNotGrantedException>(() => AppLock.AcquireAsync(db, "AracKiralama.Car.7", 3_000, ct));

        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(2.5));
        Assert.Equal(1, db.Database.GetCommandTimeout());
    }
}
