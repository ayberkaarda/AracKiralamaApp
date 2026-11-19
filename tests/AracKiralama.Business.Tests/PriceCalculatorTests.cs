using System.Globalization;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;

namespace AracKiralama.Business.Tests;

public sealed class PriceCalculatorTests
{
    private static readonly RentalPolicy NoDiscount = new();

    [Theory]
    [InlineData(1, "850.00", "850.00")]
    [InlineData(3, "12.50", "37.50")]
    [InlineData(7, "9.99", "69.93")]
    [InlineData(365, "0.01", "3.65")]
    public void Total_IsDaysTimesTheDailyPrice(int days, string dailyPrice, string expected)
    {
        Assert.Equal(Parse(expected), PriceCalculator.Total(days, Parse(dailyPrice), NoDiscount));
    }

    [Fact]
    public void Total_IsExactInDecimal()
    {
        // 0.1 is not exact in binary floating point; three days at 0.10 are exactly 0.30 in decimal.
        Assert.Equal(0.30m, PriceCalculator.Total(3, 0.10m, NoDiscount));
    }

    [Fact]
    public void Total_LongRentalDiscount_AppliesFromItsFirstDayAndIsRounded()
    {
        var policy = new RentalPolicy { LongRentalDays = 7, LongRentalDiscountPercent = 10 };

        Assert.Equal(600m, PriceCalculator.Total(6, 100m, policy));
        Assert.Equal(630m, PriceCalculator.Total(7, 100m, policy));
        Assert.Equal(66.69m, PriceCalculator.Total(7, 10.585m, policy));
        Assert.False(PriceCalculator.DiscountApplies(6, policy));
        Assert.True(PriceCalculator.DiscountApplies(7, policy));
    }

    [Fact]
    public void Total_WithoutADiscountRule_IsAlwaysDaysTimesThePrice()
    {
        Assert.False(PriceCalculator.DiscountApplies(1000, new RentalPolicy { LongRentalDays = 7, LongRentalDiscountPercent = 0 }));
        Assert.False(PriceCalculator.DiscountApplies(1000, new RentalPolicy { LongRentalDays = 0, LongRentalDiscountPercent = 20 }));
    }

    [Fact]
    public void Total_RefusesNoDaysAndNegativePrices()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceCalculator.Total(0, 10m, NoDiscount));
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceCalculator.Total(3, -1m, NoDiscount));
    }

    [Fact]
    public void Total_IsNeverNegative()
    {
        Assert.Equal(0m, PriceCalculator.Total(5, 0m, new RentalPolicy { LongRentalDays = 1, LongRentalDiscountPercent = 90 }));
    }

    /// <summary>The overlap rule of half-open day ranges: touching ranges are free, one shared day is not.</summary>
    [Theory]
    [InlineData(1, 6, 6, 8, false)]
    [InlineData(6, 8, 1, 6, false)]
    [InlineData(1, 6, 5, 7, true)]
    [InlineData(1, 6, 1, 2, true)]
    [InlineData(1, 6, 2, 4, true)]
    [InlineData(2, 4, 1, 6, true)]
    [InlineData(1, 2, 2, 3, false)]
    public void BookedRange_Overlaps_TouchingRangesDoNot(int bookedStart, int bookedEnd, int start, int end, bool overlaps)
    {
        var day = new DateOnly(2026, 3, 1);
        var booked = new BookedRange(day.AddDays(bookedStart), day.AddDays(bookedEnd));

        Assert.Equal(overlaps, booked.Overlaps(day.AddDays(start), day.AddDays(end)));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
