using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Data.Persistence;
using AracKiralama.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static AracKiralama.Data.Tests.TestServices;

namespace AracKiralama.Data.Tests;

/// <summary>
/// Date decisions on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>) when the
/// database's clock passes midnight while a request waits for the car's lock: the request is sent a few
/// minutes before midnight, another transaction holds the car's lock, the clock is moved past midnight
/// and only then is the lock released. Every decision that depends on today's date is made with the
/// date after midnight, read inside the write after the lock was granted.
/// </summary>
public sealed class SqlServerMidnightTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A rental that starts tomorrow is ended just before midnight. While the request waits for the car's
    /// lock, midnight passes: the rental has started, so it is returned today, not cancelled.
    /// </summary>
    [Fact]
    public async Task End_MidnightPassesWhileWaitingForTheCarLock_ReturnsTheStartedRental()
    {
        await using var midnight = await Midnight.StartAsync();
        var app = midnight.App;
        var admin = await SetUpAdminAsync(app);
        var customer = await RegisterAsync(app, "musteri", 1);
        var carId = await AddCarAsync(app, admin, "00 GYR 01");
        var receipt = await RentAsync(app, customer, carId, startOffset: 1, days: 3);
        Assert.Equal(midnight.Day.AddDays(1), receipt.StartDate);

        await using (var held = await CarLock.TakeAsync(midnight.Database, carId))
        {
            var ending = Task.Run(() => app.GetRequiredService<IRentalService>().EndAsync(customer, receipt.RentalId, Ct), Ct);
            await midnight.Database.WaitForBlockedRequestsAsync(1, Ct);
            midnight.PassMidnight();
            await held.ReleaseAsync();

            Assert.Equal(RentalEnding.Returned, await ending);
        }

        Assert.Equal(1, await midnight.Database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kiralama WHERE KiralamaID = @id AND Durum = 1 AND GercekIadeTarihi = @day",
            Ct,
            ("@id", receipt.RentalId),
            ("@day", midnight.Day.AddDays(1).ToDateTime(TimeOnly.MinValue))));
    }

    /// <summary>
    /// A booking from today is sent just before midnight. While it waits for the car's lock, midnight
    /// passes: its first day is yesterday now, so it is refused and nothing is stored.
    /// </summary>
    [Fact]
    public async Task Rent_MidnightPassesWhileWaitingForTheCarLock_RefusesAStartThatIsPastNow()
    {
        await using var midnight = await Midnight.StartAsync();
        var app = midnight.App;
        var admin = await SetUpAdminAsync(app);
        var customer = await RegisterAsync(app, "musteri", 1);
        var carId = await AddCarAsync(app, admin, "00 GYR 02");
        var rentals = app.GetRequiredService<IRentalService>();
        var quote = rentals.Quote(carId, 100m, midnight.Day, 2, midnight.Day);

        await using (var held = await CarLock.TakeAsync(midnight.Database, carId))
        {
            var booking = Task.Run(() => rentals.RentAsync(customer, quote, Ct), Ct);
            await midnight.Database.WaitForBlockedRequestsAsync(1, Ct);
            midnight.PassMidnight();
            await held.ReleaseAsync();

            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => booking);
            Assert.Equal(RentalService.StartInPast, error.Message);
        }

        Assert.Equal(0, await midnight.Database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kiralama", Ct));
    }

    /// <summary>
    /// A maintenance record that starts on the earliest day allowed today (ten years back) is sent just
    /// before midnight. While it waits for the car's lock, midnight passes: the day is now out of range, so
    /// the record is refused and nothing is stored.
    /// </summary>
    [Fact]
    public async Task AddMaintenance_MidnightPassesWhileWaitingForTheCarLock_RefusesADayOutOfRangeNow()
    {
        await using var midnight = await Midnight.StartAsync();
        var app = midnight.App;
        var admin = await SetUpAdminAsync(app);
        var carId = await AddCarAsync(app, admin, "00 GYR 03");
        var earliest = midnight.Day.AddDays(-RentalPolicy.MaxAllowedDays);
        var input = new MaintenanceInput(MaintenanceKind.Maintenance, "Periyodik bakım", earliest, earliest.AddDays(1), null);

        await using (var held = await CarLock.TakeAsync(midnight.Database, carId))
        {
            var adding = Task.Run(() => app.GetRequiredService<IMaintenanceService>().AddAsync(admin, carId, input, Ct), Ct);
            await midnight.Database.WaitForBlockedRequestsAsync(1, Ct);
            midnight.PassMidnight();
            await held.ReleaseAsync();

            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => adding);
            Assert.Equal(MaintenanceFieldsValidator.DatesOutOfRange, error.Message);
        }

        Assert.Equal(0, await midnight.Database.ScalarAsync("SELECT COUNT(*) FROM dbo.AracBakim", Ct));
    }

    /// <summary>
    /// Five minutes before midnight (by the database's clock), with the car's lock held by another
    /// transaction and an instance whose services can be given a fresh database clock.
    /// </summary>
    private sealed class Midnight : IAsyncDisposable
    {
        private readonly DatabaseClockShift _shift;
        private readonly FakeTimeProvider _clock;

        private Midnight(SqlServerTestDatabase database, DatabaseClockShift shift, FakeTimeProvider clock, ServiceProvider app, DateOnly day)
        {
            Database = database;
            _shift = shift;
            _clock = clock;
            App = app;
            Day = day;
        }

        public SqlServerTestDatabase Database { get; }

        /// <summary>The application instance whose database clock is moved.</summary>
        public ServiceProvider App { get; }

        /// <summary>The day before midnight (by the shifted database clock; the time zone is UTC).</summary>
        public DateOnly Day { get; }

        public static async Task<Midnight> StartAsync()
        {
            var database = await SqlServerTestDatabase.ReserveAsync(Ct);
            var serverNow = await database.ServerUtcNowAsync(Ct);
            var day = DateOnly.FromDateTime(serverNow.UtcDateTime);
            var beforeMidnight = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) - TimeSpan.FromMinutes(5);
            var shift = new DatabaseClockShift { Offset = beforeMidnight - serverNow };
            var clock = new FakeTimeProvider(beforeMidnight);
            var app = database.BuildServices(configureContext: o => o.AddInterceptors(shift), clock: clock);
            await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
            return new Midnight(database, shift, clock, app, day);
        }

        /// <summary>Moves the database's clock (and the computer's) ten minutes on, past midnight.</summary>
        public void PassMidnight()
        {
            _shift.Offset += TimeSpan.FromMinutes(10);
            _clock.Advance(TimeSpan.FromMinutes(10));
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    /// <summary>The car's application lock, held by a transaction of its own until it is released.</summary>
    private sealed class CarLock : IAsyncDisposable
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private bool _released;

        private CarLock(SqlConnection connection, SqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public static async Task<CarLock> TakeAsync(SqlServerTestDatabase database, int carId)
        {
            var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync(Ct);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Ct);
            await using var take = new SqlCommand("EXEC sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';", connection, transaction);
            take.Parameters.AddWithValue("@resource", "AracKiralama.Car." + carId.ToString(CultureInfo.InvariantCulture));
            await take.ExecuteNonQueryAsync(Ct);
            return new CarLock(connection, transaction);
        }

        public async Task ReleaseAsync()
        {
            if (!_released)
            {
                _released = true;
                await _transaction.CommitAsync(Ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
