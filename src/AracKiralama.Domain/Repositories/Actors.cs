namespace AracKiralama.Domain.Repositories;

/// <summary>
/// The account a write acts for, with the session version its session was issued with. The repository
/// checks both against the stored account inside the write's transaction: an account that was deleted,
/// or whose role or password was changed since the session began (also when the change was undone
/// again), writes nothing.
/// </summary>
public readonly record struct Actor(int UserId, int SessionVersion);

/// <summary>
/// The account a rental operation acts for: with <see cref="AsAdmin"/> every account's rentals, otherwise
/// only its own. The repository checks it, with <see cref="SessionVersion"/>, against the stored account
/// inside the operation.
/// </summary>
public readonly record struct RentalActor(int UserId, bool AsAdmin, int SessionVersion = 0)
{
    public Actor Actor => new(UserId, SessionVersion);
}

/// <summary>
/// Raised by a repository when the account an operation acts for no longer exists, no longer has the
/// role the operation needs, or had its session version raised, at the moment of the write. Nothing was
/// written.
/// </summary>
public sealed class ActorRevokedException : Exception
{
    public ActorRevokedException()
        : base("The acting account no longer has the rights for this operation.")
    {
    }

    public ActorRevokedException(string message)
        : base(message)
    {
    }

    public ActorRevokedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
