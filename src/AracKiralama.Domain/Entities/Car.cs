namespace AracKiralama.Domain.Entities;

/// <summary>A car that can be rented (table Araclar).</summary>
public sealed class Car
{
    public int Id { get; set; }

    public string Brand { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Model year.</summary>
    public int Year { get; set; }

    /// <summary>Gearbox as the screens name it ("Otomatik", "Manuel").</summary>
    public string Transmission { get; set; } = string.Empty;

    /// <summary>Fuel as the screens name it ("Benzin", "Dizel", "Elektrik").</summary>
    public string FuelType { get; set; } = string.Empty;

    /// <summary>Kilometres on the clock.</summary>
    public int Mileage { get; set; }

    public string Color { get; set; } = string.Empty;

    /// <summary>Licence plate; unique.</summary>
    public string Plate { get; set; } = string.Empty;

    /// <summary>Price of one rental day.</summary>
    public decimal DailyPrice { get; set; }

    /// <summary>Address of a picture of the car (http or https), or <see langword="null"/>.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Whether customers can see and rent the car. The original application hid a car by setting its
    /// status to "Kirada" by hand; such cars keep that state as unlisted. Whether a car is free on given
    /// days is decided by its rentals, not by this flag.
    /// </summary>
    public bool IsListed { get; set; } = true;

    /// <summary>
    /// Raised by every change of the car's details (column Surum, 1 for a new car). A change names the
    /// version its form loaded and is refused when the stored one differs, so a form opened before another
    /// admin's change (a new price, the car taken off the list) cannot write the old values back.
    /// </summary>
    public int Version { get; set; }
}

/// <summary>Distinct values the car lists can be filtered by.</summary>
public sealed record CarFilterOptions(
    IReadOnlyList<string> Brands,
    IReadOnlyList<string> Transmissions,
    IReadOnlyList<string> FuelTypes);

/// <summary>Car or rental filter; <see langword="null"/> means any value.</summary>
public sealed record CarFilter(string? Brand = null, string? Transmission = null, string? FuelType = null)
{
    public static CarFilter None { get; } = new();
}
