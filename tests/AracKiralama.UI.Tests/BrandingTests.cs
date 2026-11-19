using System.Net;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;
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
            ["AracKiralama.UI.Assets.app.ico", "AracKiralama.UI.Assets.background.png", "AracKiralama.UI.Assets.car.png", "AracKiralama.UI.Assets.hero.png", "AracKiralama.UI.Assets.logo.png"],
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
            new RentalForm(cars, rentals, Sessions.User, 1),
            new ProfileForm(rentals, forms, Sessions.User),
            new AdminPanel(cars, forms, Sessions.Admin),
            new RentedCarsPanel(rentals, cars, Sessions.Admin),
        ];

        try
        {
            Assert.Equal(
                ["Araç Kiralama", "Kayıt Ol", "Şifre Değiştir", "Araç Kirala", "Kiralama Ekranı", "Kullanıcı Profili", "Yönetim Paneli", "Kiralanan Araçlar"],
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
        Assert.Same(AppAssets.CarPlaceholder, picture.Image);
    });

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
    public void IsPublicAddress_RefusesLocalPrivateAndLinkLocalAddresses(string address, bool expected)
    {
        Assert.Equal(expected, CarPictures.IsPublicAddress(IPAddress.Parse(address)));
    }
}
