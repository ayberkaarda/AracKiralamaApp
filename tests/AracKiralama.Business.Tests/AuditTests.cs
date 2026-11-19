using AracKiralama.Business.Auditing;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace AracKiralama.Business.Tests;

/// <summary>
/// The audit codes: fixed, lower case and free of identities. Outcomes that would tell whether an
/// account exists share one entry.
/// </summary>
public sealed class AuditTests
{
    private const string Password = "Gizli.Sifre1";
    private static readonly DateTime Now = new(2026, 3, 2, 10, 10, 0, DateTimeKind.Utc);

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly PasswordHasher _hasher = new(iterations: 1_000);
    private readonly RecordingAuditLog _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Codes_AreFixed()
    {
        Assert.Equal(
            [
                "auth.sign_in.succeeded", "auth.sign_in.failed", "auth.password.upgraded", "auth.account.registered",
                "auth.password.changed", "auth.password.change_refused", "setup.first_admin.created", "setup.key.refused",
                "access.denied", "car.changed", "rental.created", "rental.overlap_refused", "rental.ended",
                "account.changed", "auth.account.registration_not_completed", "maintenance.changed", "maintenance.refused",
                "report.revenue.created", "rental.contract.created",
            ],
            Enum.GetValues<AuditEvent>().Select(AuditCodes.Of));
        Assert.Equal(["unknown", "user", "admin"], Enum.GetValues<AuditRole>().Select(AuditCodes.Of));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuditCodes.Of((AuditEvent)99));
    }

    /// <summary>Only the two sign-up outcomes are merged in the log file; every other event keeps its code.</summary>
    [Fact]
    public void PublicLog_MergesTheSignUpOutcomesOnly()
    {
        Assert.Equal(AuditCodes.RegistrationSubmitted, AuditCodes.ForPublicLog(AuditEvent.AccountRegistered));
        Assert.Equal(AuditCodes.RegistrationSubmitted, AuditCodes.ForPublicLog(AuditEvent.RegistrationNotCompleted));
        Assert.All(
            Enum.GetValues<AuditEvent>().Except([AuditEvent.AccountRegistered, AuditEvent.RegistrationNotCompleted]),
            e => Assert.Equal(AuditCodes.Of(e), AuditCodes.ForPublicLog(e)));
        Assert.Equal(Enum.GetValues<AuditEvent>().Length - 1, Enum.GetValues<AuditEvent>().Select(AuditCodes.ForPublicLog).Distinct().Count());
    }

    /// <summary>
    /// Every refused sign-in, from an empty field to an account locked by a concurrent failure, leaves
    /// the same code without a role.
    /// </summary>
    [Fact]
    public async Task SignIn_EveryRefusal_RecordsTheSameRolelessCode()
    {
        var hash = _hasher.Hash(Password);
        _users.Setup(u => u.FindByUserNameAsync("kimse", It.IsAny<CancellationToken>())).ReturnsAsync((AccountCredentials?)null);
        _users.Setup(u => u.FindByUserNameAsync("kilitli", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(8, "kilitli", UserRole.Admin, hash, PasswordFormat.Pbkdf2, 5, Now.AddMinutes(10), 0));
        _users.Setup(u => u.FindByUserNameAsync("ornek", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(7, "ornek", UserRole.Admin, hash, PasswordFormat.Pbkdf2, 0, null, 0));
        _users.Setup(u => u.FindByUserNameAsync("yaris", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(9, "yaris", UserRole.User, hash, PasswordFormat.Pbkdf2, 0, null, 0));
        _users.Setup(u => u.RecordFailedLoginAsync(It.Is<AccountCredentials>(a => a.Id == 7), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.Is<AccountCredentials>(a => a.Id == 9), It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        List<Func<Task>> refusals =
        [
            () => Service().SignInAsync("", Password, Ct),
            () => Service().SignInAsync("ornek", null, Ct),
            () => Service().SignInAsync(new string('a', 51), Password, Ct),
            () => Service().SignInAsync("ornek", new string('x', PasswordPolicy.MaxLength + 1), Ct),
            () => Service().SignInAsync("kimse", Password, Ct),
            () => Service().SignInAsync("kilitli", Password, Ct),
            () => Service().SignInAsync("ornek", "Yanlis.Sifre1", Ct),
            () => Service().SignInAsync("yaris", Password, Ct),
        ];
        foreach (var refusal in refusals)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(refusal);
        }

        Assert.Equal(refusals.Count, _audit.Entries.Count);
        Assert.All(_audit.Entries, e => Assert.Equal("auth.sign_in.failed unknown", e));
    }

    /// <summary>A new account and a sign-up with taken values look the same in the log file.</summary>
    [Fact]
    public async Task SignUp_NewAndTaken_LeaveTheSamePublicCode()
    {
        _users.Setup(u => u.FindTakenAsync("yeni", "yeni@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.AddAsync(It.Is<User>(x => x.UserName == "yeni"), null, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _users.Setup(u => u.FindTakenAsync("alinmis", "yeni@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.UserName);
        _users.Setup(u => u.FindTakenAsync("yaris", "yeni@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.AddAsync(It.Is<User>(x => x.UserName == "yaris"), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateAccountException(AccountFields.Email));
        var publicLog = new PublicLog();
        var service = Service(publicLog);

        await service.RegisterAsync("yeni", Password, "yeni@example.com", "5551234567", Ct);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync("alinmis", Password, "yeni@example.com", "5551234567", Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.RegisterAsync("yaris", Password, "yeni@example.com", "5551234567", Ct));

        Assert.Equal(["auth.account.registration_submitted user", "auth.account.registration_submitted user", "auth.account.registration_submitted user"], publicLog.Entries);
    }

    /// <summary>The detailed codes still tell the outcomes apart for anything that counts them in process.</summary>
    [Fact]
    public async Task SignUp_TakenValues_AreRecordedAsNotCompleted()
    {
        _users.Setup(u => u.FindTakenAsync("alinmis", "yeni@example.com", "5551234567", null, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.UserName | AccountFields.Phone);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().RegisterAsync("alinmis", Password, "yeni@example.com", "555 123 4567", Ct));

        Assert.Equal(["auth.account.registration_not_completed user"], _audit.Entries);
        Assert.Contains("Bu kullanıcı adı zaten mevcut.", error.Message, StringComparison.Ordinal);
        Assert.Contains("Bu telefon numarası zaten kayıtlı.", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("e-posta", error.Message, StringComparison.Ordinal);
    }

    private AuthService Service(IAuditLog? audit = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(Now));
        return new(
            _users.Object,
            _hasher,
            new TestDatabaseClock(time),
            new AccessGuard(_users.Object, audit ?? _audit),
            audit ?? _audit,
            TestValidators.SignIn,
            TestValidators.SignUp,
            TestValidators.PasswordChange);
    }

    /// <summary>Collects events as the application's log file writes them.</summary>
    private sealed class PublicLog : IAuditLog
    {
        public List<string> Entries { get; } = [];

        public void Record(AuditEvent auditEvent, AuditRole role) =>
            Entries.Add(AuditCodes.ForPublicLog(auditEvent) + " " + AuditCodes.Of(role));
    }
}
