using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>
/// Failed sign-ins racing on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>). A
/// barrier holds every failure just before its write, after it has read the account and checked the
/// password, so all of them decided on the same unlocked account before any of them counts.
/// </summary>
public sealed class SqlServerSignInLockTests
{
    private const string WrongPassword = "Yanlis.Sifre1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Six wrong passwords arrive at once. The fifth counted failure locks the account for fifteen minutes
    /// by the database's clock; the sixth arrives at a locked account and is not counted, so the lock is
    /// not followed by a stray failure that would shorten the next round.
    /// </summary>
    [Fact]
    public async Task SixConcurrentFailures_LockTheAccountOnceAndTheSixthIsNotCounted()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var app = database.BuildServices();
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await RegisterAsync(app, "kilit", 1);
        var barrier = CommandBarrier.BeforeCommandsWith("UPDATE", "BasarisizGiris");
        await using var gated = database.BuildServices(configureContext: o => o.AddInterceptors(barrier));

        var failures = FailSignIns(gated, 6);
        await barrier.WaitForArrivalsAsync(6, Ct);
        barrier.Release();

        Assert.All(await Task.WhenAll(failures), e => Assert.Equal(AuthService.CredentialsInvalid, e.Message));
        Assert.Equal(1, await database.ScalarAsync(
            """
            SELECT COUNT(*) FROM dbo.Kullanicilar
            WHERE KullaniciAdi = N'kilit' AND BasarisizGiris = 0
              AND KilitBitis > DATEADD(MINUTE, 14, SYSUTCDATETIME()) AND KilitBitis <= DATEADD(MINUTE, 15, SYSUTCDATETIME())
            """,
            Ct));
    }

    /// <summary>
    /// Six wrong passwords are on their way to the write when an admin sets a new password, which lifts
    /// any lock and clears the count. The failures were checked against the old password: none of them is
    /// counted afterwards, so they neither undo the reset nor lock the account the admin just opened.
    /// </summary>
    [Fact]
    public async Task FailuresInFlightDuringAnAdminReset_AreNotCounted()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var app = database.BuildServices();
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var admin = await SetUpAdminAsync(app);
        var customer = await RegisterAsync(app, "kilit", 1);
        var barrier = CommandBarrier.BeforeCommandsWith("UPDATE", "BasarisizGiris");
        await using var gated = database.BuildServices(configureContext: o => o.AddInterceptors(barrier));

        var failures = FailSignIns(gated, 6);
        await barrier.WaitForArrivalsAsync(6, Ct);
        await app.GetRequiredService<IAccountService>().UpdateAsync(
            admin,
            customer.UserId,
            await AccountVersionAsync(app, customer.UserId),
            new AccountInput("kilit", "Yeni.Sifre123", "kilit@example.com", "5540000001", UserRole.User),
            Ct);
        barrier.Release();

        Assert.All(await Task.WhenAll(failures), e => Assert.Equal(AuthService.CredentialsInvalid, e.Message));
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'kilit' AND BasarisizGiris = 0 AND KilitBitis IS NULL",
            Ct));
        Assert.Equal("kilit", (await app.GetRequiredService<IAuthService>().SignInAsync("kilit", "Yeni.Sifre123", Ct)).UserName);
    }

    /// <summary>Starts <paramref name="count"/> sign-ins of "kilit" with a wrong password at once; each ends with its refusal.</summary>
    private static List<Task<BusinessRuleException>> FailSignIns(IServiceProvider services, int count) =>
        Enumerable.Range(0, count)
            .Select(_ => Task.Run(() => Assert.ThrowsAsync<BusinessRuleException>(() => services.GetRequiredService<IAuthService>().SignInAsync("kilit", WrongPassword, Ct)), Ct))
            .ToList();
}
