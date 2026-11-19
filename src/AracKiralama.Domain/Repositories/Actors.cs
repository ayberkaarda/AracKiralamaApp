namespace AracKiralama.Domain.Repositories;

/// <summary>
/// The account a rental operation acts for: with <see cref="AsAdmin"/> every account's rentals, otherwise
/// only its own. The repository checks it against the stored account inside the operation.
/// </summary>
public readonly record struct RentalActor(int UserId, bool AsAdmin);

/// <summary>
/// Raised by a repository when the account an operation acts for no longer exists, or no longer has
/// the role the operation needs, at the moment of the write. Nothing was written.
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
