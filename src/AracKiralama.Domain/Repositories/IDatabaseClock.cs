namespace AracKiralama.Domain.Repositories;

/// <summary>
/// The clock that decisions on shared data use: sign-in locks, the first day a rental may start and
/// whether ending a rental cancels or returns it. It is the database server's clock, so every
/// computer decides the same, also one whose own clock is wrong.
/// </summary>
public interface IDatabaseClock
{
    /// <summary>The current UTC time (<see cref="DateTimeKind.Utc"/>) of the database's clock.</summary>
    Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default);
}
