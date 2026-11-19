using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;
using AracKiralama.UI.Theming;
using static AracKiralama.UI.Tests.Support.StaThread;

namespace AracKiralama.UI.Tests;

/// <summary>Filter loads apply only the latest answer, filling or binding lists is never a user choice, and lists mask contact data.</summary>
public sealed class ListingTests
{
    [Fact]
    public void Catalog_AnswersOutOfOrder_ShowOnlyTheLatestFilter() => Run(() =>
    {
        var cars = new ControlledCarService();
        using var form = new CarListForm(cars, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var refresh = form.RefreshAsync();
        cars.CatalogRequests[0].Answer.SetResult(cars.Cars);
        Assert.True(refresh.IsCompletedSuccessfully);

        var brand = Find<ComboBox>(form, "cmbBrand");
        brand.SelectedIndex = 1; // "Anka"
        brand.SelectedIndex = 2; // "Kartal"
        Assert.Equal([null, "Anka", "Kartal"], cars.CatalogRequests.Select(r => r.Filter.Brand));

        // The newer answer arrives first, then the stale one.
        cars.CatalogRequests[2].Answer.SetResult([cars.Cars[1]]);
        cars.CatalogRequests[1].Answer.SetResult([cars.Cars[0]]);

        Assert.Equal([2], form.ShownCars.Select(c => c.Id));
    });

    /// <summary>The grid says the cars are loading while the latest request is on its way, and that none match when it is empty.</summary>
    [Fact]
    public void Catalog_SaysWhileItLoadsAndWhenNothingMatches() => Run(() =>
    {
        var cars = new ControlledCarService();
        using var form = new CarListForm(cars, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        var grid = Find<CardGrid>(form, "grdCars");

        var refresh = form.RefreshAsync();
        Assert.Equal(grid.LoadingText, grid.Message);
        cars.CatalogRequests[0].Answer.SetResult([]);

        Assert.True(refresh.IsCompletedSuccessfully);
        Assert.Equal(grid.EmptyText, grid.Message);
        Assert.Empty(form.RentButtons);

        _ = form.LoadCarsAsync();
        cars.CatalogRequests[1].Answer.SetResult(cars.Cars);
        Assert.Null(grid.Message);
        Assert.Equal(2, form.RentButtons.Count);
        Assert.Equal(2, grid.Controls.Count);
    });

    [Fact]
    public void Catalog_FillingTheFilters_StartsNoLoadAndKeepsTheChoice() => Run(() =>
    {
        var cars = new ControlledCarService();
        using var form = new CarListForm(cars, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        _ = form.RefreshAsync();
        Assert.Single(cars.CatalogRequests);
        cars.CatalogRequests[0].Answer.SetResult(cars.Cars);

        Find<ComboBox>(form, "cmbFuelType").SelectedIndex = 2;
        cars.CatalogRequests[1].Answer.SetResult([]);
        _ = form.RefreshAsync();

        Assert.Equal(3, cars.CatalogRequests.Count);
        Assert.Equal("Dizel", cars.CatalogRequests[2].Filter.FuelType);
    });

    [Fact]
    public void AdminCars_BindingTheGrid_SelectsNothing() => Run(() =>
    {
        var cars = new ControlledCarService();
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        var grid = Find<DataGridView>(form, "dataGridView1");
        _ = grid.Handle;

        Assert.True(form.LoadCarsAsync().IsCompletedSuccessfully);

        Assert.Equal(2, grid.Rows.Count);
        Assert.Empty(grid.SelectedRows);
        Assert.Null(form.SelectedCarId);
        Assert.False(Find<Button>(form, "btnUpdateCar").Enabled);
        Assert.Empty(Find<TextBox>(form, "txtBrand").Text);

        // A click on a row is a choice; it fills the form.
        form.SelectCar(cars.Cars[1]);
        Assert.Equal("Kartal", Find<TextBox>(form, "txtBrand").Text);
        Assert.Equal("850,5", Find<TextBox>(form, "txtDailyPrice").Text);
        Assert.Equal("Otomatik", Find<ComboBox>(form, "cmbTransmission").SelectedItem);
        Assert.True(Find<Button>(form, "btnUpdateCar").Enabled);
    });

    /// <summary>
    /// A car of the original data whose gearbox or fuel type is in neither list shows that value and is
    /// saved with it unchanged; the next car shows the lists alone again.
    /// </summary>
    [Fact]
    public void AdminCars_LegacyTransmission_StaysEditable() => Run(() =>
    {
        var cars = new ControlledCarService();
        var legacy = ControlledCarService.Car(7, "Eski", "Araba");
        legacy.Transmission = "Manual";
        legacy.FuelType = "Benzinli";
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        var transmission = Find<ComboBox>(form, "cmbTransmission");

        form.SelectCar(legacy);
        Find<CheckBox>(form, "chkListed").Checked = false;
        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);

        Assert.Equal("Manual", transmission.SelectedItem);
        Assert.Equal("Manual", cars.LastUpdate!.Transmission);
        Assert.Equal("Benzinli", cars.LastUpdate.FuelType);
        Assert.False(cars.LastUpdate.IsListed);

        form.SelectCar(cars.Cars[0]);
        Assert.DoesNotContain("Manual", transmission.Items.Cast<string>());
        Assert.DoesNotContain("Benzinli", Find<ComboBox>(form, "cmbFuelType").Items.Cast<string>());
        Assert.Equal("Otomatik", transmission.SelectedItem);
    });

    /// <summary>
    /// The phone field shows digits only, so a phone of the original data in another shape is shown
    /// changed. Left as shown, the stored value is sent unchanged; typed over, the typed one is sent.
    /// </summary>
    [Fact]
    public void AccountPanel_LegacyPhoneLeftAsShown_IsSentAsStored() => Run(() =>
    {
        var accounts = new FakeAccounts();
        using var form = new UserManagementPanel(accounts, Sessions.Admin) { Prompts = new RecordingPrompts() };
        var legacy = new UserSummary(6, "eski", "eski@@ornek", "(555)1234567", UserRole.User, 0);

        form.SelectUser(legacy);
        Find<TextBox>(form, "txtPassword").Text = "Yeni.Sifre12";
        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);

        Assert.Equal("(555)1234567", accounts.LastUpdate!.Phone);
        Assert.Equal("eski@@ornek", accounts.LastUpdate.Email);

        form.SelectUser(legacy);
        Find<TextBox>(form, "txtPhone").Text = "5559876543";
        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);
        Assert.Equal("555 987 6543", accounts.LastUpdate.Phone);
    });

