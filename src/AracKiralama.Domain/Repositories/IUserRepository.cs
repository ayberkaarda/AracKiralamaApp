using AracKiralama.Domain.Entities;

namespace AracKiralama.Domain.Repositories;

/// <summary>Accounts and their sign-in state.</summary>
public interface IUserRepository
{
    Task<AccountCredentials?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    Task<AccountCredentials?> FindByIdAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's role and session version as stored now, or <see langword="null"/> when the account
    /// no longer exists. The services ask for it at the start of every operation, so a session whose
    /// account was deleted, demoted or given a new password after sign-in loses the rights at once.
    /// </summary>
    Task<SessionState?> GetSessionStateAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The account's stored name, contact data and role, or <see langword="null"/> when it does not exist.</summary>
    Task<UserSummary?> GetSummaryAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Every account, ordered by id, without passwords.</summary>
    Task<IReadOnlyList<UserSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Which of the given values another account (not <paramref name="exceptUserId"/>) already uses.</summary>
    Task<AccountFields> FindTakenAsync(string userName, string? email, string? phone, int? exceptUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new account and returns its id. An account added by an admin names that admin in
    /// <paramref name="admin"/>, who must still be an admin at the write; a sign-up passes
    /// <see langword="null"/>.
    /// </summary>
    /// <exception cref="DuplicateAccountException">The user name, e-mail or phone is taken.</exception>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<int> AddAsync(User user, Actor? admin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an account's name, contact data and role and, when <see cref="AccountUpdate.NewPasswordHash"/>
    /// is set, its password. The last admin cannot lose the admin role, and an account whose session
    /// version is no longer <see cref="AccountUpdate.ExpectedVersion"/> is not changed: the checks and the
    /// write are one conditional UPDATE, so two concurrent demotions cannot both succeed and a stale form
    /// cannot give back a role another admin just removed. Account
    /// <paramref name="admin"/>, the admin making the change, must still be an admin at the write,
    /// decided under the lock every role change takes.
    /// </summary>
    /// <exception cref="DuplicateAccountException">The user name, e-mail or phone is taken.</exception>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<AccountUpdateOutcome> UpdateAsync(Actor admin, AccountUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an account that is no admin and has no rentals, in one conditional DELETE, while account
    /// <paramref name="admin"/> is still an admin.
    /// </summary>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<AccountDeleteOutcome> DeleteAsync(Actor admin, int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's own password change: replaces the password of <paramref name="verified"/> with
    /// <paramref name="newPasswordHash"/> and raises the session version, in one conditional UPDATE, only
    /// while the stored password is still the one that was verified and the session version is still
    /// <paramref name="sessionVersion"/> (the changing session's). Every other open session of the account
    /// ends with it.
    /// </summary>
    /// <returns>The new session version, or <see langword="null"/> when nothing was written.</returns>
    Task<int?> ChangePasswordAsync(AccountCredentials verified, int sessionVersion, string newPasswordHash, CancellationToken cancellationToken);

    /// <summary>Whether any account has the admin role.</summary>
    Task<bool> AnyAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether any account exists, of any role.</summary>
    Task<bool> AnyUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="admin"/> as the first admin account, only while no admin exists. The
    /// check and the insert are one atomic step, so of concurrent setups exactly one succeeds. An empty
    /// database needs no key. A database that already has accounts needs <paramref name="setupKey"/>:
    /// inside the same serializable write transaction, after the checks found no admin and a free user
    /// name, the key is compared with every usable stored key and the matching one is used up by a
    /// conditional UPDATE. When the insert or the commit fails the use rolls back with it; a wrong key
    /// writes nothing, so wrong keys can never revoke the founder's key. There is no way to create a
    /// key here: keys come only from the database owner's script.
    /// </summary>
    /// <exception cref="DuplicateAccountException">The user name is taken by another account.</exception>
    Task<FirstAdminResult> AddFirstAdminAsync(
        User admin,
        SetupKeyAttempt? setupKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts a failed sign-in of <paramref name="verified"/> in one conditional UPDATE. It is counted only
    /// while the account is not locked by the database's clock at the moment of the write and still has the
    /// password value, format and session version that were checked: a failure that arrives at an account
    /// locked meanwhile, or after an admin set a new password, changes nothing. When the count reaches
    /// <paramref name="maxAttempts"/> the account is locked for <paramref name="lockoutDuration"/> from the
    /// database's clock at the write and the count starts again from zero.
    /// </summary>
    /// <returns>Whether the failure was counted.</returns>
    Task<bool> RecordFailedLoginAsync(AccountCredentials verified, int maxAttempts, TimeSpan lockoutDuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful sign-in in one conditional UPDATE: only while the account is not locked at
    /// <paramref name="now"/> and still has the password value and format of <paramref name="verified"/>
    /// are the failed attempts reset. When <paramref name="newPasswordHash"/> is given the stored
    /// password is replaced with it and its format becomes <see cref="PasswordFormat.Pbkdf2"/> in the
    /// same statement (upgrade of a plain text or weaker hash).
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the account changed after it was read (for example a concurrent
    /// failure locked it); nothing is written then and the sign-in must be refused.
    /// </returns>
    Task<bool> RecordSuccessfulLoginAsync(
        AccountCredentials verified,
        DateTime now,
        string? newPasswordHash,
        CancellationToken cancellationToken = default);
}

/// <summary>Account values that must be unique.</summary>
[Flags]
public enum AccountFields
{
    None = 0,
    UserName = 1,
    Email = 2,
    Phone = 4,
}

/// <summary>
/// An admin's change of an account, made in a form that loaded the account with session version
/// <see cref="ExpectedVersion"/>.
/// </summary>
public sealed record AccountUpdate(int UserId, int ExpectedVersion, string UserName, string? Email, string? Phone, UserRole Role, string? NewPasswordHash);

public enum AccountUpdateOutcome
{
    Updated = 0,
    NotFound = 1,

    /// <summary>The account is the only admin and would lose the role; nothing was written.</summary>
    LastAdmin = 2,

    /// <summary>
    /// The account's role or password changed since the form loaded it (its session version is no longer
    /// <see cref="AccountUpdate.ExpectedVersion"/>); nothing was written.
    /// </summary>
    ChangedElsewhere = 3,
}

public enum AccountDeleteOutcome
{
    Deleted = 0,
    NotFound = 1,

    /// <summary>Admin accounts are never deleted, as in the original application.</summary>
    IsAdmin = 2,

    /// <summary>The account has rentals; their history keeps the account.</summary>
    HasRentals = 3,
}

/// <summary>Outcome of <see cref="IUserRepository.AddFirstAdminAsync"/>.</summary>
public enum FirstAdminStatus
{
    /// <summary>The admin was stored; its id is in <see cref="FirstAdminResult.AdminId"/>.</summary>
    Created = 0,

    /// <summary>An admin existed already; nothing was written.</summary>
    AdminExists = 1,

    /// <summary>The database has accounts and the setup key was wrong, expired or used; nothing was written.</summary>
    NotAuthorized = 2,

    /// <summary>The database has accounts, so a setup key is needed and none was given; nothing was written.</summary>
    KeyRequired = 3,
}

/// <summary>
/// A setup key given by someone, checked by a repository inside its write transaction. The repository
/// passes every key usable now (unused and unexpired); <see cref="FindMatch"/> compares the given key
/// with each of them in constant time and returns the id of the matching one, or <see langword="null"/>.
/// Wrong keys are never counted against a stored key, so nobody can make the founder's key unusable by
/// guessing.
/// </summary>
public sealed class SetupKeyAttempt
{
    private readonly Func<IReadOnlyList<StoredSetupKey>, int?> _findMatch;

    public SetupKeyAttempt(Func<IReadOnlyList<StoredSetupKey>, int?> findMatch, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(findMatch);

        _findMatch = findMatch;
        Now = now;
    }

    /// <summary>
    /// UTC time of the attempt by the computer's clock. On SQL Server it plays no part: both the
    /// selection of the usable keys and the final UPDATE that uses one up compare the expiry with the
    /// database's clock, at the moment of use. Providers without such a clock (the tests' SQLite) use
    /// this time instead.
    /// </summary>
    public DateTime Now { get; }

    public int? FindMatch(IReadOnlyList<StoredSetupKey> usableKeys) => _findMatch(usableKeys);
}

/// <summary>Result of an attempt to store the first admin.</summary>
public readonly record struct FirstAdminResult(FirstAdminStatus Status, int AdminId);

/// <summary>Raised when a user name, e-mail or phone number is already used by another account.</summary>
public sealed class DuplicateAccountException : Exception
{
    public DuplicateAccountException()
        : this(AccountFields.UserName)
    {
    }

    public DuplicateAccountException(string message)
        : base(message)
    {
        Fields = AccountFields.UserName;
    }

    public DuplicateAccountException(string message, Exception innerException)
        : base(message, innerException)
    {
        Fields = AccountFields.UserName;
    }

    public DuplicateAccountException(AccountFields fields, Exception? innerException = null)
        : base("An account value is already registered.", innerException)
    {
        Fields = fields;
    }

    /// <summary>The values that are taken.</summary>
    public AccountFields Fields { get; }
}
