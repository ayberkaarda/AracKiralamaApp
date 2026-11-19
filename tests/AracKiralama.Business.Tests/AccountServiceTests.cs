using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Moq;

namespace AracKiralama.Business.Tests;

public sealed class AccountServiceTests
{
    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly PasswordHasher _hasher = new(iterations: 1_000);
    private readonly RecordingAuditLog _audit = new();

    public AccountServiceTests()
    {
        _users.Setup(u => u.GetRoleAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(UserRole.Admin);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AccountInput Valid => new("yeni", "Gizli.Sifre1", "yeni@example.com", "555 000 0009", UserRole.User);

    [Fact]
    public async Task EveryOperation_ForAUser_IsDeniedBeforeAnyRepositoryCall()
    {
        var service = Service();

        await Assert.ThrowsAsync<AccessDeniedException>(() => service.GetAllAsync(Sessions.User, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.AddAsync(Sessions.User, Valid, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.UpdateAsync(Sessions.User, Sessions.User.UserId, Valid with { Role = UserRole.Admin }, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.DeleteAsync(Sessions.User, 5, Ct));

        _users.VerifyNoOtherCalls();
    }

    /// <summary>An admin demoted (or deleted) after signing in cannot manage accounts with the open session.</summary>
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(null)]
    public async Task DemotedAdminSession_CannotManageAccounts(UserRole? storedRole)
    {
        _users.Setup(u => u.GetRoleAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(storedRole);
        var service = Service();

        await Assert.ThrowsAsync<AccessDeniedException>(() => service.GetAllAsync(Sessions.Admin, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.AddAsync(Sessions.Admin, Valid with { Role = UserRole.Admin }, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.UpdateAsync(Sessions.Admin, Sessions.Admin.UserId, Valid with { Role = UserRole.Admin }, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.DeleteAsync(Sessions.Admin, 5, Ct));

        AssertOnlyRoleChecks();
        Assert.All(_audit.Entries, e => Assert.StartsWith("access.denied", e, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Add_WithoutPassword_IsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid with { Password = "" }, Ct));

        AssertOnlyRoleChecks();
    }

    [Fact]
    public async Task Update_WithoutPassword_KeepsTheStoredOne()
    {
        _users.Setup(u => u.FindTakenAsync("yeni", "yeni@example.com", "5550000009", 4, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(new AccountUpdate(4, "yeni", "yeni@example.com", "5550000009", UserRole.Admin, null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 4, Valid with { Password = "", Role = UserRole.Admin }, Ct);

        Assert.Equal(["account.changed admin"], _audit.Entries);
    }

    /// <summary>
    /// Accounts of the original application may have no e-mail or phone; the admin can still change them,
    /// for example to set a new password, and may keep a name that the sign-up rules would not allow.
    /// </summary>
    [Fact]
    public async Task Update_LegacyAccountWithoutContactData_CanGetANewPassword()
    {
        string? hash = null;
        _users.Setup(u => u.FindTakenAsync("eski kullanici", null, null, 6, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(It.Is<AccountUpdate>(a => a.UserId == 6 && a.Email == null && a.Phone == null), It.IsAny<CancellationToken>()))
            .Callback<AccountUpdate, CancellationToken>((update, _) => hash = update.NewPasswordHash)
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 6, new AccountInput("eski kullanici", "Yeni.Sifre12", " ", "", UserRole.User), Ct);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Yeni.Sifre12", hash!, PasswordFormat.Pbkdf2));
    }

    [Fact]
    public async Task Add_NewAccount_FollowsTheSignUpNameRules()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid with { UserName = "iki kelime" }, Ct));

        AssertOnlyRoleChecks();
    }

    [Fact]
    public async Task Update_WithPassword_StoresAHash()
    {
        string? hash = null;
        _users.Setup(u => u.FindTakenAsync("yeni", "yeni@example.com", "5550000009", 4, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<AccountUpdate, CancellationToken>((update, _) => hash = update.NewPasswordHash)
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 4, Valid, Ct);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Gizli.Sifre1", hash!, PasswordFormat.Pbkdf2));
    }

    [Fact]
    public async Task Update_LastAdmin_KeepsTheRole()
    {
        _users.Setup(u => u.FindTakenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), 1, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>())).ReturnsAsync(AccountUpdateOutcome.LastAdmin);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 1, Valid with { Password = null }, Ct));

        Assert.Equal(AccountService.LastAdmin, error.Message);
    }

    [Theory]
    [InlineData(AccountDeleteOutcome.IsAdmin, AccountService.AdminNotDeletable)]
    [InlineData(AccountDeleteOutcome.HasRentals, AccountService.HasRentals)]
    [InlineData(AccountDeleteOutcome.NotFound, AccountService.NotFound)]
    public async Task Delete_RefusedByTheRepository_BecomesARuleMessage(AccountDeleteOutcome outcome, string message)
    {
        _users.Setup(u => u.DeleteAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().DeleteAsync(Sessions.Admin, 5, Ct));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task Add_UnknownRole_IsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid with { Role = (UserRole)7 }, Ct));

        AssertOnlyRoleChecks();
    }

    private void AssertOnlyRoleChecks()
    {
        _users.Verify(u => u.GetRoleAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        _users.VerifyNoOtherCalls();
    }

    private AccountService Service() => new(_users.Object, _hasher, new AccessGuard(_users.Object, _audit), _audit);
}
