using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI.Tests;

/// <summary>The bundled car photographs: each one is credited in docs/CREDITS.md, small and matched by make and model.</summary>
public sealed class CarPhotoTests
{
    /// <summary>Largest bundled photograph file.</summary>
    private const int MaxFileBytes = 160 * 1024;

    /// <summary>Largest total of all bundled photographs.</summary>
    private const int MaxTotalBytes = 3 * 1024 * 1024;

    [Fact]
    public void EveryBundledPhoto_IsCreditedSmallAndInTheCatalog()
    {
        var assembly = typeof(CarPhotos).Assembly;
        var resources = assembly.GetManifestResourceNames().Where(r => r.StartsWith(CarPhotos.ResourcePrefix, StringComparison.Ordinal)).ToList();
        var files = resources.Select(r => r[CarPhotos.ResourcePrefix.Length..]).Order(StringComparer.Ordinal).ToList();
        var credits = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "CREDITS.md"));

        Assert.NotEmpty(files);
        Assert.Equal(CarPhotos.Entries.Select(e => e.Key + ".jpg").Order(StringComparer.Ordinal), files);
        var total = 0L;
        foreach (var resource in resources)
        {
            var file = resource[CarPhotos.ResourcePrefix.Length..];
            using var stream = assembly.GetManifestResourceStream(resource)!;
            Assert.True(stream.Length <= MaxFileBytes, $"{file} is {stream.Length} bytes; the limit is {MaxFileBytes}.");
            total += stream.Length;

            // Every file has its own section with the author, the license and the source page.
            var start = credits.IndexOf("`" + file + "`", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{file} is not credited in docs/CREDITS.md.");
            var end = credits.IndexOf("\n### ", start, StringComparison.Ordinal);
            var section = end < 0 ? credits[start..] : credits[start..end];
            Assert.Contains("Author", section, StringComparison.Ordinal);
            Assert.Contains("https://commons.wikimedia.org/wiki/File:", section, StringComparison.Ordinal);
            Assert.Matches(@"License: \[(CC0 1\.0|Public domain|CC BY(-SA)? [0-9.]+( [a-z]{2})?)\]\(https://", section);

            var image = CarPhotos.Load(file[..^4]);
            Assert.NotNull(image);
            Assert.True(image.Width <= 640 && image.Height <= 400, $"{file} is {image.Width}x{image.Height}.");
        }

        Assert.True(total <= MaxTotalBytes, $"The photographs take {total} bytes; the limit is {MaxTotalBytes}.");
        Assert.DoesNotContain("NonCommercial", credits, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NoDerivatives", credits, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Renault", "Clio", "renault-clio")]
    [InlineData(" RENAULT ", "clio 1.0 TCe Joy", "renault-clio")]
    [InlineData("VW", "Golf", "volkswagen-golf")]
    [InlineData("Fiat", "Egea Sedan", "fiat-egea")]
    [InlineData("Fiat", "Doblò Combi", "fiat-doblo")]
    [InlineData("Mazda", "MX-5", "mazda-mx-5")]
    [InlineData("Tesla", "Model 3", "tesla-model-3")]
    [InlineData("Ford", "Transit Custom Uzun Şaseli Panelvan", "ford-transit-custom")]
    [InlineData("Renault", "Megane", null)]
    [InlineData("Fiat", "500X", null)]
    [InlineData("Ford", "Transit", null)]
    [InlineData("Dacia", "Clio", null)]
    [InlineData("Anka", "Kent", null)]
    [InlineData(null, "Clio", null)]
    [InlineData("Renault", null, null)]
    public void Match_FindsTheMakeAndModelOrNothing(string? brand, string? model, string? key)
    {
        Assert.Equal(key, CarPhotos.Match(brand, model)?.Key);
    }

    [Fact]
    public void Normalize_DropsCaseAccentsAndPunctuation()
    {
        Assert.Equal("doblo", CarPhotos.Normalize("  Doblò "));
        Assert.Equal("mx5", CarPhotos.Normalize("MX-5"));
        Assert.Equal("transit custom", CarPhotos.Normalize("Transit   Custom"));
        Assert.Equal("isik", CarPhotos.Normalize("IŞIK"));
        Assert.Equal(string.Empty, CarPhotos.Normalize(null));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AracKiralama.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("AracKiralama.sln was not found above the test's folder.");
    }
}
