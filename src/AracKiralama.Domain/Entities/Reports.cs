namespace AracKiralama.Domain.Entities;

/// <summary>A rental that earns revenue (active or returned, not cancelled), with its car, for the revenue report.</summary>
public sealed record RevenueRow(
    int RentalId,
    int CarId,
    string Brand,
    string Model,
    string Plate,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Price);
