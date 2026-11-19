using System.Globalization;

namespace AracKiralama.Business;

/// <summary>Rental settings (section <c>Rental</c> of appsettings.json).</summary>
public sealed class RentalPolicy
{
    public const string SectionName = "Rental";

    /// <summary>Longest rental and booking horizon the settings may name.</summary>
    public const int MaxAllowedDays = 3650;

    /// <summary>Shortest rental, in days.</summary>
    public int MinRentalDays { get; init; } = 1;

    /// <summary>Longest rental, in days.</summary>
    public int MaxRentalDays { get; init; } = 90;

    /// <summary>How many days ahead of today a rental may start (0: only today).</summary>
    public int MaxAdvanceDays { get; init; } = 365;

    /// <summary>
    /// Rentals of at least this many days get <see cref="LongRentalDiscountPercent"/> off; 0 turns the
    /// discount off.
    /// </summary>
    public int LongRentalDays { get; init; }

    /// <summary>Discount of a long rental, in whole per cent (0 to 90); 0 means the price is always days times the daily price.</summary>
    public int LongRentalDiscountPercent { get; init; }

    /// <summary>Problems of the settings in Turkish, naming settings and limits only; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (MinRentalDays < 1 || MinRentalDays > MaxAllowedDays)
        {
            problems.Add(Text($"Rental:MinRentalDays 1 ile {MaxAllowedDays} arasında bir gün sayısı olmalıdır."));
        }

        if (MaxRentalDays < MinRentalDays || MaxRentalDays > MaxAllowedDays)
        {
            problems.Add(Text($"Rental:MaxRentalDays Rental:MinRentalDays ile {MaxAllowedDays} arasında bir gün sayısı olmalıdır."));
        }

        if (MaxAdvanceDays < 0 || MaxAdvanceDays > MaxAllowedDays)
        {
            problems.Add(Text($"Rental:MaxAdvanceDays 0 ile {MaxAllowedDays} arasında bir gün sayısı olmalıdır."));
        }

        if (LongRentalDays < 0 || LongRentalDays > MaxAllowedDays)
        {
            problems.Add(Text($"Rental:LongRentalDays 0 ile {MaxAllowedDays} arasında bir gün sayısı olmalıdır."));
        }

        if (LongRentalDiscountPercent is < 0 or > 90)
        {
            problems.Add("Rental:LongRentalDiscountPercent 0 ile 90 arasında bir yüzde olmalıdır.");
        }

        return problems;
    }

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
