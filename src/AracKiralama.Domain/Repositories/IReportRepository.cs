using AracKiralama.Domain.Entities;

namespace AracKiralama.Domain.Repositories;

/// <summary>Read-only data of the admin's reports.</summary>
public interface IReportRepository
{
    /// <summary>
    /// The rentals that earn revenue (active or returned; cancelled ones earn nothing) whose pick-up day
    /// is in [<paramref name="from"/>, <paramref name="until"/>), with their cars, ordered by pick-up day and id.
    /// </summary>
    Task<IReadOnlyList<RevenueRow>> GetRevenueRowsAsync(DateOnly from, DateOnly until, CancellationToken cancellationToken = default);
}
