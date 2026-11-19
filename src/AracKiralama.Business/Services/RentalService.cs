using AracKiralama.Business.Auditing;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.Domain.Repositories;
using FluentValidation;

namespace AracKiralama.Business.Services;

/// <summary>What a rental will cost and which days it holds, shown before it is confirmed.</summary>
/// <param name="EndDate">The day the car is due back: the first day no longer rented.</param>
public sealed record RentalQuote(
    int CarId,
    int Days,
    decimal DailyPrice,
    decimal Total,
    DateOnly StartDate,
    DateOnly EndDate,
    bool Discounted);

/// <summary>A stored rental.</summary>
public sealed record RentalReceipt(int RentalId, decimal Total, DateOnly StartDate, DateOnly EndDate);

/// <summary>How a rental was ended.</summary>
public enum RentalEnding
{
    /// <summary>It had not started; all its days are free again.</summary>
    Cancelled = 0,

    /// <summary>It was under way; the car is back and the days from today on are free again.</summary>
    Returned = 1,
}

public interface IRentalService
{
    /// <summary>The current settings (shortest and longest rental, booking horizon, discount).</summary>
    RentalPolicy Policy { get; }

    /// <summary>
    /// Today's date by this computer's clock, for the screens. A booking itself is checked against
    /// today by the database's clock.
    /// </summary>
    DateOnly Today { get; }

    /// <summary>
    /// Price and days of renting <paramref name="days"/> days from <paramref name="startDate"/> at the
    /// given daily price. Refuses a start in the past or beyond the booking horizon and a number of days
    /// outside the settings.
    /// </summary>
    RentalQuote Quote(int carId, decimal dailyPrice, DateOnly startDate, int days);

    /// <summary>
    /// Books a car for the signed-in account. Refused when an active rental of the car shares a day with
    /// the quoted range, when the car is unlisted or when its price is no longer the quoted one.
    /// </summary>
    Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default);

    /// <summary>The days of a car already held by active rentals, from today on.</summary>
    Task<IReadOnlyList<BookedRange>> GetBookedRangesAsync(UserSession session, int carId, CancellationToken cancellationToken = default);

    /// <summary>The signed-in account's own active rentals.</summary>
    Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Every active rental that matches <paramref name="filter"/> (admin only).</summary>
    Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Whether ending <paramref name="rental"/> today would be a return (it has started) rather than a cancellation.</summary>
    bool IsUnderway(RentalDetails rental);

    /// <summary>
    /// Ends an active rental: one that has not started is cancelled, one under way is returned today. A
    /// user can end only their own rentals; a rental of another account gets the same answer as one that
    /// does not exist. Admins can end any.
    /// </summary>
    Task<RentalEnding> EndAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default);
}

