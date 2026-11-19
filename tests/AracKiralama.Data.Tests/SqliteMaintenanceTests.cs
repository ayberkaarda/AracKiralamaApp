using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>The maintenance and damage log with the real services on SQLite: blocked days, overlaps, stale forms and rights.</summary>
public sealed class SqliteMaintenanceTests : IAsyncLifetime
{
    // Demonstration data: cars 1-5; the setup below adds the admin (1) and a customer (2).
    private const int CarId = 1;

    private readonly SqliteDatabase _db = new();
    private UserSession _admin = null!;
    private UserSession _customer = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IMaintenanceService Maintenance => _db.Get<IMaintenanceService>();

    public async ValueTask InitializeAsync()
    {
        await _db.InitializeAsync();
        _admin = await SetUpAdminAsync(_db.Services);
        _customer = await RegisterAsync(_db.Services, "ornek", 1);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Add_StoresTheRecordStampedWithTheDatabaseClock()
    {
        _db.Clock.Advance(TimeSpan.FromMinutes(5));

        var id = await Maintenance.AddAsync(_admin, CarId, Input(5, 3, "  Periyodik bakım  ", "1250,50"), Ct);

        var record = Assert.Single(await Maintenance.GetForCarAsync(_admin, CarId, Ct));
        Assert.Equal(id, record.Id);
        Assert.Equal(MaintenanceKind.Maintenance, record.Kind);
        Assert.Equal("Periyodik bakım", record.Description);
        Assert.Equal(Today.AddDays(5), record.StartDate);
        Assert.Equal(Today.AddDays(8), record.EndDate);
        Assert.Equal(1250.50m, record.Cost);
        Assert.Equal(MaintenanceStatus.Active, record.Status);
        Assert.Equal(1, record.Version);
        Assert.Equal(Start.UtcDateTime.AddMinutes(5), record.CreatedAt);
        Assert.Equal(record.CreatedAt, record.UpdatedAt);
    }

    /// <summary>Boundary values of a booking against maintenance on days 5, 6 and 7 (end day 8).</summary>
    [Theory]
    [InlineData(8, 2, true)]   // starts on the day the car is back: allowed
    [InlineData(2, 3, true)]   // ends on the day the maintenance starts: allowed
    [InlineData(7, 2, false)]  // shares the last day
    [InlineData(4, 2, false)]  // shares the first day
    [InlineData(6, 1, false)]  // inside
    [InlineData(1, 20, false)] // around
    public async Task Rent_MaintenanceDays_AreRefusedWithTheSameAnswerAsATakenDay(int startOffset, int days, bool allowed)
    {
        await Maintenance.AddAsync(_admin, CarId, Input(5, 3), Ct);

        if (allowed)
        {
            await RentAsync(_db.Services, _customer, CarId, startOffset, days);
            Assert.Equal(1, await ActiveRentalsAsync());
        }
        else
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RentAsync(_db.Services, _customer, CarId, startOffset, days));
            Assert.Equal(RentalService.Overlaps, error.Message);
            Assert.Equal(0, await ActiveRentalsAsync());
        }
    }

    [Theory]
    [InlineData(2, 3, false)] // the same days
    [InlineData(4, 1, false)] // the last rented day
    [InlineData(5, 2, true)]  // from the return day on
    [InlineData(0, 2, true)]  // up to the pick-up day
    public async Task Add_OverActiveRentalDays_IsRefused(int startOffset, int days, bool allowed)
    {
        await RentAsync(_db.Services, _customer, CarId, startOffset: 2, days: 3);

        if (allowed)
        {
            await Maintenance.AddAsync(_admin, CarId, Input(startOffset, days), Ct);
            Assert.Single(await Maintenance.GetForCarAsync(_admin, CarId, Ct));
        }
        else
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.AddAsync(_admin, CarId, Input(startOffset, days), Ct));
            Assert.Equal(MaintenanceService.OverlapsRental, error.Message);
            Assert.Empty(await Maintenance.GetForCarAsync(_admin, CarId, Ct));
        }
    }

    [Fact]
    public async Task Add_OverACancelledRental_IsAllowed()
    {
        var receipt = await RentAsync(_db.Services, _customer, CarId, startOffset: 2, days: 3);
        await _db.Get<IRentalService>().EndAsync(_customer, receipt.RentalId, Ct);

        await Maintenance.AddAsync(_admin, CarId, Input(2, 3), Ct);
    }

    [Fact]
    public async Task Update_WithTheLoadedVersion_ChangesTheRecordAndRaisesTheVersion()
    {
        var id = await Maintenance.AddAsync(_admin, CarId, Input(5, 3), Ct);

        await Maintenance.UpdateAsync(_admin, id, 1, Input(6, 1, "Lastik değişimi", null, MaintenanceKind.Damage), Ct);

        var record = Assert.Single(await Maintenance.GetForCarAsync(_admin, CarId, Ct));
        Assert.Equal((MaintenanceKind.Damage, "Lastik değişimi", Today.AddDays(6), Today.AddDays(7), 0m, 2), (record.Kind, record.Description, record.StartDate, record.EndDate, record.Cost, record.Version));
        await RentAsync(_db.Services, _customer, CarId, startOffset: 4, days: 2);
    }

    /// <summary>Two admins opened the same record; the second save carries the old version and writes nothing.</summary>
    [Fact]
    public async Task Update_FromAStaleForm_IsRefusedAndKeepsTheOtherChange()
    {
        var id = await Maintenance.AddAsync(_admin, CarId, Input(5, 3, "İlk"), Ct);
        await Maintenance.UpdateAsync(_admin, id, 1, Input(5, 3, "Birinci yönetici"), Ct);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.UpdateAsync(_admin, id, 1, Input(9, 3, "İkinci yönetici"), Ct));
        Assert.Equal(MaintenanceService.ChangedElsewhere, error.Message);
        var cancel = await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.CancelAsync(_admin, id, 1, Ct));
        Assert.Equal(MaintenanceService.ChangedElsewhere, cancel.Message);

        var record = Assert.Single(await Maintenance.GetForCarAsync(_admin, CarId, Ct));
        Assert.Equal(("Birinci yönetici", Today.AddDays(5), 2, MaintenanceStatus.Active), (record.Description, record.StartDate, record.Version, record.Status));
    }

    [Fact]
    public async Task Update_OntoRentedDays_IsRefused()
    {
        var id = await Maintenance.AddAsync(_admin, CarId, Input(10, 2), Ct);
        await RentAsync(_db.Services, _customer, CarId, startOffset: 2, days: 3);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.UpdateAsync(_admin, id, 1, Input(3, 2), Ct));

        Assert.Equal(MaintenanceService.OverlapsRental, error.Message);
        Assert.Equal(Today.AddDays(10), Assert.Single(await Maintenance.GetForCarAsync(_admin, CarId, Ct)).StartDate);
    }

    [Fact]
    public async Task Cancel_FreesTheDaysAndKeepsTheRecord()
    {
        var id = await Maintenance.AddAsync(_admin, CarId, Input(5, 3), Ct);

        await Maintenance.CancelAsync(_admin, id, 1, Ct);
        await RentAsync(_db.Services, _customer, CarId, startOffset: 5, days: 3);

        var record = Assert.Single(await Maintenance.GetForCarAsync(_admin, CarId, Ct));
        Assert.Equal((MaintenanceStatus.Cancelled, 2), (record.Status, record.Version));
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.CancelAsync(_admin, id, 2, Ct));
        Assert.Equal(MaintenanceService.NotFound, again.Message);
        var change = await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.UpdateAsync(_admin, id, 2, Input(20, 1), Ct));
        Assert.Equal(MaintenanceService.NotFound, change.Message);
    }

    [Fact]
    public async Task MissingCarOrRecord_AreRefused()
    {
        Assert.Equal(MaintenanceService.CarNotFound, (await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.AddAsync(_admin, 999, Input(1, 1), Ct))).Message);
        Assert.Equal(MaintenanceService.NotFound, (await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.UpdateAsync(_admin, 999, 1, Input(1, 1), Ct))).Message);
        Assert.Equal(MaintenanceService.NotFound, (await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.CancelAsync(_admin, 999, 1, Ct))).Message);
    }

    [Fact]
    public async Task Customer_CanNeitherReadNorWriteTheLog()
    {
        var id = await Maintenance.AddAsync(_admin, CarId, Input(5, 3), Ct);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Maintenance.GetForCarAsync(_customer, CarId, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Maintenance.AddAsync(_customer, CarId, Input(1, 1), Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Maintenance.UpdateAsync(_customer, id, 1, Input(1, 1), Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Maintenance.CancelAsync(_customer, id, 1, Ct));

        Assert.Equal(1, Assert.Single(await Maintenance.GetForCarAsync(_admin, CarId, Ct)).Version);
    }

    [Fact]
    public async Task BookedRanges_IncludeMaintenanceDays()
    {
        await RentAsync(_db.Services, _customer, CarId, startOffset: 1, days: 2);
        await Maintenance.AddAsync(_admin, CarId, Input(5, 2), Ct);
        var cancelled = await Maintenance.AddAsync(_admin, CarId, Input(9, 2), Ct);
        await Maintenance.CancelAsync(_admin, cancelled, 1, Ct);

        var ranges = await _db.Get<IRentalService>().GetBookedRangesAsync(_customer, CarId, Ct);

        Assert.Equal([new BookedRange(Today.AddDays(1), Today.AddDays(3)), new BookedRange(Today.AddDays(5), Today.AddDays(7))], ranges);
    }

    [Fact]
    public async Task DeleteCar_WithMaintenanceHistoryOnly_IsRefused()
    {
        var id = await Maintenance.AddAsync(_admin, CarId, Input(5, 3), Ct);
        await Maintenance.CancelAsync(_admin, id, 1, Ct);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<ICarService>().DeleteAsync(_admin, CarId, Ct));

        Assert.Equal(CarService.HasRentals, error.Message);
    }

    [Theory]
    [InlineData(-3651, 1, MaintenanceFieldsValidator.DatesOutOfRange)]
    [InlineData(3650, 1, MaintenanceFieldsValidator.DatesOutOfRange)]
    [InlineData(5, 0, MaintenanceFieldsValidator.DatesInvalid)]
    [InlineData(5, -1, MaintenanceFieldsValidator.DatesInvalid)]
    public async Task Add_DaysOutsideTheLimits_AreRefused(int startOffset, int days, string message)
    {
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Maintenance.AddAsync(_admin, CarId, Input(startOffset, days), Ct));

        Assert.Equal(message, error.Message);
        Assert.Empty(await Maintenance.GetForCarAsync(_admin, CarId, Ct));
    }

    /// <summary>A past repair can be logged; the limits are ten years either side of the database's today.</summary>
    [Fact]
    public async Task Add_PastAndFarRanges_WithinTheLimits_AreStored()
    {
        await Maintenance.AddAsync(_admin, CarId, Input(-3650, 10), Ct);
        await Maintenance.AddAsync(_admin, CarId, Input(3649, 1), Ct);

        Assert.Equal(2, (await Maintenance.GetForCarAsync(_admin, CarId, Ct)).Count);
    }

    private static MaintenanceInput Input(int startOffset, int days, string description = "Bakım", string? cost = null, MaintenanceKind kind = MaintenanceKind.Maintenance) =>
        new(kind, description, Today.AddDays(startOffset), Today.AddDays(startOffset + days), cost);

    private async Task<int> ActiveRentalsAsync()
    {
        await using var db = _db.CreateContext();
        return await db.Rentals.CountAsync(r => r.CarId == CarId && r.Status == RentalStatus.Active, Ct);
    }
}
