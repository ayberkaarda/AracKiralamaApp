using System.Globalization;
using System.Reflection;
using AracKiralama.Business;
using AracKiralama.Business.Exports;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;

namespace AracKiralama.Screenshots;

/// <summary>
/// A sample rental fleet in memory, so the screenshots need no database and never show real data: common car
/// models shown with the application's bundled photographs (credited in docs/CREDITS.md), demonstration plates
/// with the province code 00 that no Turkish plate uses, sample accounts on the reserved example.com domain and
/// nothing loaded from the web. Every call completes synchronously.
/// </summary>
internal sealed class SyntheticFleet : IAuthService, ICarService, IRentalService, IContractService, IAvailabilityService, IMaintenanceService, IReportService, IAccountService, ISetupService
{
    /// <summary>The day the screens are drawn on, so the images do not change with the date of the run.</summary>
    public static readonly DateOnly SampleToday = new(2026, 3, 10);

    private readonly List<Car> _cars =
    [
        Car(1, "Renault", "Clio", 2023, "Otomatik", "Benzin", 18_400, "Beyaz", "00 RC 101", 1250m),
        Car(2, "Fiat", "Egea", 2022, "Manuel", "Dizel", 42_150, "Füme", "00 FE 214", 1100m),
        Car(3, "Toyota", "Corolla", 2024, "Otomatik", "Hibrit", 7_900, "Gümüş", "00 TC 330", 1890m),
        Car(4, "Dacia", "Duster", 2025, "Manuel", "Benzin", 23_020, "Gri", "00 DD 048", 950m),
        Car(5, "Tesla", "Model 3", 2024, "Otomatik", "Elektrik", 12_300, "Beyaz", "00 TM 512", 2150m),
        Car(6, "Volkswagen", "Golf", 2024, "Otomatik", "Benzin", 28_700, "Beyaz", "00 VG 766", 1350m, listed: false),
        Car(7, "Ford", "Transit Custom Uzun Şaseli Panelvan", 2022, "Manuel", "Dizel", 51_000, "Mavi", "00 FT 902", 1650m),
    ];

    private readonly List<UserSummary> _accounts =
    [
        new(1, "yonetici", null, null, UserRole.Admin, 3),
        new(2, "ornek.musteri", "ornek.musteri@example.com", "5550001021", UserRole.User, 1),
        new(3, "deniz.yolcu", "deniz.yolcu@example.com", "5550001034", UserRole.User, 1),
        new(4, "ada.sürücü.uzun.kullanici.adi", "ada.uzun.adres.ornegi@example.com", null, UserRole.User, 2),
        new(5, "filo.yonetimi", "filo@example.com", "5550001099", UserRole.Admin, 1),
        new(6, "eski.hesap", null, null, UserRole.User, 1),
    ];

    public RentalPolicy Policy { get; } = new() { LongRentalDays = 7, LongRentalDiscountPercent = 10 };

    public UserSession Customer { get; } = Session(2, "ornek.musteri", UserRole.User);

    public UserSession Admin { get; } = Session(1, "yonetici", UserRole.Admin);

    public IReadOnlyList<Car> Cars => _cars;

