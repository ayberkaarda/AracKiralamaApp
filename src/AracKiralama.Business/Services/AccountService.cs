using AracKiralama.Business.Auditing;
using AracKiralama.Business.Security;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using FluentValidation;

namespace AracKiralama.Business.Services;

/// <summary>An account as the admin's account form enters it; an empty password keeps the current one on a change.</summary>
public sealed record AccountInput(string? UserName, string? Password, string? Email, string? Phone, UserRole Role);

/// <summary>The admin's account management. Passwords are never read back, only replaced.</summary>
public interface IAccountService
{
    Task<IReadOnlyList<UserSummary>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Adds an account with any role; name, password and role are required, e-mail and phone optional.</summary>
    Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default);

    /// <summary>Changes an account; the last admin keeps the admin role.</summary>
    Task UpdateAsync(UserSession session, int userId, AccountInput input, CancellationToken cancellationToken = default);

    /// <summary>Deletes an account that is no admin and has no rentals.</summary>
    Task DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken = default);
}

internal sealed class AccountService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    AccessGuard access,
    IAuditLog audit,
    IValidator<NewAccountInput> newAccountValidator,
    IValidator<AccountChangeInput> accountChangeValidator) : IAccountService
{
    internal const string NotFound = "Kullanıcı bulunamadı.";
    internal const string LastAdmin = "Sistemde en az bir yönetici kalmalıdır; son yöneticinin rolü değiştirilemez.";
    internal const string AdminNotDeletable = "Yönetici rolündeki kullanıcılar silinemez.";
    internal const string HasRentals = "Kiralama geçmişi olan kullanıcı silinemez.";

    public async Task<IReadOnlyList<UserSummary>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        return await users.GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);

        // Admins may give any name the rules allow, also the one the sign-up form keeps.
        var account = NewAccountInput.Create(input);
        newAccountValidator.ValidateOrThrow(account);
        var (name, email, phone) = (account.UserName, account.Email, account.Phone is null ? null : InputRules.NormalizePhone(account.Phone));
        await RequireFreeAsync(name, email, phone, null, cancellationToken).ConfigureAwait(false);

        try
        {
            var user = new User
            {
                UserName = name,
                PasswordHash = passwordHasher.Hash(account.Password!),
                PasswordFormat = PasswordFormat.Pbkdf2,
                Role = input.Role,
                Email = email,
                Phone = phone,
            };
            var id = await access.WriteAsActorAsync(session, () => users.AddAsync(user, session.UserId, cancellationToken)).ConfigureAwait(false);
            audit.Record(AuditEvent.AccountChanged, AuditRole.Admin);
            return id;
        }
        catch (DuplicateAccountException ex)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(ex.Fields), ex);
        }
    }

    public async Task UpdateAsync(UserSession session, int userId, AccountInput input, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(input);

        var stored = await users.GetSummaryAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException(NotFound);
        var change = AccountChangeInput.Create(input, stored);
        accountChangeValidator.ValidateOrThrow(change);
        var name = change.UserName;
        var email = change.Email;
        var phone = change.Phone is null || change.KeepsStoredPhone ? change.Phone : InputRules.NormalizePhone(change.Phone);
        var newHash = string.IsNullOrEmpty(change.Password) ? null : passwordHasher.Hash(change.Password);

        await RequireFreeAsync(name, email, phone, userId, cancellationToken).ConfigureAwait(false);

        AccountUpdateOutcome outcome;
        try
        {
            var update = new AccountUpdate(userId, name, email, phone, input.Role, newHash);
            outcome = await access.WriteAsActorAsync(session, () => users.UpdateAsync(session.UserId, update, cancellationToken)).ConfigureAwait(false);
        }
        catch (DuplicateAccountException ex)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(ex.Fields), ex);
        }

        switch (outcome)
        {
            case AccountUpdateOutcome.Updated:
                audit.Record(AuditEvent.AccountChanged, AuditRole.Admin);
                return;
            case AccountUpdateOutcome.LastAdmin:
                throw new BusinessRuleException(LastAdmin);
            default:
                throw new BusinessRuleException(NotFound);
        }
    }

    public async Task DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        var outcome = await access.WriteAsActorAsync(session, () => users.DeleteAsync(session.UserId, userId, cancellationToken)).ConfigureAwait(false);
        switch (outcome)
        {
            case AccountDeleteOutcome.Deleted:
                audit.Record(AuditEvent.AccountChanged, AuditRole.Admin);
                return;
            case AccountDeleteOutcome.IsAdmin:
                throw new BusinessRuleException(AdminNotDeletable);
            case AccountDeleteOutcome.HasRentals:
                throw new BusinessRuleException(HasRentals);
            default:
                throw new BusinessRuleException(NotFound);
        }
    }

    private async Task RequireFreeAsync(string name, string? email, string? phone, int? exceptUserId, CancellationToken cancellationToken)
    {
        var taken = await users.FindTakenAsync(name, email, phone, exceptUserId, cancellationToken).ConfigureAwait(false);
        if (taken != AccountFields.None)
        {
            throw new BusinessRuleException(AccountRules.DescribeTaken(taken));
        }
    }
}