using AracKiralama.Domain.Entities;

namespace AracKiralama.Domain.Repositories;

/// <summary>
/// The maintenance and damage log. Every write runs in one transaction under the car's lock, the same
/// lock a booking, a cancellation and a return of the car take, and checks the acting admin inside it.
/// </summary>
public interface IMaintenanceRepository
{
    /// <summary>Every record of the car, newest start first.</summary>
    Task<IReadOnlyList<MaintenanceRecord>> GetForCarAsync(int carId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores an active record for <paramref name="carId"/>, stamped with <paramref name="nowUtc"/> (the
    /// database's clock), unless an active rental of the car shares a day with its range.
    /// </summary>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<MaintenanceWriteResult> AddAsync(Actor admin, int carId, MaintenanceValues values, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes an active record to <paramref name="changed"/>, only while its stored version is still
    /// <paramref name="expectedVersion"/> and no active rental of the car shares a day with the new range.
    /// </summary>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<MaintenanceWriteResult> UpdateAsync(
        Actor admin,
        int recordId,
        int expectedVersion,
        MaintenanceValues changed,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels an active record, only while its stored version is still <paramref name="expectedVersion"/>.</summary>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<MaintenanceWriteResult> CancelAsync(Actor admin, int recordId, int expectedVersion, DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>The fields of a maintenance record an admin enters.</summary>
public readonly record struct MaintenanceValues(MaintenanceKind Kind, string Description, DateOnly StartDate, DateOnly EndDate, decimal Cost);

public enum MaintenanceOutcome
{
    Saved = 0,

    /// <summary>The car does not exist; nothing was written.</summary>
    CarNotFound = 1,

    /// <summary>No active record with that id; nothing was written.</summary>
    NotFound = 2,

    /// <summary>The record no longer has the version the caller loaded (somebody else changed it); nothing was written.</summary>
    ChangedElsewhere = 3,

    /// <summary>An active rental of the car shares at least one day with the range; nothing was written.</summary>
    OverlapsRental = 4,
}

/// <summary>Outcome of a maintenance write, with the record's id and its version after the write.</summary>
public readonly record struct MaintenanceWriteResult(MaintenanceOutcome Outcome, int RecordId = 0, int Version = 0);
