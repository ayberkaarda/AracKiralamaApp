using AracKiralama.Domain.Entities;

namespace AracKiralama.Domain.Repositories;

/// <summary>Cars (the original application's Araclar table).</summary>
public interface ICarRepository
{
    /// <summary>Listed cars that match <paramref name="filter"/>, ordered by id.</summary>
    Task<IReadOnlyList<Car>> GetCatalogAsync(CarFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Distinct brands, gearboxes and fuel types of all cars.</summary>
    Task<CarFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every car, ordered by id.</summary>
    Task<IReadOnlyList<Car>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Car?> FindAsync(int carId, CancellationToken cancellationToken = default);

    // Every write names the admin making it; that account must still be an admin at the write.

    /// <summary>Stores a new car and returns its id.</summary>
    /// <exception cref="DuplicatePlateException">Another car has the plate.</exception>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<int> AddAsync(Actor admin, Car car, CancellationToken cancellationToken = default);

    /// <summary>Changes a car's details under the car's lock.</summary>
    /// <exception cref="DuplicatePlateException">Another car has the plate.</exception>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<CarUpdateOutcome> UpdateAsync(Actor admin, Car car, CancellationToken cancellationToken = default);

    /// <summary>Deletes a car that has never been rented, under the car's lock.</summary>
    /// <exception cref="ActorRevokedException">Account <paramref name="admin"/> is no admin (any more) or its session is outdated; nothing was written.</exception>
    Task<CarDeleteOutcome> DeleteAsync(Actor admin, int carId, CancellationToken cancellationToken = default);
}

public enum CarUpdateOutcome
{
    Updated = 0,
    NotFound = 1,
}

public enum CarDeleteOutcome
{
    Deleted = 0,
    NotFound = 1,

    /// <summary>The car has rentals, active or past; their history keeps the car.</summary>
    HasRentals = 2,
}

/// <summary>Raised when a licence plate is already registered for another car.</summary>
public sealed class DuplicatePlateException : Exception
{
    public DuplicatePlateException()
        : base("The plate is already registered.")
    {
    }

    public DuplicatePlateException(string message)
        : base(message)
    {
    }

    public DuplicatePlateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