/// <remarks>
/// <see cref="Today"/>, <see cref="Quote"/> and <see cref="IsUnderway"/> use this computer's clock and
/// only prepare what a screen shows. The operations decide with today's date by the database's clock:
/// a booking that starts before it is refused, and it decides whether ending a rental cancels or
/// returns it, so a computer whose clock is wrong cannot book past days or cancel a rental under way.
/// </remarks>
internal sealed class RentalService(
    IRentalRepository rentals,
    AccessGuard access,
    TimeProvider timeProvider,
    IDatabaseClock clock,
    RentalPolicy policy,
    IAuditLog audit,
    IValidator<RentalQuoteInput> quoteValidator) : IRentalService
{
    internal const string CarNotFound = "Araç bulunamadı.";
    internal const string NotAvailable = "Bu araç şu anda kiralamaya kapalı.";
    internal const string Overlaps = "Araç seçtiğiniz tarihlerin bir kısmında başka bir kiralamada. Lütfen başka tarihler seçin.";
    internal const string PriceChanged = "Aracın günlük ücreti değişti. Lütfen güncel ücreti kontrol edip yeniden deneyin.";
    internal const string RentalNotFound = "Kiralama bulunamadı veya zaten sona ermiş.";
    internal const string StartInPast = "Kiralama geçmiş bir tarihte başlayamaz.";
    internal const string TotalTooHigh = "Toplam tutar çok yüksek; lütfen daha kısa bir süre seçin.";

    public RentalPolicy Policy => policy;

    public DateOnly Today => timeProvider.Today();

    public RentalQuote Quote(int carId, decimal dailyPrice, DateOnly startDate, int days) =>
        Quote(carId, dailyPrice, startDate, days, timeProvider.Today());

    private RentalQuote Quote(int carId, decimal dailyPrice, DateOnly startDate, int days, DateOnly today)
    {
        quoteValidator.ValidateOrThrow(new RentalQuoteInput(days, dailyPrice, startDate, today));

        var total = PriceCalculator.Total(days, dailyPrice, policy);

        return new RentalQuote(carId, days, dailyPrice, total, startDate, startDate.AddDays(days), PriceCalculator.DiscountApplies(days, policy));
    }

    public async Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quote);
        var role = await access.RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);

        // The end day and the total are worked out again here; only the car, the start day, the days and
        // the price the customer saw come from the screen, and the repository refuses a price that has
        // changed since and any range that shares a day with an active rental of the car. The first
        // allowed day is today by the database's clock.
        var today = await clock.TodayAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        var current = Quote(quote.CarId, quote.DailyPrice, quote.StartDate, quote.Days, today);
        var result = await rentals.RentAsync(
            new RentalRequest(current.CarId, session.UserId, current.StartDate, current.EndDate, current.DailyPrice, current.Total),
            cancellationToken).ConfigureAwait(false);

        var auditRole = AuditCodes.Of(role);
        switch (result.Outcome)
        {
            case RentOutcome.Rented:
                audit.Record(AuditEvent.RentalCreated, auditRole);
                return new RentalReceipt(result.RentalId, current.Total, current.StartDate, current.EndDate);
            case RentOutcome.Overlaps:
                audit.Record(AuditEvent.RentalOverlapRefused, auditRole);
                throw new BusinessRuleException(Overlaps);
            case RentOutcome.NotAvailable:
                throw new BusinessRuleException(NotAvailable);
            case RentOutcome.PriceChanged:
                throw new BusinessRuleException(PriceChanged);
            default:
                throw new BusinessRuleException(CarNotFound);
        }
    }

    public async Task<IReadOnlyList<BookedRange>> GetBookedRangesAsync(UserSession session, int carId, CancellationToken cancellationToken = default)
    {
        await access.RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);
        var today = await clock.TodayAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        return await rentals.GetBookedRangesAsync(carId, today, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await access.RequireActiveAsync(session, cancellationToken).ConfigureAwait(false);
        return await rentals.GetActiveAsync(session.UserId, CarFilter.None, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await access.RequireAdminAsync(session, cancellationToken).ConfigureAwait(false);
        return await rentals.GetActiveAsync(null, filter, cancellationToken).ConfigureAwait(false);
    }

    public bool IsUnderway(RentalDetails rental)
    {
        ArgumentNullException.ThrowIfNull(rental);
        return timeProvider.Today() >= rental.StartDate;
    }

    public async Task<RentalEnding> EndAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        // The owner filter comes from the role stored now: a demoted admin ends only their own rentals.
        // The repository checks the role again inside the write.
        var owner = await access.OwnerFilterAsync(session, cancellationToken).ConfigureAwait(false);
        var actor = new RentalActor(session.UserId, AsAdmin: owner is null);
        var today = await clock.TodayAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        var result = await access.WriteAsActorAsync(session, () => rentals.EndAsync(rentalId, actor, today, cancellationToken)).ConfigureAwait(false);
        if (result.Outcome == EndRentalOutcome.NotFound)
        {
            throw new BusinessRuleException(RentalNotFound);
        }

        audit.Record(AuditEvent.RentalEnded, owner is null ? AuditRole.Admin : AuditRole.User);
        return result.Outcome == EndRentalOutcome.Cancelled ? RentalEnding.Cancelled : RentalEnding.Returned;
    }
}
