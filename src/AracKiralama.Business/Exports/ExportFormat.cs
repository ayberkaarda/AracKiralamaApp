using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AracKiralama.Business.Exports;

/// <summary>A file to save: a suggested name and its content.</summary>
public sealed record ExportFile(string SuggestedName, byte[] Content);

/// <summary>
/// The safe forms of exported files. Everything an exported file contains leaves the application's
/// control, so values typed by people (car names, plates, user names, descriptions) are written so that
/// neither a spreadsheet nor a browser runs them.
/// </summary>
internal static class ExportFormat
{
    /// <summary>
    /// A CSV field as a spreadsheet must take it: always quoted, inner quotes doubled, and a leading "'"
    /// before any value a spreadsheet would run as a formula or a command (=, +, -, @, tab, carriage
    /// return, line feed), also after leading spaces.
    /// </summary>
    public static string CsvField(string? value)
    {
        var text = value ?? string.Empty;
        var first = text.TrimStart(' ');
        if (first.Length > 0 && first[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>One CSV line: the fields joined with ";" (the Turkish list separator) and CRLF.</summary>
    public static void AppendCsvLine(StringBuilder text, IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(text);
        text.AppendJoin(';', fields.Select(CsvField)).Append("\r\n");
    }

    /// <summary>UTF-8 with a byte order mark, so Excel reads the Turkish letters.</summary>
    public static byte[] CsvBytes(string csv)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. encoding.GetPreamble(), .. encoding.GetBytes(csv)];
    }

    /// <summary>HTML text: every value is encoded, never written as markup.</summary>
    public static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>
    /// A self-contained printable page: no script, no external resource, a Content-Security-Policy that
    /// allows nothing but <paramref name="style"/>, pinned by its hash, and no referrer.
    /// <paramref name="body"/> must already be encoded.
    /// </summary>
    public static byte[] HtmlPage(string title, string style, string body)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\r\n<html lang=\"tr\">\r\n<head>\r\n<meta charset=\"utf-8\">\r\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src '")
            .Append(StyleHash(style))
            .Append("'; img-src 'none'; base-uri 'none'; form-action 'none'\">\r\n")
            .Append("<meta name=\"referrer\" content=\"no-referrer\">\r\n")
            .Append("<title>").Append(Html(title)).Append("</title>\r\n")
            .Append("<style>").Append(style).Append("</style>\r\n</head>\r\n<body>\r\n")
            .Append(body)
            .Append("</body>\r\n</html>\r\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(html.ToString());
    }

    /// <summary>The CSP source of a style block: "sha256-" and the Base64 of its SHA-256.</summary>
    public static string StyleHash(string style) =>
        "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(style)));
}
