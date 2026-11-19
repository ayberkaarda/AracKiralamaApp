using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace AracKiralama.UI.Infrastructure;

/// <summary>A bundled photograph and the make and models it shows.</summary>
/// <param name="Key">File name without extension under Assets/Cars, listed in docs/CREDITS.md.</param>
/// <param name="Brand">The make as it is usually written.</param>
/// <param name="Models">Model names that match, in the form <see cref="CarPhotos.Normalize"/> gives.</param>
internal sealed record CarPhotoEntry(string Key, string Brand, IReadOnlyList<string> Models);

/// <summary>
/// Photographs of common car models from Wikimedia Commons, embedded in the assembly (Assets/Cars, credited in
/// docs/CREDITS.md): nothing is downloaded. A car whose make and model match an entry shows that photograph; any
/// other car, or a photograph that is missing from the assembly, keeps the application's own drawing. The images
/// are shared by all screens and live for the whole process, so callers must not dispose them.
/// </summary>
internal static class CarPhotos
{
    /// <summary>Prefix of the embedded photographs' resource names.</summary>
    public const string ResourcePrefix = "AracKiralama.UI.Assets.Cars.";

    /// <summary>Every bundled photograph, by make; a model also matches when it continues with a trim ("Clio 1.0 TCe").</summary>
    public static IReadOnlyList<CarPhotoEntry> Entries { get; } =
    [
        new("renault-clio", "Renault", ["clio"]),
        new("fiat-egea", "Fiat", ["egea", "tipo"]),
        new("toyota-corolla", "Toyota", ["corolla"]),
        new("volkswagen-golf", "Volkswagen", ["golf"]),
        new("dacia-duster", "Dacia", ["duster"]),
        new("peugeot-2008", "Peugeot", ["2008"]),
        new("tesla-model-3", "Tesla", ["model 3", "model3"]),
        new("fiat-500", "Fiat", ["500"]),
        new("ford-transit-custom", "Ford", ["transit custom"]),
        new("volkswagen-touran", "Volkswagen", ["touran"]),
        new("mazda-mx-5", "Mazda", ["mx5", "mx 5", "miata"]),
        new("fiat-doblo", "Fiat", ["doblo"]),
    ];

    /// <summary>Other ways the makes are written, in normalized form.</summary>
    private static readonly Dictionary<string, string> BrandAliases = new(StringComparer.Ordinal)
    {
        ["vw"] = "volkswagen",
    };

    private static readonly ConcurrentDictionary<string, Image?> Loaded = new(StringComparer.Ordinal);

    /// <summary>The entry for a car's make and model, or <see langword="null"/> for a model without a bundled photograph.</summary>
    public static CarPhotoEntry? Match(string? brand, string? model)
    {
        var make = Normalize(brand);
        make = BrandAliases.GetValueOrDefault(make, make);
        var name = Normalize(model);
        if (make.Length == 0 || name.Length == 0)
        {
            return null;
        }

        return Entries.FirstOrDefault(entry =>
            Normalize(entry.Brand) == make
            && entry.Models.Any(m => name == m || name.StartsWith(m + " ", StringComparison.Ordinal)));
    }

    /// <summary>The photograph for a car's make and model, or <see langword="null"/> when there is none.</summary>
    public static Image? Find(string? brand, string? model) => Match(brand, model) is { } entry ? Load(entry.Key) : null;

    /// <summary>The embedded photograph <paramref name="key"/>, or <see langword="null"/> when it is missing or unreadable.</summary>
    public static Image? Load(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Loaded.GetOrAdd(key, static k =>
        {
            using var stream = typeof(CarPhotos).Assembly.GetManifestResourceStream(ResourcePrefix + k + ".jpg");
            if (stream is null)
            {
                return null;
            }

            try
            {
                // Image.FromStream needs the stream for the lifetime of the image; a copy detaches it.
                using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                return new Bitmap(image);
            }
            catch (ArgumentException)
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Lower case without accents or punctuation, words separated by single spaces: "  Doblò " gives "doblo",
    /// "MX-5" gives "mx5", "C-HR" gives "chr", the Turkish dotless and dotted i both give "i".
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().Replace('ı', 'i').Replace('İ', 'I').Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var c in decomposed)
        {
            if (char.IsWhiteSpace(c))
            {
                space = result.Length > 0;
            }
            else if (char.IsLetterOrDigit(c))
            {
                if (space)
                {
                    result.Append(' ');
                    space = false;
                }

                result.Append(char.ToLowerInvariant(c));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c != '-' && c != '.')
            {
                // Other punctuation separates words; accents, hyphens and dots are dropped.
                space = result.Length > 0;
            }
        }

        return result.ToString();
    }
}
