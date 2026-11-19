using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace AracKiralama.Data.Repositories;

/// <summary>
/// The rows of the revenue report, read with one LINQ query. The sums are made by the business layer:
/// SQLite cannot add decimals in SQL, and a report covers a bounded range of days, so no raw SQL is
/// needed on either provider.
/// </summary>
internal sealed class ReportRepository(IDbContextFactory<AracDbContext> contextFactory) : IReportRepository
{
    public async Task<IReadOnlyList<RevenueRow>> GetRevenueRowsAsync(DateOnly from, DateOnly until, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query =
            from rental in db.Rentals.AsNoTracking()
            join car in db.Cars.AsNoTracking() on rental.CarId equals car.Id
            where rental.Status != RentalStatus.Cancelled && rental.StartDate >= @from && rental.StartDate < until
            orderby rental.StartDate, rental.Id
            select new RevenueRow(rental.Id, car.Id, car.Brand, car.Model, car.Plate, rental.StartDate, rental.EndDate, rental.Price);

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
