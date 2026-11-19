using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Repositories;

internal sealed class MaintenanceRepository(IDbContextFactory<AracDbContext> contextFactory, TimeProvider timeProvider) : IMaintenanceRepository
{
    public async Task<IReadOnlyList<MaintenanceRecord>> GetForCarAsync(int carId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Maintenance
            .AsNoTracking()
            .Where(m => m.CarId == carId)
            .OrderByDescending(m => m.StartDate)
            .ThenByDescending(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MaintenanceWriteResult> AddAsync(Actor admin, int carId, MaintenanceValues values, CancellationToken cancellationToken = default)
    {
        RequireRange(values);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Taken before anything is read: no booking of this car can take the days between the overlap
        // check below and the commit, and no record can be added while a booking checks them.
        await Locks.CarAsync(db, carId, cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, admin, admin: true, cancellationToken).ConfigureAwait(false);

        if (!await db.Cars.AnyAsync(c => c.Id == carId, cancellationToken).ConfigureAwait(false))
        {
            return new MaintenanceWriteResult(MaintenanceOutcome.CarNotFound);
        }

        if (await RentalRepository.OverlapsAsync(db, carId, values.StartDate, values.EndDate, cancellationToken).ConfigureAwait(false))
        {
            return new MaintenanceWriteResult(MaintenanceOutcome.OverlapsRental);
        }

        if (await OverlapsAsync(db, carId, values.StartDate, values.EndDate, cancellationToken).ConfigureAwait(false))
        {
            return new MaintenanceWriteResult(MaintenanceOutcome.OverlapsMaintenance);
        }

        // Stamped after the lock was granted, inside the transaction, so the time is the write's own.
        var nowUtc = await DatabaseClock.ReadAsync(db, timeProvider, cancellationToken).ConfigureAwait(false);
        var record = new MaintenanceRecord
        {
            CarId = carId,
            Kind = values.Kind,
            Description = values.Description,
            StartDate = values.StartDate,
            EndDate = values.EndDate,
            Cost = values.Cost,
            Status = MaintenanceStatus.Active,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            Version = 1,
        };
        db.Maintenance.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MaintenanceWriteResult(MaintenanceOutcome.Saved, record.Id, record.Version);
    }

    public async Task<MaintenanceWriteResult> UpdateAsync(
        Actor admin,
        int recordId,
        int expectedVersion,
        MaintenanceValues changed,
        CancellationToken cancellationToken = default)
    {
        RequireRange(changed);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (await LockCarOfAsync(db, admin, recordId, cancellationToken).ConfigureAwait(false) is not { } carId)
        {
            return new MaintenanceWriteResult(MaintenanceOutcome.NotFound);
        }

        if (await RentalRepository.OverlapsAsync(db, carId, changed.StartDate, changed.EndDate, cancellationToken).ConfigureAwait(false))
        {
            return await RefusalAsync(db, recordId, expectedVersion, MaintenanceOutcome.OverlapsRental, cancellationToken).ConfigureAwait(false);
        }

        // Another active record of the car on these days; the record itself may keep its own days.
        if (await OverlapsAsync(db, carId, changed.StartDate, changed.EndDate, cancellationToken, exceptRecordId: recordId).ConfigureAwait(false))
        {
            return await RefusalAsync(db, recordId, expectedVersion, MaintenanceOutcome.OverlapsMaintenance, cancellationToken).ConfigureAwait(false);
        }

        // The WHERE clause is the final decision: a record cancelled or changed since the form loaded it
        // matches no row, so a stale form never writes over another admin's change.
        var (kind, description, start, end, cost) = changed;
        var nowUtc = await DatabaseClock.ReadAsync(db, timeProvider, cancellationToken).ConfigureAwait(false);
        var updated = await db.Maintenance
            .Where(m => m.Id == recordId && m.Status == MaintenanceStatus.Active && m.Version == expectedVersion)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Kind, kind)
                    .SetProperty(m => m.Description, description)
                    .SetProperty(m => m.StartDate, start)
                    .SetProperty(m => m.EndDate, end)
                    .SetProperty(m => m.Cost, cost)
                    .SetProperty(m => m.UpdatedAt, nowUtc)
                    .SetProperty(m => m.Version, m => m.Version + 1),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated != 1)
        {
            return await RefusalAsync(db, recordId, expectedVersion, MaintenanceOutcome.ChangedElsewhere, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MaintenanceWriteResult(MaintenanceOutcome.Saved, recordId, expectedVersion + 1);
    }

    public async Task<MaintenanceWriteResult> CancelAsync(Actor admin, int recordId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (await LockCarOfAsync(db, admin, recordId, cancellationToken).ConfigureAwait(false) is null)
        {
            return new MaintenanceWriteResult(MaintenanceOutcome.NotFound);
        }

        var nowUtc = await DatabaseClock.ReadAsync(db, timeProvider, cancellationToken).ConfigureAwait(false);
        var cancelled = await db.Maintenance
            .Where(m => m.Id == recordId && m.Status == MaintenanceStatus.Active && m.Version == expectedVersion)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.Status, MaintenanceStatus.Cancelled)
                    .SetProperty(m => m.UpdatedAt, nowUtc)
                    .SetProperty(m => m.Version, m => m.Version + 1),
                cancellationToken)
            .ConfigureAwait(false);
        if (cancelled != 1)
        {
            return await RefusalAsync(db, recordId, expectedVersion, MaintenanceOutcome.ChangedElsewhere, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new MaintenanceWriteResult(MaintenanceOutcome.Saved, recordId, expectedVersion + 1);
    }

    /// <summary>
    /// Whether an active maintenance record of the car shares a day with [<paramref name="start"/>,
    /// <paramref name="end"/>), other than <paramref name="exceptRecordId"/>. Touching ranges do not overlap.
    /// </summary>
    internal static Task<bool> OverlapsAsync(AracDbContext db, int carId, DateOnly start, DateOnly end, CancellationToken cancellationToken, int? exceptRecordId = null) =>
        db.Maintenance.AnyAsync(
            m => m.CarId == carId && m.Status == MaintenanceStatus.Active && m.StartDate < end && start < m.EndDate && (exceptRecordId == null || m.Id != exceptRecordId),
            cancellationToken);

    /// <summary>
    /// Finds the car of an active record, takes the car's lock and checks the admin; <see langword="null"/>
    /// when there is no active record with that id. A record never moves to another car, so the car read
    /// before the lock is the one to lock.
    /// </summary>
    private static async Task<int?> LockCarOfAsync(AracDbContext db, Actor admin, int recordId, CancellationToken cancellationToken)
    {
        var carId = await db.Maintenance
            .AsNoTracking()
            .Where(m => m.Id == recordId)
            .Select(m => (int?)m.CarId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (carId is null)
        {
            // Checked anyway, so an account without the rights learns nothing about the records.
            await Actors.RequireAsync(db, admin, admin: true, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await Locks.CarAsync(db, carId.Value, cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, admin, admin: true, cancellationToken).ConfigureAwait(false);
        return await db.Maintenance.AnyAsync(m => m.Id == recordId && m.Status == MaintenanceStatus.Active, cancellationToken).ConfigureAwait(false)
            ? carId
            : null;
    }

    /// <summary>
    /// Why a write of an active record was refused: a version other than the expected one wins over
    /// <paramref name="otherwise"/>, so a stale form is always told to reload.
    /// </summary>
    private static async Task<MaintenanceWriteResult> RefusalAsync(
        AracDbContext db,
        int recordId,
        int expectedVersion,
        MaintenanceOutcome otherwise,
        CancellationToken cancellationToken)
    {
        var stored = await db.Maintenance
            .AsNoTracking()
            .Where(m => m.Id == recordId)
            .Select(m => new { m.Status, m.Version })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (stored is null || stored.Status != MaintenanceStatus.Active)
        {
            return new MaintenanceWriteResult(MaintenanceOutcome.NotFound);
        }

        return new MaintenanceWriteResult(stored.Version != expectedVersion ? MaintenanceOutcome.ChangedElsewhere : otherwise);
    }

    private static void RequireRange(MaintenanceValues values)
    {
        if (values.EndDate <= values.StartDate)
        {
            throw new ArgumentException("A maintenance record must end after it starts.", nameof(values));
        }
    }
}
