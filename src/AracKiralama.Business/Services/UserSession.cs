using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

namespace AracKiralama.Business.Services;

/// <summary>
/// The signed-in account. Only <see cref="AuthService"/> and <see cref="SetupService"/> create sessions
/// (the constructor is internal), and every service takes one and checks the role and the ownership of
/// the data against it, so the user interface cannot widen access by passing other ids.
/// </summary>
public sealed class UserSession
{
    internal UserSession(int userId, string userName, UserRole role, int sessionVersion = 0)
    {
        UserId = userId;
        UserName = userName;
        Role = role;
        SessionVersion = sessionVersion;
    }

    public int UserId { get; }

    public string UserName { get; }

    public UserRole Role { get; }

    /// <summary>
    /// The account's session version at sign-in. Once the stored version is raised (a role change or a
    /// password set by an admin) the session is refused for good, also when the change is undone.
    /// </summary>
    public int SessionVersion { get; }

    public bool IsAdmin => Role == UserRole.Admin;

    /// <summary>The account as the repositories check it inside a write.</summary>
    internal Actor Actor => new(UserId, SessionVersion);
}
