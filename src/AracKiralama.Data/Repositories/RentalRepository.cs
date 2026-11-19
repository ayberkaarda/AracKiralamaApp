using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Repositories;

internal sealed class RentalRepository(IDbContextFactory<AracDbContext> contextFactory) : IRentalRepository
{
    public async Task<RentResult> RentAsync(RentalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EndDate <= request.StartDate)
        {
            throw new ArgumentException("A rental must end after it starts.", nameof(request));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Taken before anything is read: no other booking, cancellation, return or change of this car
        // can run between the checks below and the commit.
        await Locks.CarAsync(db, request.CarId, cancellationToken).ConfigureAwait(false);

        var car = await db.Cars
            .AsNoTracking()
            .Where(c => c.Id == request.CarId)
            .Select(c => new { c.IsListed, c.DailyPrice })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (car is null)
        {
            return new RentResult(RentOutcome.CarNotFound, 0);
        }

        if (!car.IsListed)
        {
            return new RentResult(RentOutcome.NotAvailable, 0);
        }

        if (car.DailyPrice != request.ExpectedDailyPrice)
        {
            return new RentResult(RentOutcome.PriceChanged, 0);
        }

        if (await OverlapsAsync(db, request.CarId, request.StartDate, request.EndDate, cancellationToken).ConfigureAwait(false))
        {
            return new RentResult(RentOutcome.Overlaps, 0);
        }

        var rental = new Rental
        {
            CarId = request.CarId,
            UserId = request.UserId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = RentalStatus.Active,
            Price = request.Price,
        };
        db.Rentals.Add(rental);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new RentResult(RentOutcome.Rented, rental.Id);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index rejected a second active rental of the car starting on the same
            // day (a writer that skipped the lock); nothing was stored.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await using var check = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            if (await OverlapsAsync(check, request.CarId, request.StartDate, request.EndDate, CancellationToken.None).ConfigureAwait(false))
            {
                return new RentResult(RentOutcome.Overlaps, 0);
            }

            throw;
        }
    }

    public async Task<EndRentalResult> EndAsync(int rentalId, RentalActor actor, DateOnly today, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Checked first, so an account that lost the admin role learns nothing about other accounts' rentals.
        await Actors.RequireAsync(db, actor.UserId, actor.AsAdmin, cancellationToken).ConfigureAwait(false);
        int? ownerId = actor.AsAdmin ? null : actor.UserId;

        // The car decides which lock to take; a rental of another account is the same as none.
        var carId = await Owned(db.Rentals.AsNoTracking(), ownerId)
            .Where(r => r.Id == rentalId && r.Status == RentalStatus.Active)
            .Select(r => (int?)r.CarId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (carId is null)
        {
            return new EndRentalResult(EndRentalOutcome.NotFound);
        }

        await Locks.CarAsync(db, carId.Value, cancellationToken).ConfigureAwait(false);

        // Read again under the lock: a concurrent end of the same rental has finished by now.
        var startDate = await Owned(db.Rentals.AsNoTracking(), ownerId)
            .Where(r => r.Id == rentalId && r.Status == RentalStatus.Active)
            .Select(r => (DateOnly?)r.StartDate)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (startDate is null)
        {
            return new EndRentalResult(EndRentalOutcome.NotFound);
        }

        // A rental that has not started frees all its days; one under way frees the days from today on.
        var cancel = today < startDate.Value;
        var active = db.Rentals.Where(r => r.Id == rentalId && r.Status == RentalStatus.Active);
        var changed = cancel
            ? await active.ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RentalStatus.Cancelled), cancellationToken).ConfigureAwait(false)
            : await active.ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, RentalStatus.Returned)
                    .SetProperty(r => r.ReturnedOn, today),
                cancellationToken).ConfigureAwait(false);
        if (changed != 1)
        {
            return new EndRentalResult(EndRentalOutcome.NotFound);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new EndRentalResult(cancel ? EndRentalOutcome.Cancelled : EndRentalOutcome.Returned);
    }

    public async Task<IReadOnlyList<RentalDetails>> GetActiveAsync(int? ownerId, CarFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query =
            from rental in Owned(db.Rentals.AsNoTracking(), ownerId)
            join car in CarRepository.Matching(db.Cars.AsNoTracking(), filter) on rental.CarId equals car.Id
            join user in db.Users.AsNoTracking() on rental.UserId equals user.Id
            where rental.Status == RentalStatus.Active
            orderby rental.StartDate, rental.Id
            select new RentalDetails(
                rental.Id,
                car.Id,
                car.Brand,
                car.Model,
                car.Transmission,
                car.FuelType,
                car.Plate,
                car.ImageUrl,
                user.Id,
                user.UserName,
                rental.StartDate,
                rental.EndDate,
                rental.Price,
                rental.Status);

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BookedRange>> GetBookedRangesAsync(int carId, DateOnly from, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var ranges = await db.Rentals
            .AsNoTracking()
            .Where(r => r.CarId == carId && r.Status == RentalStatus.Active && r.EndDate > from)
            .OrderBy(r => r.StartDate)
            .Select(r => new { r.StartDate, r.EndDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return ranges.Select(r => new BookedRange(r.StartDate, r.EndDate)).ToList();
    }

    /// <summary>
    /// Whether an active rental of the car shares a day with [<paramref name="start"/>,
    /// <paramref name="end"/>): it starts before the new one ends and ends after the new one starts.
    /// Touching ranges (one ends on the day the other starts) do not overlap.
    /// </summary>
    internal static Task<bool> OverlapsAsync(AracDbContext db, int carId, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        db.Rentals.AnyAsync(
            r => r.CarId == carId && r.Status == RentalStatus.Active && r.StartDate < end && start < r.EndDate,
            cancellationToken);

    private static IQueryable<Rental> Owned(IQueryable<Rental> rentals, int? ownerId) =>
        ownerId is { } owner ? rentals.Where(r => r.UserId == owner) : rentals;
}