    [Fact]
    public void AdminCars_FirstAddWithASelection_OnlyClearsTheForm() => Run(() =>
    {
        var cars = new ControlledCarService();
        var prompts = new RecordingPrompts();
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = prompts };
        form.SelectCar(cars.Cars[0]);

        Assert.True(form.AddAsync().IsCompletedSuccessfully);

        Assert.Equal(0, cars.AddCalls);
        Assert.Null(form.SelectedCarId);
        Assert.Empty(Find<TextBox>(form, "txtBrand").Text);
        Assert.Single(prompts.Messages);
    });

    [Fact]
    public void CarRow_ShowsTheOriginalColumnNamesAndTheListing()
    {
        var car = ControlledCarService.Car(1, "Anka", "Kent");
        var headers = System.ComponentModel.TypeDescriptor.GetProperties(typeof(CarRow))
            .Cast<System.ComponentModel.PropertyDescriptor>()
            .Where(p => p.IsBrowsable)
            .Select(p => p.DisplayName);

        Assert.Equal(["AracID", "Marka", "Model", "Yil", "VitesTipi", "YakitTipi", "Km", "Renk", "Plaka", "GunlukUcret", "Durum", "Image"], headers);
        Assert.Equal("Kiralamaya açık", CarRow.Describe(car));
        car.IsListed = false;
        Assert.Equal("Kiralamaya kapalı", CarRow.Describe(car));
    }

    [Fact]
    public void UserRow_NeverShowsAPasswordAndMasksContactData()
    {
        var names = System.ComponentModel.TypeDescriptor.GetProperties(typeof(UserRow))
            .Cast<System.ComponentModel.PropertyDescriptor>()
            .Where(p => p.IsBrowsable)
            .Select(p => p.DisplayName)
            .ToList();
        var row = new UserRow(new UserSummary(2, "ornek", "ornek@example.com", "5551234567", UserRole.User, 0));

        Assert.Equal(["KullaniciID", "KullaniciAdi", "Eposta", "Telefon", "Rol"], names);
        Assert.Equal("Müşteri", row.Role);
        Assert.Equal("o***@example.com", row.Email);
        Assert.Equal("555 *** ** 67", row.Phone);
        Assert.Empty(new UserRow(new UserSummary(3, "eski", null, null, UserRole.User, 0)).Phone);
    }

    [Fact]
    public void AccountPanel_EditFieldsShowTheFullValuesOfThePickedAccountOnly() => Run(() =>
    {
        using var form = new UserManagementPanel(new FakeAccounts(), Sessions.Admin) { Prompts = new RecordingPrompts() };

        form.SelectUser(new UserSummary(2, "ornek", "ornek@example.com", "5551234567", UserRole.User, 0));

        Assert.Equal("ornek@example.com", Find<TextBox>(form, "txtEmail").Text);
        Assert.Empty(Find<TextBox>(form, "txtPassword").Text);
    });

    [Theory]
    [InlineData("5", "5")]
    [InlineData("5551", "555 1")]
    [InlineData("5551234", "555 123 4")]
    [InlineData("555 123 45678", "555 123 4567")]
    public void FormatPhone_GroupsTheDigitsAsTheOriginalForm(string typed, string shown)
    {
        Assert.Equal(shown, UiExtensions.FormatPhone(typed));
    }

    [Theory]
    [InlineData("ornek", "Ornek")]
    [InlineData("İREM", "İrem")]
    [InlineData("ılgaz", "Ilgaz")]
    public void DisplayName_CapitalizesTheFirstLetterTheTurkishWay(string name, string shown)
    {
        Assert.Equal(shown, AppText.DisplayName(name));
    }

    /// <summary>
    /// Admin A loaded an admin account before admin B demoted it. A's save carries the loaded version and
    /// is refused as stale; the form then shows the account as it is now, with its new role and version.
    /// </summary>
    [Fact]
    public void AccountPanel_StaleSave_ReloadsTheAccountWithItsNewRole() => Run(() =>
    {
        var accounts = new FakeAccounts();
        var loaded = new UserSummary(6, "xadmin", null, null, UserRole.Admin, 0);
        accounts.Users.Add(loaded);
        using var form = new UserManagementPanel(accounts, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadUsersAsync().IsCompletedSuccessfully);
        form.SelectUser(form.ShownUsers[0]);

        accounts.Users[0] = loaded with { Role = UserRole.User, Version = 1 };
        accounts.UpdateFailure = Business.BusinessRuleException.Stale("Kullanıcının rolü veya şifresi siz düzenlerken başka biri tarafından değiştirildi.");
        Find<TextBox>(form, "txtPhone").Text = "5551234567";
        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);

        Assert.Equal((6, 0), accounts.LastVersion);
        Assert.Equal(1, form.Selected!.Version);
        Assert.Equal(UserRole.User, form.Selected.Role);
        Assert.Equal("Müşteri", Find<ComboBox>(form, "cmbRole").Text);
        Assert.Contains(NoDialogs.Requested, r => r.Contains("başka biri tarafından değiştirildi", StringComparison.Ordinal));
    });

    /// <summary>An admin who changes their own password or role is told why and the panel closes as signed out.</summary>
    [Fact]
    public void AccountPanel_OwnPasswordChanged_SignsOutWithAClearMessage() => Run(() =>
    {
        var accounts = new FakeAccounts { EndsOwnSession = true };
        var prompts = new RecordingPrompts();
        using var form = new UserManagementPanel(accounts, Sessions.Admin) { Prompts = prompts };
        form.SelectUser(new UserSummary(1, "yonetici", null, null, UserRole.Admin, 0));
        Find<TextBox>(form, "txtPassword").Text = "Yeni.Sifre12";

        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);

        Assert.True(form.SessionEnded);
        Assert.Equal([UserManagementPanel.OwnSessionEndedMessage], prompts.Messages);
    });

    [Fact]
    public void AccountPanel_OtherAccountChanged_KeepsThePanelOpen() => Run(() =>
    {
        var accounts = new FakeAccounts();
        using var form = new UserManagementPanel(accounts, Sessions.Admin) { Prompts = new RecordingPrompts() };
        form.SelectUser(new UserSummary(2, "ornek", null, null, UserRole.User, 0));

        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);

        Assert.False(form.SessionEnded);
    });

    private sealed class FakeAccounts : Business.Services.IAccountService
    {
        public List<UserSummary> Users { get; } = [];

        public Task<IReadOnlyList<UserSummary>> GetAllAsync(Business.Services.UserSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSummary>>([.. Users]);

        public Task<int> AddAsync(Business.Services.UserSession session, Business.Services.AccountInput input, CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Business.Services.AccountInput? LastUpdate { get; private set; }

        /// <summary>The account and the version the last change was sent with.</summary>
        public (int UserId, int Version)? LastVersion { get; private set; }

        /// <summary>Thrown by changes instead of completing them.</summary>
        public Exception? UpdateFailure { get; set; }

        /// <summary>What the next successful change answers: whether it ended the admin's own session.</summary>
        public bool EndsOwnSession { get; set; }

        public Task<bool> UpdateAsync(Business.Services.UserSession session, int userId, int expectedVersion, Business.Services.AccountInput input, CancellationToken cancellationToken = default)
        {
            LastUpdate = input;
            LastVersion = (userId, expectedVersion);
            return UpdateFailure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(EndsOwnSession);
        }

        public Task DeleteAsync(Business.Services.UserSession session, int userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
