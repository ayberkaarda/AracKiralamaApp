using AracKiralama.Domain.Entities;

namespace AracKiralama.Domain.Repositories;

/// <summary>Rentals and their end. Every write runs in one transaction under the car's lock.</summary>
public interface IRentalRepository
{
    /// <summary>
    /// Books a car: under the car's lock, inside the transaction, it checks that the renter's session is
    /// current, that the car exists, is listed, still costs <see cref="RentalRequest.ExpectedDailyPrice"/>
    /// a day and that no active rental and no active maintenance record of the car shares a day with
    /// [<see cref="RentalRequest.StartDate"/>, <see cref="RentalRequest.EndDate"/>), then stores the rental.
    /// </summary>
    Task<RentResult> RentAsync(RentalRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends an active rental on <paramref name="today"/>: one that has not started yet is cancelled,
    /// one that has is marked returned on that day. Unless <paramref name="actor"/> acts as an admin, a
    /// rental of another account is treated as missing. The actor's account must exist and, acting as
    /// an admin, still be an admin at the write.
    /// </summary>
    /// <exception cref="ActorRevokedException">The actor's account is gone or no admin (any more); nothing was written.</exception>
    Task<EndRentalResult> EndAsync(int rentalId, RentalActor actor, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>
    /// Active rentals with car and account, ordered by start day and id; only those of
    /// <paramref name="ownerId"/> when it is set, and only cars matching <paramref name="filter"/>.
    /// </summary>
    Task<IReadOnlyList<RentalDetails>> GetActiveAsync(int? ownerId, CarFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// The days of <paramref name="carId"/> that cannot be booked, from <paramref name="from"/> on: held by
    /// active rentals or active maintenance records that end after it, in order of their start.
    /// </summary>
    Task<IReadOnlyList<BookedRange>> GetBookedRangesAsync(int carId, DateOnly from, CancellationToken cancellationToken = default);

    /// <summary>
    /// The days of <paramref name="carId"/> held by active rentals or active maintenance records that
    /// share a day with [<paramref name="from"/>, <paramref name="until"/>), with their kind, in order of their start.
    /// </summary>
    Task<IReadOnlyList<BlockedRange>> GetBlockedRangesAsync(int carId, DateOnly from, DateOnly until, CancellationToken cancellationToken = default);

    /// <summary>
    /// The contract data of a rental in any state, or <see langword="null"/> when it does not exist or,
    /// with <paramref name="ownerId"/> set, belongs to another account.
    /// </summary>
    Task<ContractData?> GetContractDataAsync(int rentalId, int? ownerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A rental to store for account <see cref="UserId"/>, whose session carries <see cref="SessionVersion"/>;
/// the price was computed from <see cref="ExpectedDailyPrice"/>.
/// </summary>
public sealed record RentalRequest(
    int CarId,
    int UserId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal ExpectedDailyPrice,
    decimal Price,
    int SessionVersion = 0);

public enum RentOutcome
{
    Rented = 0,
    CarNotFound = 1,

    /// <summary>The car is unlisted.</summary>
    NotAvailable = 2,

    /// <summary>An active rental of the car shares at least one day with the requested range; nothing was written.</summary>
    Overlaps = 3,

    /// <summary>The daily price changed after it was shown; nothing was written.</summary>
    PriceChanged = 4,

    /// <summary>An active maintenance record of the car shares at least one day with the requested range; nothing was written.</summary>
    InMaintenance = 5,
}

public readonly record struct RentResult(RentOutcome Outcome, int RentalId);

public enum EndRentalOutcome
{
    /// <summary>The rental had not started and is cancelled.</summary>
    Cancelled = 0,

    /// <summary>The rental had started and the car is returned.</summary>
    Returned = 1,

    /// <summary>No active rental with that id (or not one of the owner's); nothing was written.</summary>
    NotFound = 2,
}

public readonly record struct EndRentalResult(EndRentalOutcome Outcome);
