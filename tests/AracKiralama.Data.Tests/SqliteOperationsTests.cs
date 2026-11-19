using System.Text;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>The calendar, the revenue report and the contract with the real services on SQLite.</summary>
public sealed class SqliteOperationsTests : IAsyncLifetime
{
    // Demonstration data: cars 1-5 (car 1 costs 850 a day); the setup adds the admin (1) and a customer (2).
    private const int CarId = 1;
    private const int OtherCarId = 3;

    private readonly SqliteDatabase _db = new();
    private UserSession _admin = null!;
    private UserSession _customer = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.InitializeAsync();
        _admin = await SetUpAdminAsync(_db.Services);
        _customer = await RegisterAsync(_db.Services, "ornek", 1);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Calendar_ShowsRentedMaintenanceFreeAndPastDays()
    {
        await RentAsync(_db.Services, _customer, CarId, startOffset: 1, days: 2);
        await _db.Get<IMaintenanceService>().AddAsync(_admin, CarId, new MaintenanceInput(MaintenanceKind.Damage, "Cam", Today.AddDays(4), Today.AddDays(6), null), Ct);

        var calendar = await _db.Get<IAvailabilityService>().GetCalendarAsync(_customer, CarId, Today, Ct);

        Assert.Equal(new DateOnly(2026, 3, 1), calendar.Month);
        Assert.Equal(Today, calendar.Today);
        Assert.Equal(
            [DayState.Past, DayState.Free, DayState.Booked, DayState.Booked, DayState.Free, DayState.Maintenance, DayState.Maintenance, DayState.Free],
            calendar.Days.Take(8).Select(d => d.State));
        Assert.Equal(31, calendar.Days.Count);
    }

    [Fact]
    public async Task Calendar_OfAnUnlistedOrMissingCar_IsRefusedToCustomersAlike()
    {
        var cars = _db.Get<ICarService>();
        var car = await cars.GetAsync(_admin, CarId, Ct);
        await cars.UpdateAsync(_admin, CarId, await CarVersionAsync(_db.Services, CarId), new CarInput(car.Brand, car.Model, "2021", car.Transmission, car.FuelType, "1000", car.Color, car.Plate, "850", null, false), Ct);
        var availability = _db.Get<IAvailabilityService>();

        var unlisted = await Assert.ThrowsAsync<BusinessRuleException>(() => availability.GetCalendarAsync(_customer, CarId, Today, Ct));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => availability.GetCalendarAsync(_customer, 999, Today, Ct));

        Assert.Equal(unlisted.Message, missing.Message);
        Assert.Equal(31, (await availability.GetCalendarAsync(_admin, CarId, Today, Ct)).Days.Count);
    }

    [Fact]
    public async Task Revenue_CountsActiveAndReturnedRentalsByPickUpDayAndCar()
    {
        var rentals = _db.Get<IRentalService>();
        await RentAsync(_db.Services, _customer, CarId, startOffset: 0, days: 2);           // 1700, returned today
        var cancelled = await RentAsync(_db.Services, _customer, CarId, startOffset: 5, days: 1);
        await RentAsync(_db.Services, _customer, OtherCarId, startOffset: 40, days: 3);
        await rentals.EndAsync(_customer, cancelled.RentalId, Ct);
        var active = (await rentals.GetMyActiveRentalsAsync(_customer, Ct)).Single(r => r.CarId == CarId);
        await rentals.EndAsync(_customer, active.Id, Ct);
        var otherPrice = (await rentals.GetMyActiveRentalsAsync(_customer, Ct)).Single().Price;

        var report = await _db.Get<IReportService>().GetRevenueAsync(_admin, Today, Today.AddDays(60), RevenueGrouping.Month, Ct);

        Assert.Equal(["Mart 2026", "Nisan 2026", "Mayıs 2026"], report.Periods.Select(p => p.Label));
        Assert.Equal([1700m, otherPrice, 0m], report.Periods.Select(p => p.Revenue));
        Assert.Equal(new RevenueLine("Toplam", null, 2, 5, 1700m + otherPrice), report.Total);
        Assert.Equal(2, report.Cars.Count);
        Assert.Equal(Start.UtcDateTime, report.CreatedAt);

        var narrow = await _db.Get<IReportService>().GetRevenueAsync(_admin, Today.AddDays(1), Today.AddDays(39), RevenueGrouping.Day, Ct);
        Assert.Equal(0, narrow.Total.Rentals);
    }

    [Fact]
    public async Task Revenue_IsForAdminsOnly()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => _db.Get<IReportService>().GetRevenueAsync(_customer, Today, Today, RevenueGrouping.Day, Ct));
    }

    [Fact]
    public async Task Contract_ForTheRenterAndAdmins_AndTheSameRefusalForOthersAsForAMissingRental()
    {
        var receipt = await RentAsync(_db.Services, _customer, CarId, startOffset: 2, days: 3);
        var other = await RegisterAsync(_db.Services, "baska", 2);
        var contracts = _db.Get<IContractService>();

        var own = Encoding.UTF8.GetString((await contracts.CreateAsync(_customer, receipt.RentalId, Ct)).Content);
        var admins = await contracts.CreateAsync(_admin, receipt.RentalId, Ct);
        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() => contracts.CreateAsync(other, receipt.RentalId, Ct));
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => contracts.CreateAsync(other, 999, Ct));

        Assert.Contains("<td>ornek@example.com</td>", own, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>ornek</td>", own, StringComparison.Ordinal);
        Assert.Contains("<td>2550,00 TL</td>", own, StringComparison.Ordinal);
        Assert.Equal("kiralama-sozlesmesi-" + receipt.RentalId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".html", admins.SuggestedName);
        Assert.Equal(foreign.Message, missing.Message);
        Assert.Equal(ContractService.NotFound, foreign.Message);
    }
}
