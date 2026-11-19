using System.Data.Common;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Repositories;

internal sealed class CarRepository(IDbContextFactory<AracDbContext> contextFactory) : ICarRepository
{
    public async Task<IReadOnlyList<Car>> GetCatalogAsync(CarFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await Matching(db.Cars.AsNoTracking(), filter)
            .Where(c => c.IsListed)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CarFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var cars = db.Cars.AsNoTracking();
        var brands = await cars.Select(c => c.Brand).Distinct().OrderBy(b => b).ToListAsync(cancellationToken).ConfigureAwait(false);
        var transmissions = await cars.Select(c => c.Transmission).Distinct().OrderBy(t => t).ToListAsync(cancellationToken).ConfigureAwait(false);
        var fuelTypes = await cars.Select(c => c.FuelType).Distinct().OrderBy(f => f).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new CarFilterOptions(brands, transmissions, fuelTypes);
    }

    public async Task<IReadOnlyList<Car>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Cars.AsNoTracking().OrderBy(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Car?> FindAsync(int carId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Cars.AsNoTracking().SingleOrDefaultAsync(c => c.Id == carId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(Actor admin, Car car, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(car);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await Actors.RequireAsync(db, admin, admin: true, cancellationToken).ConfigureAwait(false);

            var entity = Copy(car);
            db.Cars.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return entity.Id;
        }
        catch (DbUpdateException ex)
        {
            // The unique index rejected a plate registered concurrently; checked with a new query so the
            // provider specific error code does not matter.
            await ThrowIfPlateTakenAsync(car.Plate, null, ex).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CarUpdateOutcome> UpdateAsync(Actor admin, Car car, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(car);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // A booking of this car checks the daily price under the same lock, so it sees either the old
            // price (and commits first) or the new one (and is refused as changed).
            await Locks.CarAsync(db, car.Id, cancellationToken).ConfigureAwait(false);
            await Actors.RequireAsync(db, admin, admin: true, cancellationToken).ConfigureAwait(false);

            // The WHERE clause is the final decision: a car changed since the form loaded it (another
            // admin's new price or listing) matches no row, so a stale form never writes the old values back.
            var (id, expectedVersion) = (car.Id, car.Version);
            var updated = await db.Cars
                .Where(c => c.Id == id && c.Version == expectedVersion)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(c => c.Brand, car.Brand)
                        .SetProperty(c => c.Model, car.Model)
                        .SetProperty(c => c.Year, car.Year)
                        .SetProperty(c => c.Transmission, car.Transmission)
                        .SetProperty(c => c.FuelType, car.FuelType)
                        .SetProperty(c => c.Mileage, car.Mileage)
                        .SetProperty(c => c.Color, car.Color)
                        .SetProperty(c => c.Plate, car.Plate)
                        .SetProperty(c => c.DailyPrice, car.DailyPrice)
                        .SetProperty(c => c.ImageUrl, car.ImageUrl)
                        .SetProperty(c => c.IsListed, car.IsListed)
                        .SetProperty(c => c.Version, c => c.Version + 1),
                    cancellationToken)
                .ConfigureAwait(false);
            if (updated != 1)
            {
                return await db.Cars.AnyAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false)
                    ? CarUpdateOutcome.ChangedElsewhere
                    : CarUpdateOutcome.NotFound;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return CarUpdateOutcome.Updated;
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            await ThrowIfPlateTakenAsync(car.Plate, car.Id, ex).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CarDeleteOutcome> DeleteAsync(Actor admin, int carId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await Locks.CarAsync(db, carId, cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, admin, admin: true, cancellationToken).ConfigureAwait(false);

        if (!await db.Cars.AnyAsync(c => c.Id == carId, cancellationToken).ConfigureAwait(false))
        {
            return CarDeleteOutcome.NotFound;
        }

        if (await db.Rentals.AnyAsync(r => r.CarId == carId, cancellationToken).ConfigureAwait(false)
            || await db.Maintenance.AnyAsync(m => m.CarId == carId, cancellationToken).ConfigureAwait(false))
        {
            return CarDeleteOutcome.HasRentals;
        }

        await db.Cars.Where(c => c.Id == carId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return CarDeleteOutcome.Deleted;
    }

    /// <summary>Cars matching every set part of <paramref name="filter"/>.</summary>
    internal static IQueryable<Car> Matching(IQueryable<Car> cars, CarFilter filter)
    {
        if (filter.Brand is { } brand)
        {
            cars = cars.Where(c => c.Brand == brand);
        }

        if (filter.Transmission is { } transmission)
        {
            cars = cars.Where(c => c.Transmission == transmission);
        }

        if (filter.FuelType is { } fuelType)
        {
            cars = cars.Where(c => c.FuelType == fuelType);
        }

        return cars;
    }

    private async Task ThrowIfPlateTakenAsync(string plate, int? exceptCarId, Exception cause)
    {
        await using var db = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        if (await db.Cars.AnyAsync(c => c.Plate == plate && (exceptCarId == null || c.Id != exceptCarId), CancellationToken.None).ConfigureAwait(false))
        {
            throw new DuplicatePlateException("The plate is already registered.", cause);
        }
    }

    private static Car Copy(Car car) => new()
    {
        Brand = car.Brand,
        Model = car.Model,
        Year = car.Year,
        Transmission = car.Transmission,
        FuelType = car.FuelType,
        Mileage = car.Mileage,
        Color = car.Color,
        Plate = car.Plate,
        DailyPrice = car.DailyPrice,
        ImageUrl = car.ImageUrl,
        IsListed = car.IsListed,
    };
}
