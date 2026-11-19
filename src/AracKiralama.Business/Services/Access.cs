using AracKiralama.Business.Auditing;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

namespace AracKiralama.Business.Services;

/// <summary>
/// Role checks of the services. A session only says who signed in; what the account may do is read from
/// the database at the start of every operation, so an admin who was demoted or deleted after signing in
/// can no longer manage cars, accounts or other people's rentals with the session that is still open.
/// </summary>
internal sealed class AccessGuard(IUserRepository users, IAuditLog audit)
{
    /// <summary>
    /// The account's current role. Refuses, and records, a session whose account no longer exists.
    /// </summary>
    public async Task<UserRole> RequireActiveAsync(UserSession? session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var role = await users.GetRoleAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        if (role is not { } current)
        {
            audit.Record(AuditEvent.AccessDenied, AuditCodes.Of(session.Role));
            throw new AccessDeniedException();
        }

        return current;
    }

    /// <summary>
    /// Refuses, and records, an operation of a session that is not an admin, or whose account is no
    /// longer an admin in the database. A user session is refused before the database is asked.
    /// </summary>
    public async Task RequireAdminAsync(UserSession? session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsAdmin)
        {
            audit.Record(AuditEvent.AccessDenied, AuditCodes.Of(session.Role));
            throw new AccessDeniedException();
        }

        if (await RequireActiveAsync(session, cancellationToken).ConfigureAwait(false) != UserRole.Admin)
        {
            audit.Record(AuditEvent.AccessDenied, AuditRole.User);
            throw new AccessDeniedException();
        }
    }

    /// <summary>
    /// Runs a repository write that checks the session's account again inside the write. When the
    /// account was deleted or lost the needed role after the check at the start of the operation, the
    /// repository wrote nothing; the refusal is recorded and reported like any other denied access.
    /// </summary>
    public async Task<T> WriteAsActorAsync<T>(UserSession session, Func<Task<T>> write)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(write);

        try
        {
            return await write().ConfigureAwait(false);
        }
        catch (ActorRevokedException ex)
        {
            audit.Record(AuditEvent.AccessDenied, AuditCodes.Of(session.Role));
            throw new AccessDeniedException(AccessDeniedException.DefaultMessage, ex);
        }
    }

    /// <summary>
    /// The account whose rentals the session may see and end: its own id, or <see langword="null"/>
    /// (everyone) when the account is an admin in the database now.
    /// </summary>
    public async Task<int?> OwnerFilterAsync(UserSession session, CancellationToken cancellationToken)
    {
        var role = await RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);
        return role == UserRole.Admin ? null : session.UserId;
    }
}

internal static class Access
{
    /// <summary>Today's date on the local wall clock; rental days are local calendar days.</summary>
    public static DateOnly Today(this TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
}
