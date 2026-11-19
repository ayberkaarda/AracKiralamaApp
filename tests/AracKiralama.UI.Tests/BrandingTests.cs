using System.Net;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;
using AracKiralama.UI.Theming;
using static AracKiralama.UI.Tests.Support.StaThread;

namespace AracKiralama.UI.Tests;

/// <summary>The application ships only its own artwork, its windows carry neutral titles and car pictures are loaded safely.</summary>
public sealed class BrandingTests
{
    [Fact]
    public void EmbeddedArtwork_IsTheApplicationsOwn()
    {
        var resources = typeof(AppAssets).Assembly.GetManifestResourceNames().Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            ["AracKiralama.UI.Assets.app.ico", "AracKiralama.UI.Assets.background.png", "AracKiralama.UI.Assets.car.png", "AracKiralama.UI.Assets.hero.png", "AracKiralama.UI.Assets.logo-256.png"],
            resources.Where(r => r.Contains(".Assets.", StringComparison.Ordinal)));
        Assert.DoesNotContain(resources, r => r.EndsWith(".resources", StringComparison.Ordinal));
        Assert.Equal(new Size(345, 518), AppAssets.Hero.Size);
        Assert.Equal(new Size(360, 220), AppAssets.CarPlaceholder.Size);
        Assert.Equal(256, AppAssets.Logo.Width);
        Assert.NotNull(AppAssets.Icon);
    }

    [Fact]
    public void Forms_HaveNeutralTurkishTitles() => Run(() =>
    {
        var cars = new ControlledCarService();
        var rentals = new ControlledRentalService();
        var forms = new NoFormsFactory();
        Form[] all =
        [
            new LoginForm(new PendingAuthService(), forms),
            new RegisterForm(new PendingAuthService()),
            new ChangePasswordForm(new PendingAuthService(), Sessions.User),
            new CarListForm(cars, forms, Sessions.User),
            new RentalForm(cars, rentals, new NoFormsFactory(), Sessions.User, 1),
            new ProfileForm(rentals, new ControlledContractService(), forms, Sessions.User),
            new AdminPanel(cars, forms, Sessions.Admin),
            new RentedCarsPanel(rentals, cars, new ControlledContractService(), Sessions.Admin),
        ];

        try
        {
            Assert.Equal(
                ["Araç Kiralama - Giriş", "Araç Kiralama - Hesap oluştur", "Araç Kiralama - Şifre değiştir", "Araç Kirala", "Kiralama Ekranı", "Kullanıcı Profili", "Yönetim Paneli", "Kiralanan Araçlar"],
                all.Select(f => f.Text));
            Assert.All(all, f => Assert.NotNull(f.Icon));
        }
        finally
        {
            foreach (var form in all)
            {
                form.Dispose();
            }
        }
    });

    [Theory]
    [InlineData("\\\\sunucu\\pay\\arac.png")]
    [InlineData("C:\\arac.png")]
    [InlineData("file:///C:/arac.png")]
    [InlineData("ftp://example.com/arac.png")]
    [InlineData("http://localhost/arac.png")]
    [InlineData("http://127.0.0.1/arac.png")]
    [InlineData("http://10.0.0.5/arac.png")]
    [InlineData("http://172.16.0.1/arac.png")]
    [InlineData("http://192.168.1.10/arac.png")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/arac.png")]
    [InlineData("http://[fe80::1]/arac.png")]
    [InlineData("http://[fd00::1]/arac.png")]
    [InlineData("http://kullanici:sifre@example.com/arac.png")]
    [InlineData(null)]
    public void CarPicture_UnsafeAddress_ShowsThePlaceholderWithoutOpeningIt(string? address) => Run(() =>
    {
        using var picture = new PictureBox();

        picture.ShowCarPicture(address);

        Assert.False(CarPictures.TryGetAllowedUri(address, out _));
        Assert.Null(picture.Image);
    });

    /// <summary>
    /// A car without an acceptable picture shows the application's own drawing: its body style follows the model
    /// (the same model always gets the same style) and its paint the color field, in every palette.
    /// </summary>
    [Fact]
    public void VehicleArt_DrawsEveryCarInItsColorWithoutAPicture() => Run(() =>
    {
        Assert.Equal(VehicleArt.ShapeFor("Kent"), VehicleArt.ShapeFor(" kent "));
        Assert.True(Models.Select(VehicleArt.ShapeFor).Distinct().Count() > 2, "Models should get different body styles.");
        Assert.Equal(Color.FromArgb(0xC0, 0x39, 0x2B).ToArgb(), VehicleArt.PaintFor("Kırmızı").ToArgb());
        Assert.Equal(Color.FromArgb(0x23, 0x36, 0x5F).ToArgb(), VehicleArt.PaintFor("Metalik Lacivert").ToArgb());
        Assert.Equal(VehicleArt.PaintFor(null), VehicleArt.PaintFor("Belirsiz"));

        var previous = Theme.Current;
        try
        {
            foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast() })
            {
                Theme.Use(palette);
                using var picture = new VehiclePicture { Size = new Size(240, 120) };
                picture.ShowCar("Kent", "Kırmızı", "http://localhost/arac.png", "Anka Kent");
                using var image = new Bitmap(240, 120);
                picture.DrawToBitmap(image, new Rectangle(0, 0, 240, 120));

                Assert.Null(picture.Image);
                Assert.True(Colors(image) > 3, $"The {palette.Name} drawing is empty.");
            }
        }
        finally
        {
            Theme.Use(previous);
        }
    });

    private static readonly string[] Models = ["Kent", "Kasa", "Rota", "Pusula", "Liman", "Ova", "Yayla", "Koru"];

    private static int Colors(Bitmap image)
    {
        var seen = new HashSet<int>();
        for (var y = 0; y < image.Height; y += 3)
        {
            for (var x = 0; x < image.Width; x += 3)
            {
                seen.Add(image.GetPixel(x, y).ToArgb());
            }
        }

        return seen.Count;
    }

    [Theory]
    [InlineData("https://example.com/arac.png")]
    [InlineData("http://93.184.216.34/arac.png")]
    public void CarPicture_PublicWebAddress_IsAllowed(string address)
    {
        Assert.True(CarPictures.TryGetAllowedUri(address, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("169.254.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("ff02::1", false)]
    // IPv4 addresses inside IPv6 transition prefixes are judged by the IPv4 address they carry.
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("64:ff9b::7f00:1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]
    [InlineData("64:ff9b::808:808", true)]
    [InlineData("64:ff9b:1::a00:1", false)]
    [InlineData("2002:a00:1::", false)]
    [InlineData("2002:c0a8:101::1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("2002:808:808::1", true)]
    [InlineData("::127.0.0.1", false)]
    [InlineData("::10.0.0.1", false)]
    [InlineData("::169.254.169.254", false)]
    [InlineData("::8.8.8.8", true)]
    [InlineData("::", false)]
    public void IsPublicAddress_RefusesLocalPrivateAndLinkLocalAddresses(string address, bool expected)
    {
        Assert.Equal(expected, CarPictures.IsPublicAddress(IPAddress.Parse(address)));
    }

    /// <summary>
    /// A picture of 30000 x 30000 pixels compresses to far less than the download limit but would need
    /// about 3.6 GB once opened. Its size is read from the header and it is refused before anything is
    /// decoded, as is any picture wider or taller than the limit.
    /// </summary>
    [Fact]
    public void CarPicture_OversizedDimensions_AreRefusedBeforeDecoding()
    {
        var bomb = PictureFiles.Png(30_000, 30_000);
        Assert.True(bomb.Length < CarPictures.MaxBytes, "The test picture must fit the download limit.");

        // Header-only files: nothing after the size could be decoded, so only the header can refuse them.
        Assert.Equal(CarPictures.TooLarge, Assert.Throws<InvalidDataException>(() => CarPictures.Decode(PictureFiles.HeaderOnlyGif(20_000, 20_000))).Message);
        Assert.Equal(CarPictures.TooLarge, Assert.Throws<InvalidDataException>(() => CarPictures.Decode(PictureFiles.HeaderOnlyBmp(20_000, -20_000))).Message);
        Assert.Equal(CarPictures.TooLarge, Assert.Throws<InvalidDataException>(() => CarPictures.Decode(PictureFiles.HeaderOnlyJpeg(20_000, 20_000))).Message);
        Assert.Equal(CarPictures.TooLarge, Assert.Throws<InvalidDataException>(() => CarPictures.Decode(PictureFiles.Png(CarPictures.MaxDimension + 1, 1))).Message);

        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(CarPictures.TooLarge, Assert.Throws<InvalidDataException>(() => CarPictures.Decode(bomb)).Message);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1), "Refusing by the header must not decode the picture.");
    }

    [Fact]
    public void CarPicture_UnknownOrBrokenFormat_IsRefused()
    {
        byte[][] refused =
        [
            [],
            [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00], // TIFF
            [0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x10, 0x10], // ICO
            [0x89, 0x50, 0x4E, 0x47], // PNG signature only
            [0xFF, 0xD8, 0xFF, 0xD9], // JPEG without a frame
        ];

        Assert.All(refused, data => Assert.Equal(CarPictures.NotAPicture, Assert.Throws<InvalidDataException>(() => CarPictures.Decode(data)).Message));
    }

    [Theory]
    [InlineData("Png")]
    [InlineData("Jpeg")]
    [InlineData("Gif")]
    [InlineData("Bmp")]
    public void CarPicture_UsualFormatWithinTheLimits_IsDecoded(string format)
    {
        using var original = new Bitmap(40, 30);
        using var saved = new MemoryStream();
        original.Save(saved, format switch
        {
            "Png" => System.Drawing.Imaging.ImageFormat.Png,
            "Jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
            "Gif" => System.Drawing.Imaging.ImageFormat.Gif,
            _ => System.Drawing.Imaging.ImageFormat.Bmp,
        });

        using var decoded = CarPictures.Decode(saved.ToArray());

        Assert.Equal(new Size(40, 30), decoded.Size);
    }

    /// <summary>
    /// The catalog asks for every car's picture at once; only <see cref="CarPictures.MaxConcurrentLoads"/>
    /// downloads (and the decoding after them) run at the same time.
    /// </summary>
    [Fact]
    public async Task CarPicture_ConcurrentLoads_AreLimited()
    {
        var running = 0;
        var most = 0;
        var answers = Enumerable.Range(0, 8).Select(_ => new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously)).ToList();
        var loads = answers.Select(answer => CarPictures.LoadThrottledAsync(async () =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            try
            {
                return await answer.Task;
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        })).ToList();

        Assert.Equal(CarPictures.MaxConcurrentLoads, Volatile.Read(ref running));
        foreach (var answer in answers)
        {
            answer.SetResult([0x00]);
        }

        foreach (var load in loads)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => load);
        }

        Assert.Equal(CarPictures.MaxConcurrentLoads, most);
        Assert.Equal(0, running);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
