using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI.Tests.Support;

/// <summary>
/// Creates every screen with test doubles for its dependencies and with long names in its lists, so no window,
/// icon or database is involved and the layout is checked with the hardest content.
/// </summary>
internal static class TestForms
{
    public const string LongBrand = "Uzunadlıbirmarka Otomotiv";

    public const string LongModel = "Yüksek Tavanlı Uzun Şaseli Panelvan Kombi Konfor Paketi";

    public const string LongUserName = "ada.surucu.cok.uzun.bir.kullanici.adi.ornegi";

    /// <summary>The screens of the application by class name.</summary>
    public static readonly string[] Names =
    [
        nameof(LoginForm), nameof(RegisterForm), nameof(FirstAdminSetupForm), nameof(ChangePasswordForm), nameof(CarListForm),
        nameof(RentalForm), nameof(ProfileForm), nameof(RentedCarsPanel), nameof(AdminPanel), nameof(UserManagementPanel),
        nameof(AvailabilityCalendarForm), nameof(MaintenanceForm), nameof(RevenueReportForm),
    ];

    public static AppDialog Create(string name) => name switch
    {
        nameof(LoginForm) => new LoginForm(new PendingAuthService(), new NoFormsFactory()) { OfferAdminSetup = true },
        nameof(RegisterForm) => new RegisterForm(new PendingAuthService()),
        nameof(FirstAdminSetupForm) => new FirstAdminSetupForm(new PendingSetupService()) { RequireSetupKey = true },
        nameof(ChangePasswordForm) => new ChangePasswordForm(new PendingAuthService(), Sessions.User),
        nameof(CarListForm) => CarList(),
        nameof(RentalForm) => Rental(),
        nameof(ProfileForm) => Profile(),
        nameof(RentedCarsPanel) => Rentals(),
        nameof(AdminPanel) => Admin(),
        nameof(UserManagementPanel) => Accounts(),
        nameof(AvailabilityCalendarForm) => Calendar(),
        nameof(MaintenanceForm) => Maintenance(),
        nameof(RevenueReportForm) => Report(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>A car service whose first car has a very long brand and model.</summary>
    public static ControlledCarService LongCars()
    {
        var cars = new ControlledCarService();
        cars.Cars.Insert(0, ControlledCarService.Car(9, LongBrand, LongModel));
        return cars;
    }

    /// <summary>A rental service whose first rental has a very long car name and renter.</summary>
    public static ControlledRentalService LongRentals()
    {
        var rentals = new ControlledRentalService();
        rentals.Rentals.Insert(0, ControlledRentalService.Rental(8) with { Brand = LongBrand, Model = LongModel, UserName = LongUserName });
        return rentals;
    }

    public static CarListForm CarList()
    {
        var cars = LongCars();
        var form = new CarListForm(cars, new NoFormsFactory(), Sessions.User);
        var refresh = form.RefreshAsync();
        cars.CatalogRequests[0].Answer.SetResult(cars.Cars);
        Assert.True(refresh.IsCompletedSuccessfully);
        return form;
    }

    public static RentalForm Rental()
    {
        var cars = LongCars();
        var form = new RentalForm(cars, new ControlledRentalService(), new NoFormsFactory(), Sessions.User, 9);
        Assert.True(form.LoadCarAsync().IsCompletedSuccessfully);
        return form;
    }

    public static ProfileForm Profile()
    {
        var form = new ProfileForm(LongRentals(), new ControlledContractService(), new NoFormsFactory(), Sessions.User);
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);
        return form;
    }

    public static RentedCarsPanel Rentals()
    {
        var form = new RentedCarsPanel(LongRentals(), new ControlledCarService(), new ControlledContractService(), Sessions.Admin);
        Assert.True(form.RefreshAsync().IsCompletedSuccessfully);
        return form;
    }

    public static AdminPanel Admin()
    {
        var cars = LongCars();
        var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin);
        Assert.True(form.LoadCarsAsync().IsCompletedSuccessfully);
        form.SelectCar(cars.Cars[0]);
        return form;
    }

    public static UserManagementPanel Accounts()
    {
        var form = new UserManagementPanel(new ListedAccounts(), Sessions.Admin);
        Assert.True(form.LoadUsersAsync().IsCompletedSuccessfully);
        return form;
    }

    public static AvailabilityCalendarForm Calendar()
    {
        var availability = new ControlledAvailabilityService();
        var form = new AvailabilityCalendarForm(availability, Sessions.User, 1, LongBrand + " " + LongModel);
        var load = form.LoadMonthAsync(new DateOnly(2026, 3, 1));
        availability.Requests[0].Answer.SetResult(ControlledAvailabilityService.Calendar(new DateOnly(2026, 3, 1), DayState.Past, DayState.Booked, DayState.Maintenance));
        Assert.True(load.IsCompletedSuccessfully);
        return form;
    }

    public static MaintenanceForm Maintenance()
    {
        var form = new MaintenanceForm(new ControlledMaintenanceService(), Sessions.Admin, 1, LongBrand + " " + LongModel);
        Assert.True(form.LoadRecordsAsync().IsCompletedSuccessfully);
        return form;
    }

    public static RevenueReportForm Report()
    {
        var reports = new ControlledReportService();
        var form = new RevenueReportForm(reports, Sessions.Admin);
        var load = form.LoadReportAsync();
        reports.Requests[0].SetResult(ControlledReportService.Report(123_456.78m));
        Assert.True(load.IsCompletedSuccessfully);
        return form;
    }
}

/// <summary>A setup service that is only drawn: the key prefill is empty and creating an admin never completes.</summary>
internal sealed class PendingSetupService : ISetupService
{
    public Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public SetupKeyPrefill GetEnvironmentKey() => new(null, false);

    public Task<UserSession> CreateFirstAdminAsync(string? setupKey, string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default) =>
        new TaskCompletionSource<UserSession>().Task;
}

/// <summary>Answers with a fixed list of accounts, one with a very long name and contact data; writes are refused.</summary>
internal sealed class ListedAccounts : IAccountService
{
    public Task<IReadOnlyList<UserSummary>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(
        [
            new UserSummary(1, "yonetici", null, null, UserRole.Admin, 0),
            new UserSummary(2, TestForms.LongUserName, "cok.uzun.bir.eposta.adresi.ornegi@example.com", "5551234567", UserRole.User, 0),
            new UserSummary(3, "ornek", "ornek@example.com", null, UserRole.User, 0),
        ]);

    public Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default) => Refused<int>();

    public Task<bool> UpdateAsync(UserSession session, int userId, int expectedVersion, AccountInput input, CancellationToken cancellationToken = default) => Refused<bool>();

    public Task DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken = default) => Refused<bool>();

    private static Task<T> Refused<T>() => Task.FromException<T>(new BusinessRuleException("Bu testte yazılmaz."));
}
