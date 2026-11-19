namespace AracKiralama.Domain.Entities;

/// <summary>
/// A maintenance or damage repair of one car for the half-open range of calendar days
/// [<see cref="StartDate"/>, <see cref="EndDate"/>) (table AracBakim). While it is
/// <see cref="MaintenanceStatus.Active"/> the car cannot be rented on those days, and no such record can
/// share a day with an active rental of the car. Records are never deleted: a record entered by mistake
/// is cancelled and stays in the car's history.
/// </summary>
public sealed class MaintenanceRecord
{
    public int Id { get; set; }

    public int CarId { get; set; }

    public MaintenanceKind Kind { get; set; }

    /// <summary>What was done or found, as plain text.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>First day the car is in the workshop.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>First day the car is available again; always after <see cref="StartDate"/>.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Cost of the work; 0 when it is not known.</summary>
    public decimal Cost { get; set; }

    public MaintenanceStatus Status { get; set; }

    /// <summary>UTC time the record was entered, by the database's clock.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC time of the last change, by the database's clock.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Raised by every change. A change names the version its form loaded and is refused when the stored
    /// one differs, so a form opened before another admin's change cannot write over it.
    /// </summary>
    public int Version { get; set; } = 1;
}

/// <summary>Kind of a <see cref="MaintenanceRecord"/> (column Tur).</summary>
public enum MaintenanceKind
{
    /// <summary>Planned maintenance (service, tyres, inspection).</summary>
    Maintenance = 0,

    /// <summary>Repair of a damage.</summary>
    Damage = 1,
}

/// <summary>State of a <see cref="MaintenanceRecord"/> (column Durum).</summary>
public enum MaintenanceStatus
{
    /// <summary>The record holds its days: the car cannot be rented on them.</summary>
    Active = 0,

    /// <summary>Cancelled; the record is history and holds no day.</summary>
    Cancelled = 1,
}
