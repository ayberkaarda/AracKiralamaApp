using System.Globalization;
using System.Text;
using AracKiralama.Domain.Entities;

namespace AracKiralama.Business.Exports;

/// <summary>A rental contract as it is printed. <see cref="CreatedAt"/> is the local time by the database's clock.</summary>
public sealed record RentalContract(ContractData Data, DateTime CreatedAt);

/// <summary>
/// Writes a <see cref="RentalContract"/> as a printable HTML page. It shows the rental, the car and the
/// renter's e-mail address and phone, and nothing else of the account: no sign-in name, no account id, no
/// password, no session data. Every value is HTML encoded and the page runs nothing (see <see cref="ExportFormat.HtmlPage"/>).
/// </summary>
public static class RentalContractDocument
{
    // Static and self-contained: the page loads nothing and allows only this style block.
    internal const string Style =
        "body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#000;background:#fff;font-size:14px;line-height:1.5}"
        + "h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:24px 0 8px}p{margin:4px 0}"
        + "table{border-collapse:collapse;width:100%}th,td{border:1px solid #444;padding:6px 8px;text-align:left;vertical-align:top}"
        + "th{width:35%;background:#eee}ol{padding-left:20px}"
        + ".signatures{display:flex;gap:48px;margin-top:48px}.signatures div{flex:1;border-top:1px solid #000;padding-top:6px}"
        + "@media print{body{margin:0}th{background:#fff}}";

    internal static readonly string[] Terms =
    [
        "Araç, alış tarihinde teslim edilir ve en geç iade tarihinde teslim alınır.",
        "Kiralama bedeli rezervasyon sırasında belirlenen tutardır; erken iadede değişmez.",
        "Kiracı, aracı özenle kullanmayı ve trafik kurallarına uymayı kabul eder.",
        "Kiralama süresince oluşan hasar ve trafik cezaları kiracının sorumluluğundadır.",
        "Alış tarihinden önce yapılan iptallerde ücret alınmaz.",
    ];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static ExportFile ToHtml(RentalContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var data = contract.Data;
        var number = data.RentalId.ToString(CultureInfo.InvariantCulture);
        var title = "Araç Kiralama Sözleşmesi No " + number;
        var body = new StringBuilder();
        body.Append("<h1>").Append(ExportFormat.Html(title)).Append("</h1>\r\n")
            .Append("<p>Düzenlenme: ").Append(ExportFormat.Html(contract.CreatedAt.ToString("dd.MM.yyyy HH:mm", Turkish)))
            .Append(" - Durum: ").Append(ExportFormat.Html(Describe(data.Status))).Append("</p>\r\n");

        body.Append("<h2>Kiracı</h2>\r\n<table>\r\n");
        Row(body, "E-posta", data.RenterEmail ?? "-");
        Row(body, "Telefon", FormatPhone(data.RenterPhone));
        body.Append("</table>\r\n");

        body.Append("<h2>Araç</h2>\r\n<table>\r\n");
        Row(body, "Marka / Model", data.Brand + " " + data.Model);
        Row(body, "Model yılı", data.Year.ToString(CultureInfo.InvariantCulture));
        Row(body, "Renk", data.Color);
        Row(body, "Plaka", data.Plate);
        body.Append("</table>\r\n");

        body.Append("<h2>Kiralama</h2>\r\n<table>\r\n");
        Row(body, "Alış tarihi", Date(data.StartDate));
        Row(body, "İade tarihi", Date(data.EndDate));
        Row(body, "Kiralanan gün", (data.EndDate.DayNumber - data.StartDate.DayNumber).ToString(CultureInfo.InvariantCulture));
        if (data.ReturnedOn is { } returned)
        {
            Row(body, "Gerçek iade tarihi", Date(returned));
        }

        Row(body, "Toplam ücret", data.Price.ToString("0.00", Turkish) + " TL");
        body.Append("</table>\r\n");

        body.Append("<h2>Koşullar</h2>\r\n<ol>\r\n");
        foreach (var term in Terms)
        {
            body.Append("<li>").Append(ExportFormat.Html(term)).Append("</li>\r\n");
        }

        body.Append("</ol>\r\n")
            .Append("<div class=\"signatures\"><div>Kiraya veren</div><div>Kiracı</div></div>\r\n");

        return new ExportFile("kiralama-sozlesmesi-" + number + ".html", ExportFormat.HtmlPage(title, Style, body.ToString()));
    }

    private static void Row(StringBuilder body, string label, string value) =>
        body.Append("<tr><th>").Append(ExportFormat.Html(label)).Append("</th><td>").Append(ExportFormat.Html(value)).Append("</td></tr>\r\n");

    private static string Date(DateOnly value) => value.ToString("dd.MM.yyyy", Turkish);

    private static string FormatPhone(string? phone) =>
        phone is { Length: 10 } digits ? digits[..3] + " " + digits[3..6] + " " + digits[6..] : phone ?? "-";

    private static string Describe(RentalStatus status) => status switch
    {
        RentalStatus.Active => "Aktif",
        RentalStatus.Returned => "İade edildi",
        _ => "İptal edildi",
    };
}
