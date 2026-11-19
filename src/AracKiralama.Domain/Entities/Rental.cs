namespace AracKiralama.Domain.Entities;

/// <summary>
/// A rental of one car by one account for the half-open range of calendar days
/// [<see cref="StartDate"/>, <see cref="EndDate"/>) (table Kiralama). The car is picked up on the start
/// day and due back on the end day, so a rental ending on a day and another starting on the same day do
/// not overlap. Only <see cref="RentalStatus.Active"/> rentals hold their days.
/// </summary>
public sealed class Rental
{
    public int Id { get; set; }

    public int CarId { get; set; }

    public int UserId { get; set; }

    /// <summary>Local calendar day the car is picked up (first rented day).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Local calendar day the car is due back; the first day that is no longer rented. Always after <see cref="StartDate"/>.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Local calendar day the car came back, set exactly when the rental is <see cref="RentalStatus.Returned"/>.</summary>
    public DateOnly? ReturnedOn { get; set; }

    public RentalStatus Status { get; set; }

    /// <summary>Price of the rental period, agreed when it was booked.</summary>
    public decimal Price { get; set; }

    /// <summary>Rented days: <see cref="EndDate"/> minus <see cref="StartDate"/>.</summary>
    public int Days => EndDate.DayNumber - StartDate.DayNumber;
}

/// <summary>State of a rental (column Durum).</summary>
public enum RentalStatus
{
    /// <summary>Booked or under way; the rental holds its days.</summary>
    Active = 0,

    /// <summary>The car came back (on <see cref="Rental.ReturnedOn"/>); the row is history.</summary>
    Returned = 1,

    /// <summary>Cancelled before its start day; the row is history.</summary>
    Cancelled = 2,
}

/// <summary>A rental with its car and account, for the rental lists.</summary>
public sealed record RentalDetails(
    int Id,
    int CarId,
    string Brand,
    string Model,
    string Transmission,
    string FuelType,
    string Plate,
    string? ImageUrl,
    int UserId,
    string UserName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Price,
    RentalStatus Status);

/// <summary>Days of a car held by an active rental: [<see cref="StartDate"/>, <see cref="EndDate"/>).</summary>
public readonly record struct BookedRange(DateOnly StartDate, DateOnly EndDate)
{
    /// <summary>
    /// Whether two half-open ranges share at least one day. Ranges that only touch (one ends on the day
    /// the other starts) do not.
    /// </summary>
    public bool Overlaps(DateOnly startDate, DateOnly endDate) => StartDate < endDate && startDate < EndDate;
}

/// <summary>Why a car's days are taken.</summary>
public enum BlockKind
{
    /// <summary>An active rental holds the days.</summary>
    Rental = 0,

    /// <summary>An active maintenance or damage record holds the days.</summary>
    Maintenance = 1,
}

/// <summary>Days [<see cref="StartDate"/>, <see cref="EndDate"/>) of a car that cannot be booked, and why.</summary>
public readonly record struct BlockedRange(DateOnly StartDate, DateOnly EndDate, BlockKind Kind);

/// <summary>What a rental contract shows: the rental, its car and the renter's name and contact data.</summary>
public sealed record ContractData(
    int RentalId,
    RentalStatus Status,
    string Brand,
    string Model,
    int Year,
    string Color,
    string Plate,
    string RenterName,
    string? RenterEmail,
    string? RenterPhone,
    DateOnly StartDate,
    DateOnly EndDate,
    DateOnly? ReturnedOn,
    decimal Price);
