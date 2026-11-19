using System.Text;
using AracKiralama.Business.Exports;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;

namespace AracKiralama.Business.Tests;

/// <summary>The revenue CSV and the contract page: formula injection, HTML encoding, the CSP and what a contract leaves out.</summary>
public sealed class ExportTests
{
    private static readonly DateTime Created = new(2026, 3, 2, 14, 5, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData("Anka", "\"Anka\"")]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+90 555", "\"'+90 555\"")]
    [InlineData("-2", "\"'-2\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("\rx", "\"'\rx\"")]
    [InlineData("\nx", "\"'\nx\"")]
    [InlineData("  =cmd|' /C calc'!A0", "\"'  =cmd|' /C calc'!A0\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("", "\"\"")]
    [InlineData(null, "\"\"")]
    public void CsvField_QuotesEveryValueAndDisarmsFormulas(string? value, string expected) =>
        Assert.Equal(expected, ExportFormat.CsvField(value));

    [Fact]
    public void RevenueCsv_HasABomTheHeaderEveryLineAndTurkishAmounts()
    {
        var report = new RevenueReport(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            RevenueGrouping.Month,
            [new RevenueLine("Mart 2026", null, 2, 5, 1250.5m)],
            [new RevenueLine("=HYPERLINK(\"x\") Kent", "00 DMO 001", 2, 5, 1250.5m)],
            new RevenueLine("Toplam", null, 2, 5, 1250.5m),
            Created);

        var file = RevenueReportExport.ToCsv(report);

        Assert.Equal("gelir-raporu-2026-03-01-2026-03-31.csv", file.SuggestedName);
        Assert.Equal([0xEF, 0xBB, 0xBF], file.Content[..3]);
        var lines = Encoding.UTF8.GetString(file.Content[3..]).Split("\r\n");
        Assert.Equal(
            [
                "\"Bölüm\";\"Ad\";\"Plaka\";\"Kiralama sayısı\";\"Kiralanan gün\";\"Gelir (TL)\"",
                "\"Ay\";\"Mart 2026\";\"\";\"2\";\"5\";\"1250,50\"",
                "\"Araç\";\"'=HYPERLINK(\"\"x\"\") Kent\";\"00 DMO 001\";\"2\";\"5\";\"1250,50\"",
                "\"Toplam\";\"\";\"\";\"2\";\"5\";\"1250,50\"",
                string.Empty,
            ],
            lines);
    }

    [Fact]
    public void Contract_EncodesEveryValueAndRunsNothing()
    {
        var data = Data() with { Brand = "<script>alert(1)</script>", RenterName = "a&b\"<img src=x onerror=alert(1)>" };

        var file = RentalContractDocument.ToHtml(new RentalContract(data, Created));
        var html = Encoding.UTF8.GetString(file.Content);

        Assert.Equal("kiralama-sozlesmesi-42.html", file.SuggestedName);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("a&amp;b&quot;&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_HasAStrictPolicyPinnedToItsOwnStyle()
    {
        var html = Encoding.UTF8.GetString(RentalContractDocument.ToHtml(new RentalContract(Data(), Created)).Content);

        var policy = "default-src 'none'; style-src '" + ExportFormat.StyleHash(RentalContractDocument.Style) + "'; img-src 'none'; base-uri 'none'; form-action 'none'";
        Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"" + policy + "\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"referrer\" content=\"no-referrer\">", html, StringComparison.Ordinal);
        Assert.Contains("<style>" + RentalContractDocument.Style + "</style>", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(html, "<style>"));
        Assert.DoesNotContain(" style=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_ShowsTheRentalAndTheRenterButNoAccountInternals()
    {
        var html = Encoding.UTF8.GetString(RentalContractDocument.ToHtml(new RentalContract(Data(), Created)).Content);

        Assert.Contains("Araç Kiralama Sözleşmesi No 42", html, StringComparison.Ordinal);
        Assert.Contains("Düzenlenme: 02.03.2026 14:05", html, StringComparison.Ordinal);
        Assert.Contains("<td>ornek</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>555 123 4567</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>04.03.2026</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>07.03.2026</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>3</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>2551,50 TL</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Gerçek iade", html, StringComparison.Ordinal);
        foreach (var internals in new[] { "KullaniciID", "Sifre", "Şifre", "PBKDF2", "OturumSurumu", "Server=", "Password" })
        {
            Assert.DoesNotContain(internals, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Contract_OfAReturnedRental_ShowsTheReturnDay()
    {
        var data = Data() with { Status = RentalStatus.Returned, ReturnedOn = new DateOnly(2026, 3, 6) };

        var html = Encoding.UTF8.GetString(RentalContractDocument.ToHtml(new RentalContract(data, Created)).Content);

        Assert.Contains("Durum: İade edildi", html, StringComparison.Ordinal);
        Assert.Contains("<th>Gerçek iade tarihi</th><td>06.03.2026</td>", html, StringComparison.Ordinal);
    }

    private static ContractData Data() => new(
        42,
        RentalStatus.Active,
        "Anka",
        "Kent",
        2021,
        "Beyaz",
        "00 DMO 001",
        "ornek",
        "ornek@example.com",
        "5551234567",
        new DateOnly(2026, 3, 4),
        new DateOnly(2026, 3, 7),
        null,
        2551.5m);

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
