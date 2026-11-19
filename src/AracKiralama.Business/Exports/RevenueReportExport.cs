using System.Globalization;
using System.Text;
using AracKiralama.Business.Services;

namespace AracKiralama.Business.Exports;

/// <summary>Writes a <see cref="RevenueReport"/> as CSV.</summary>
public static class RevenueReportExport
{
    internal static readonly string[] Headers = ["Bölüm", "Ad", "Plaka", "Kiralama sayısı", "Kiralanan gün", "Gelir (TL)"];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// CSV for spreadsheets (see <see cref="ExportFormat.CsvField"/>): a header, one line per period, one
    /// per car and the total. Amounts have a decimal comma and no thousands separator.
    /// </summary>
    public static ExportFile ToCsv(RevenueReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        ExportFormat.AppendCsvLine(text, Headers);
        foreach (var line in report.Periods)
        {
            Append(text, report.Grouping == RevenueGrouping.Month ? "Ay" : "Gün", line);
        }

        foreach (var line in report.Cars)
        {
            Append(text, "Araç", line);
        }

        Append(text, "Toplam", report.Total with { Label = string.Empty });

        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"gelir-raporu-{report.From:yyyy-MM-dd}-{report.To:yyyy-MM-dd}.csv");
        return new ExportFile(name, ExportFormat.CsvBytes(text.ToString()));
    }

    private static void Append(StringBuilder text, string section, RevenueLine line) =>
        ExportFormat.AppendCsvLine(
            text,
            [
                section,
                line.Label,
                line.Plate ?? string.Empty,
                line.Rentals.ToString(CultureInfo.InvariantCulture),
                line.Days.ToString(CultureInfo.InvariantCulture),
                line.Revenue.ToString("0.00", Turkish),
            ]);
}
