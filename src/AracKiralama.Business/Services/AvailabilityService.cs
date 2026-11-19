using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;

namespace AracKiralama.Business.Services;

/// <summary>State of one day of a car's calendar.</summary>
public enum DayState
{
    /// <summary>Free: the car can be booked on this day.</summary>
    Free = 0,

    /// <summary>An active rental holds the day.</summary>
    Booked = 1,

    /// <summary>The car is in maintenance or a damage repair on this day.</summary>
    Maintenance = 2,

    /// <summary>A free day before today; it can no longer be booked.</summary>
    Past = 3,
}

/// <summary>One day of a car's calendar.</summary>
public readonly record struct CalendarDay(DateOnly Date, DayState State);

/// <summary>A month of one car's calendar: every day of <see cref="Month"/> with its state, decided against <see cref="Today"/>.</summary>
public sealed record CarCalendar(int CarId, DateOnly Month, DateOnly Today, IReadOnlyList<CalendarDay> Days);

/// <summary>Which days of a car are free, booked or in maintenance.</summary>
public interface IAvailabilityService
{
    /// <summary>
    /// The month of <paramref name="month"/> of the car's calendar, today by the database's clock; with no
    /// month, the month of that today, so a workstation whose clock is wrong still opens on the right month.
    /// Customers see only listed cars; an unlisted or missing car gets the same refusal.
    /// </summary>
    Task<CarCalendar> GetCalendarAsync(UserSession session, int carId, DateOnly? month, CancellationToken cancellationToken = default);
}

internal sealed class AvailabilityService(
    ICarRepository cars,
    IRentalRepository rentals,
    AccessGuard access,
    TimeProvider timeProvider,
    IDatabaseClock clock) : IAvailabilityService
{
    /// <summary>How many months before or after today's month the calendar shows.</summary>
    internal const int MaxMonthsAway = 120;

    internal const string CarNotFound = "Araç bulunamadı.";
    internal const string MonthOutOfRange = "Takvim bugünden en fazla 10 yıl önce veya sonrası için gösterilebilir.";

    public async Task<CarCalendar> GetCalendarAsync(UserSession session, int carId, DateOnly? month, CancellationToken cancellationToken = default)
    {
        var role = await access.RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);
        var car = await cars.FindAsync(carId, cancellationToken).ConfigureAwait(false);
        if (car is null || (!car.IsListed && role != UserRole.Admin))
        {
            throw new BusinessRuleException(CarNotFound);
        }

        var today = await clock.TodayAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        var shown = month ?? today;
        var first = new DateOnly(shown.Year, shown.Month, 1);
        var away = ((first.Year - today.Year) * 12) + first.Month - today.Month;
        if (Math.Abs(away) > MaxMonthsAway)
        {
            throw new BusinessRuleException(MonthOutOfRange);
        }

        var next = first.AddMonths(1);
        var blocked = await rentals.GetBlockedRangesAsync(carId, first, next, cancellationToken).ConfigureAwait(false);
        return new CarCalendar(carId, first, today, Build(first, next, today, blocked, showPast: role == UserRole.Admin));
    }

    /// <summary>
    /// The states of [<paramref name="first"/>, <paramref name="next"/>): maintenance wins over a rental
    /// (both cannot be active on one day, but old data may disagree). With <paramref name="showPast"/> (an
    /// admin) a rental or maintenance also wins over a past day; customers see every day before today as past,
    /// so they cannot tell since when someone has had a car, only which days are still taken.
    /// </summary>
    internal static IReadOnlyList<CalendarDay> Build(DateOnly first, DateOnly next, DateOnly today, IReadOnlyList<BlockedRange> blocked, bool showPast)
    {
        var days = new List<CalendarDay>(31);
        for (var day = first; day < next; day = day.AddDays(1))
        {
            var state = day < today ? DayState.Past : DayState.Free;
            if (state == DayState.Past && !showPast)
            {
                days.Add(new CalendarDay(day, state));
                continue;
            }

            foreach (var range in blocked)
            {
                if (range.StartDate <= day && day < range.EndDate)
                {
                    state = range.Kind == BlockKind.Maintenance ? DayState.Maintenance : state == DayState.Maintenance ? state : DayState.Booked;
                }
            }

            days.Add(new CalendarDay(day, state));
        }

        return days;
    }
}
