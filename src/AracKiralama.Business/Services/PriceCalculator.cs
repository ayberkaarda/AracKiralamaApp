namespace AracKiralama.Business.Services;

/// <summary>Rental price arithmetic, in decimal so kuruş amounts stay exact.</summary>
internal static class PriceCalculator
{
    /// <summary>Largest amount the DECIMAL(10, 2) money columns can hold.</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    /// <summary>
    /// Price of <paramref name="days"/> days at <paramref name="dailyPrice"/>: days times the daily price,
    /// less the long rental discount of <paramref name="policy"/> when it applies, rounded to kuruş.
    /// Never negative.
    /// </summary>
    public static decimal Total(int days, decimal dailyPrice, RentalPolicy policy)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(dailyPrice);
        ArgumentNullException.ThrowIfNull(policy);

        var total = days * dailyPrice;
        if (DiscountApplies(days, policy))
        {
            total = total * (100 - policy.LongRentalDiscountPercent) / 100m;
        }

        return Math.Max(0m, decimal.Round(total, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Whether a rental of <paramref name="days"/> days gets the long rental discount.</summary>
    public static bool DiscountApplies(int days, RentalPolicy policy) =>
        policy.LongRentalDays > 0 && policy.LongRentalDiscountPercent > 0 && days >= policy.LongRentalDays;
}
