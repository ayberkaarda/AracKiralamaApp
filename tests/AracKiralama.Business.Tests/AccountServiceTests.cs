using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
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

        // By default the stored account has the values of Valid.
        _users.Setup(u => u.GetSummaryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new UserSummary(id, "yeni", "yeni@example.com", "5550000009", UserRole.User));
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
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, new AccountUpdate(4, "yeni", "yeni@example.com", "5550000009", UserRole.Admin, null), It.IsAny<CancellationToken>()))
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
        Stored(6, "eski kullanici", null, null);
        _users.Setup(u => u.FindTakenAsync("eski kullanici", null, null, 6, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.Is<AccountUpdate>(a => a.UserId == 6 && a.Email == null && a.Phone == null), It.IsAny<CancellationToken>()))
            .Callback<int, AccountUpdate, CancellationToken>((_, update, _) => hash = update.NewPasswordHash)
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 6, new AccountInput("eski kullanici", "Yeni.Sifre12", " ", "", UserRole.User), Ct);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Yeni.Sifre12", hash!, PasswordFormat.Pbkdf2));
    }

    /// <summary>
    /// A value of the original data that the rules would refuse, such as a phone with brackets or an
    /// e-mail address with two @, is kept when the admin leaves it as it is, so the account can still
    /// get a new password. A changed value follows the rules.
    /// </summary>
    [Fact]
    public async Task Update_LegacyAccountWithMalformedContactData_CanGetANewPasswordWhenTheyAreUnchanged()
    {
        AccountUpdate? sent = null;
        Stored(6, "eski", "eski@@ornek", "(555)1234567");
        _users.Setup(u => u.FindTakenAsync("eski", "eski@@ornek", "(555)1234567", 6, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<int, AccountUpdate, CancellationToken>((_, update, _) => sent = update)
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 6, new AccountInput("eski", "Yeni.Sifre12", "eski@@ornek", "(555)1234567", UserRole.User), Ct);

        Assert.NotNull(sent);
        Assert.Equal("(555)1234567", sent.Phone);
        Assert.Equal("eski@@ornek", sent.Email);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Yeni.Sifre12", sent.NewPasswordHash!, PasswordFormat.Pbkdf2));

        var phone = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().UpdateAsync(Sessions.Admin, 6, new AccountInput("eski", null, "eski@@ornek", "(555)123456", UserRole.User), Ct));
        var email = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().UpdateAsync(Sessions.Admin, 6, new AccountInput("eski", null, "yeni@@ornek", "(555)1234567", UserRole.User), Ct));
        Assert.Equal(AccountRules.PhoneInvalid, phone.Message);
        Assert.Equal(AccountRules.EmailInvalid, email.Message);
        _users.Verify(u => u.UpdateAsync(It.IsAny<int>(), It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>An account may keep a name of the original data, but a new name follows the rules.</summary>
    [Theory]
    [InlineData("a")]
    [InlineData("ali veli")]
    [InlineData("eski  kullanici")]
    public async Task Update_RenameToAnInvalidName_IsRefused(string newName)
    {
        Stored(6, "eski kullanici", null, null);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => Service().UpdateAsync(Sessions.Admin, 6, new AccountInput(newName, null, null, null, UserRole.User), Ct));

        Assert.Equal(AccountRules.UserNameInvalid, error.Message);
        _users.Verify(u => u.UpdateAsync(It.IsAny<int>(), It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_MissingAccount_IsNotFound()
    {
        _users.Setup(u => u.GetSummaryAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync((UserSummary?)null);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 9, Valid, Ct));

        Assert.Equal(AccountService.NotFound, error.Message);
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
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>()))
            .Callback<int, AccountUpdate, CancellationToken>((_, update, _) => hash = update.NewPasswordHash)
            .ReturnsAsync(AccountUpdateOutcome.Updated);

        await Service().UpdateAsync(Sessions.Admin, 4, Valid, Ct);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.Verify("Gizli.Sifre1", hash!, PasswordFormat.Pbkdf2));
    }

    [Fact]
    public async Task Update_LastAdmin_KeepsTheRole()
    {
        _users.Setup(u => u.FindTakenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), 1, It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>())).ReturnsAsync(AccountUpdateOutcome.LastAdmin);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().UpdateAsync(Sessions.Admin, 1, Valid with { Password = null }, Ct));

        Assert.Equal(AccountService.LastAdmin, error.Message);
    }

    [Theory]
    [InlineData(AccountDeleteOutcome.IsAdmin, AccountService.AdminNotDeletable)]
    [InlineData(AccountDeleteOutcome.HasRentals, AccountService.HasRentals)]
    [InlineData(AccountDeleteOutcome.NotFound, AccountService.NotFound)]
    public async Task Delete_RefusedByTheRepository_BecomesARuleMessage(AccountDeleteOutcome outcome, string message)
    {
        _users.Setup(u => u.DeleteAsync(Sessions.Admin.UserId, 5, It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service().DeleteAsync(Sessions.Admin, 5, Ct));

        Assert.Equal(message, error.Message);
    }

    /// <summary>
    /// The admin passed the check at the start but the repository finds the role gone at the write:
    /// nothing was written, and the answer is a denied access, recorded like any other.
    /// </summary>
    [Fact]
    public async Task Write_ByAnAdminDemotedBeforeTheWrite_IsDenied()
    {
        _users.Setup(u => u.FindTakenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(AccountFields.None);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), Sessions.Admin.UserId, It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());
        _users.Setup(u => u.UpdateAsync(Sessions.Admin.UserId, It.IsAny<AccountUpdate>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());
        _users.Setup(u => u.DeleteAsync(Sessions.Admin.UserId, 5, It.IsAny<CancellationToken>())).ThrowsAsync(new ActorRevokedException());
        var service = Service();

        await Assert.ThrowsAsync<AccessDeniedException>(() => service.AddAsync(Sessions.Admin, Valid with { Role = UserRole.Admin }, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.UpdateAsync(Sessions.Admin, 4, Valid with { Role = UserRole.Admin }, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.DeleteAsync(Sessions.Admin, 5, Ct));

        Assert.Equal(["access.denied admin", "access.denied admin", "access.denied admin"], _audit.Entries);
    }

    [Fact]
    public async Task Add_UnknownRole_IsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service().AddAsync(Sessions.Admin, Valid with { Role = (UserRole)7 }, Ct));

        AssertOnlyRoleChecks();
    }

    private void Stored(int id, string name, string? email, string? phone, UserRole role = UserRole.User) =>
        _users.Setup(u => u.GetSummaryAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new UserSummary(id, name, email, phone, role));

    private void AssertOnlyRoleChecks()
    {
        _users.Verify(u => u.GetRoleAsync(Sessions.Admin.UserId, It.IsAny<CancellationToken>()), Times.AtLeastOnce());
        _users.VerifyNoOtherCalls();
    }

    private AccountService Service() => new(_users.Object, _hasher, new AccessGuard(_users.Object, _audit), _audit, TestValidators.NewAccount, TestValidators.AccountChange);
}
