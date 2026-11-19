using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;
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
        var row = new UserRow(new UserSummary(2, "ornek", "ornek@example.com", "5551234567", UserRole.User));

        Assert.Equal(["KullaniciID", "KullaniciAdi", "Eposta", "Telefon", "Rol"], names);
        Assert.Equal("Müşteri", row.Role);
        Assert.Equal("o***@example.com", row.Email);
        Assert.Equal("555 *** ** 67", row.Phone);
        Assert.Empty(new UserRow(new UserSummary(3, "eski", null, null, UserRole.User)).Phone);
    }

    [Fact]
    public void AccountPanel_EditFieldsShowTheFullValuesOfThePickedAccountOnly() => Run(() =>
    {
        using var form = new UserManagementPanel(new FakeAccounts(), Sessions.Admin) { Prompts = new RecordingPrompts() };

        form.SelectUser(new UserSummary(2, "ornek", "ornek@example.com", "5551234567", UserRole.User));

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

    private sealed class FakeAccounts : Business.Services.IAccountService
    {
        public Task<IReadOnlyList<UserSummary>> GetAllAsync(Business.Services.UserSession session, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSummary>>([]);

        public Task<int> AddAsync(Business.Services.UserSession session, Business.Services.AccountInput input, CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task UpdateAsync(Business.Services.UserSession session, int userId, Business.Services.AccountInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Business.Services.UserSession session, int userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
