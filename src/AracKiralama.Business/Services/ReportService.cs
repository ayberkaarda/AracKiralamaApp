using System.Globalization;
using AracKiralama.Business.Auditing;
using AracKiralama.Business.Exports;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

namespace AracKiralama.Business.Services;

/// <summary>How the revenue report groups the rentals by time.</summary>
public enum RevenueGrouping
{
    /// <summary>One line per calendar month.</summary>
    Month = 0,

    /// <summary>One line per day.</summary>
    Day = 1,
}

/// <summary>One line of the revenue report: rentals that start in a period or of one car, their rented days and their total price.</summary>
public sealed record RevenueLine(string Label, string? Plate, int Rentals, int Days, decimal Revenue);

/// <summary>
/// Revenue of the rentals whose pick-up day is in [<see cref="From"/>, <see cref="To"/>] (both days
/// included): by period and by car. A rental counts with the price agreed at booking, in the period of
/// its pick-up day; cancelled rentals earn nothing. <see cref="CreatedAt"/> is the local time by the
/// database's clock.
/// </summary>
public sealed record RevenueReport(
    DateOnly From,
    DateOnly To,
    RevenueGrouping Grouping,
    IReadOnlyList<RevenueLine> Periods,
    IReadOnlyList<RevenueLine> Cars,
    RevenueLine Total,
    DateTime CreatedAt);

/// <summary>The admin's reports.</summary>
public interface IReportService
{
    /// <summary>The revenue between two days, both included (admin only).</summary>
    Task<RevenueReport> GetRevenueAsync(UserSession session, DateOnly firstDay, DateOnly lastDay, RevenueGrouping grouping, CancellationToken cancellationToken = default);

    /// <summary>The report as a CSV file for spreadsheets, safe against formula injection.</summary>
    ExportFile ToCsv(RevenueReport report);
}

internal sealed class ReportService(
    IReportRepository reports,
    AccessGuard access,
    TimeProvider timeProvider,
    IDatabaseClock clock,
    IAuditLog audit) : IReportService
{
    /// <summary>Longest range of a report, and of a report by day.</summary>
    internal const int MaxDays = 3660;
    internal const int MaxDaysByDay = 366;

    internal const string RangeInvalid = "Bitiş tarihi başlangıç tarihinden önce olamaz.";
    internal const string RangeTooLong = "Rapor en fazla 10 yıllık bir dönem için alınabilir.";
    internal const string DailyRangeTooLong = "Günlük rapor en fazla 366 gün için alınabilir; daha uzun dönemler için aylık raporu seçin.";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<RevenueReport> GetRevenueAsync(
        UserSession session,
        DateOnly firstDay,
        DateOnly lastDay,
        RevenueGrouping grouping,
        CancellationToken cancellationToken = default)
    {
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        if (!Enum.IsDefined(grouping))
        {
            throw new ArgumentOutOfRangeException(nameof(grouping));
        }

        var length = lastDay.DayNumber - firstDay.DayNumber + 1;
        if (length < 1)
        {
            throw new BusinessRuleException(RangeInvalid);
        }

        if (length > MaxDays)
        {
            throw new BusinessRuleException(RangeTooLong);
        }

        if (grouping == RevenueGrouping.Day && length > MaxDaysByDay)
        {
            throw new BusinessRuleException(DailyRangeTooLong);
        }

        var rows = await reports.GetRevenueRowsAsync(firstDay, lastDay.AddDays(1), cancellationToken).ConfigureAwait(false);
        var now = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), timeProvider.LocalTimeZone);
        audit.Record(AuditEvent.RevenueReportCreated, AuditRole.Admin);
        return Build(firstDay, lastDay, grouping, rows, local);
    }

    public ExportFile ToCsv(RevenueReport report) => RevenueReportExport.ToCsv(report);

    /// <summary>Sums <paramref name="rows"/> by period (every period of the range, also empty ones) and by car (cars with rentals, by revenue).</summary>
    internal static RevenueReport Build(DateOnly from, DateOnly to, RevenueGrouping grouping, IReadOnlyList<RevenueRow> rows, DateTime createdAt)
    {
        var periods = new List<RevenueLine>();
        for (var start = PeriodStart(from, grouping); start <= to; start = Next(start, grouping))
        {
            var next = Next(start, grouping);
            var inPeriod = rows.Where(r => r.StartDate >= start && r.StartDate < next).ToList();
            periods.Add(Line(PeriodLabel(start, grouping), null, inPeriod));
        }

        var cars = rows
            .GroupBy(r => r.CarId)
            .Select(g => Line(g.First().Brand + " " + g.First().Model, g.First().Plate, [.. g]))
            .OrderByDescending(l => l.Revenue)
            .ThenBy(l => l.Plate, StringComparer.Ordinal)
            .ToList();

        return new RevenueReport(from, to, grouping, periods, cars, Line("Toplam", null, rows), createdAt);
    }

    private static RevenueLine Line(string label, string? plate, IReadOnlyCollection<RevenueRow> rows) =>
        new(label, plate, rows.Count, rows.Sum(r => r.EndDate.DayNumber - r.StartDate.DayNumber), rows.Sum(r => r.Price));

    private static DateOnly PeriodStart(DateOnly day, RevenueGrouping grouping) =>
        grouping == RevenueGrouping.Month ? new DateOnly(day.Year, day.Month, 1) : day;

    private static DateOnly Next(DateOnly start, RevenueGrouping grouping) =>
        grouping == RevenueGrouping.Month ? start.AddMonths(1) : start.AddDays(1);

    private static string PeriodLabel(DateOnly start, RevenueGrouping grouping) =>
        grouping == RevenueGrouping.Month ? start.ToString("MMMM yyyy", Turkish) : start.ToString("dd.MM.yyyy", Turkish);
}