    // Sign-in, sign-up and setup are only drawn, never submitted.
    public Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default) => Unused<UserSession>();

    public Task RegisterAsync(string? userName, string? password, string? email, string? phone, CancellationToken cancellationToken = default) => Unused<bool>();

    public Task ChangePasswordAsync(UserSession session, string? currentPassword, string? newPassword, string? newPasswordRepeat, CancellationToken cancellationToken = default) => Unused<bool>();

    public Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public SetupKeyPrefill GetEnvironmentKey() => new(null, false);

    public Task<UserSession> CreateFirstAdminAsync(string? setupKey, string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default) => Unused<UserSession>();

    public Task<IReadOnlyList<Car>> GetCatalogAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Car>>([.. _cars.Where(c => c.IsListed && (filter.Brand is null || c.Brand == filter.Brand))]);

    public Task<CarFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CarFilterOptions(
            [.. _cars.Select(c => c.Brand).Distinct().Order()],
            [.. _cars.Select(c => c.Transmission).Distinct().Order()],
            [.. _cars.Select(c => c.FuelType).Distinct().Order()]));

    public Task<Car> GetAsync(UserSession session, int carId, CancellationToken cancellationToken = default) => Task.FromResult(_cars.Single(c => c.Id == carId));

    public Task<IReadOnlyList<Car>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Car>>(_cars);

    public Task<int> AddAsync(UserSession session, CarInput input, CancellationToken cancellationToken = default) => Unused<int>();

    public Task UpdateAsync(UserSession session, int carId, int expectedVersion, CarInput input, CancellationToken cancellationToken = default) => Unused<bool>();

    public Task DeleteAsync(UserSession session, int carId, CancellationToken cancellationToken = default) => Unused<bool>();

    public Task<DateOnly> GetTodayAsync(UserSession session, CancellationToken cancellationToken = default) => Task.FromResult(SampleToday);

    public RentalQuote Quote(int carId, decimal dailyPrice, DateOnly startDate, int days, DateOnly today)
    {
        var total = days * dailyPrice;
        var discounted = days >= Policy.LongRentalDays && Policy.LongRentalDays > 0;
        if (discounted)
        {
            total = Math.Round(total * (100 - Policy.LongRentalDiscountPercent) / 100m, 2);
        }

        return new RentalQuote(carId, days, dailyPrice, total, startDate, startDate.AddDays(days), discounted);
    }

    public Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default) => Unused<RentalReceipt>();

    public Task<IReadOnlyList<BookedRange>> GetBookedRangesAsync(UserSession session, int carId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BookedRange>>([new(Day(14), Day(17)), new(Day(23), Day(26))]);

    public Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>([.. Rentals().Where(r => r.UserId == session.UserId)]);

    public Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>([.. Rentals()]);

    public bool IsUnderway(RentalDetails rental)
    {
        ArgumentNullException.ThrowIfNull(rental);
        return rental.StartDate <= SampleToday;
    }

    public Task<RentalEnding> EndAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default) => Unused<RentalEnding>();

    public Task<ExportFile> CreateAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default) => Unused<ExportFile>();

    public Task<CarCalendar> GetCalendarAsync(UserSession session, int carId, DateOnly? month, CancellationToken cancellationToken = default)
    {
        var first = new DateOnly(SampleToday.Year, SampleToday.Month, 1);
        var days = Enumerable.Range(0, DateTime.DaysInMonth(first.Year, first.Month)).Select(i =>
        {
            var date = first.AddDays(i);
            var state = date < SampleToday ? DayState.Past
                : date.Day is >= 14 and < 17 or >= 23 and < 26 ? DayState.Booked
                : date.Day is >= 18 and < 20 ? DayState.Maintenance
                : DayState.Free;
            return new CalendarDay(date, state);
        }).ToList();
        return Task.FromResult(new CarCalendar(carId, first, SampleToday, days));
    }

    public Task<IReadOnlyList<MaintenanceRecord>> GetForCarAsync(UserSession session, int carId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MaintenanceRecord>>(
        [
            Record(11, MaintenanceKind.Maintenance, "Periyodik bakım: yağ, filtreler ve fren kontrolü", Day(18), Day(20), 2400m, MaintenanceStatus.Active),
            Record(10, MaintenanceKind.Damage, "Arka tampon çizik onarımı ve boya", new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 5), 3150m, MaintenanceStatus.Active),
            Record(9, MaintenanceKind.Maintenance, "Kış lastiklerinin takılması", new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 2), 0m, MaintenanceStatus.Cancelled),
        ]);

    public Task<int> AddAsync(UserSession session, int carId, MaintenanceInput input, CancellationToken cancellationToken = default) => Unused<int>();

    public Task UpdateAsync(UserSession session, int recordId, int expectedVersion, MaintenanceInput input, CancellationToken cancellationToken = default) => Unused<bool>();

    public Task CancelAsync(UserSession session, int recordId, int expectedVersion, CancellationToken cancellationToken = default) => Unused<bool>();

    public Task<ReportRange> GetDefaultRangeAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ReportRange(new DateOnly(2026, 1, 1), SampleToday));

    public Task<RevenueReport> GetRevenueAsync(UserSession session, DateOnly firstDay, DateOnly lastDay, RevenueGrouping grouping, CancellationToken cancellationToken = default)
    {
        RevenueLine[] periods =
        [
            new("Ocak 2026", null, 14, 52, 71_480m),
            new("Şubat 2026", null, 11, 38, 54_120m),
            new("Mart 2026", null, 5, 17, 23_910m),
        ];
        RevenueLine[] cars =
        [
            new("Toyota Corolla", "00 TC 330", 8, 26, 49_140m),
            new("Tesla Model 3", "00 TM 512", 6, 19, 40_850m),
            new("Renault Clio", "00 RC 101", 7, 24, 30_000m),
            new("Fiat Egea", "00 FE 214", 6, 22, 24_200m),
            new("Dacia Duster", "00 DD 048", 3, 16, 5_320m),
        ];
        var total = new RevenueLine("Toplam", null, periods.Sum(p => p.Rentals), periods.Sum(p => p.Days), periods.Sum(p => p.Revenue));
        return Task.FromResult(new RevenueReport(firstDay, lastDay, grouping, periods, cars, total, new DateTime(2026, 3, 10, 9, 0, 0), SampleToday));
    }

    public ExportFile ToCsv(RevenueReport report) => new("gelir-raporu.csv", []);

    Task<IReadOnlyList<UserSummary>> IAccountService.GetAllAsync(UserSession session, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(_accounts);

    public Task<int> AddAsync(UserSession session, AccountInput input, CancellationToken cancellationToken = default) => Unused<int>();

    public Task<bool> UpdateAsync(UserSession session, int userId, int expectedVersion, AccountInput input, CancellationToken cancellationToken = default) => Unused<bool>();

    Task IAccountService.DeleteAsync(UserSession session, int userId, CancellationToken cancellationToken) => Unused<bool>();

    private IEnumerable<RentalDetails> Rentals()
    {
        RentalDetails Rental(int id, int carId, int userId, string userName, DateOnly start, int days)
        {
            var car = _cars.Single(c => c.Id == carId);
            return new RentalDetails(id, carId, car.Brand, car.Model, car.Transmission, car.FuelType, car.Plate, null, userId, userName, start, start.AddDays(days), Quote(carId, car.DailyPrice, start, days, SampleToday).Total, RentalStatus.Active);
        }

        yield return Rental(41, 1, 2, "ornek.musteri", Day(8), 4);
        yield return Rental(42, 3, 2, "ornek.musteri", Day(14), 3);
        yield return Rental(43, 2, 3, "deniz.yolcu", Day(9), 7);
        yield return Rental(44, 5, 4, "ada.sürücü.uzun.kullanici.adi", Day(23), 3);
        yield return Rental(45, 7, 3, "deniz.yolcu", Day(12), 2);
    }

    private static DateOnly Day(int day) => new(SampleToday.Year, SampleToday.Month, day);

    private static Car Car(int id, string brand, string model, int year, string transmission, string fuel, int mileage, string color, string plate, decimal price, bool listed = true) => new()
    {
        Id = id,
        Brand = brand,
        Model = model,
        Year = year,
        Transmission = transmission,
        FuelType = fuel,
        Mileage = mileage,
        Color = color,
        Plate = plate,
        DailyPrice = price,
        IsListed = listed,
        Version = 1,
    };

    private static MaintenanceRecord Record(int id, MaintenanceKind kind, string description, DateOnly start, DateOnly end, decimal cost, MaintenanceStatus status) => new()
    {
        Id = id,
        CarId = 3,
        Kind = kind,
        Description = description,
        StartDate = start,
        EndDate = end,
        Cost = cost,
        Status = status,
        Version = 1,
    };

    /// <summary>Sessions are made by the sign-in only (the constructor is internal); the tool makes its samples by reflection.</summary>
    private static UserSession Session(int id, string name, UserRole role) =>
        (UserSession)Activator.CreateInstance(
            typeof(UserSession),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [id, name, role, 0],
            culture: CultureInfo.InvariantCulture)!;

    private static Task<T> Unused<T>() => Task.FromException<T>(new BusinessRuleException("Ekran görüntüsü aracında yazma yapılmaz."));
}
