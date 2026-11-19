using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI.Tests.Support;

/// <summary>Sign-in, sign-up and password calls that stay pending until the test completes them.</summary>
internal sealed class PendingAuthService : IAuthService
{
    private TaskCompletionSource<UserSession> _signIn = new();
    private TaskCompletionSource _register = new();

    public int SignInCalls { get; private set; }

    public int RegisterCalls { get; private set; }

    public string? LastPassword { get; private set; }

    public Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        SignInCalls++;
        LastPassword = password;
        _signIn = new TaskCompletionSource<UserSession>();
        return _signIn.Task;
    }

    public Task RegisterAsync(string? userName, string? password, string? email, string? phone, CancellationToken cancellationToken = default)
    {
        RegisterCalls++;
        _register = new TaskCompletionSource();
        return _register.Task;
    }

    public Task ChangePasswordAsync(UserSession session, string? currentPassword, string? newPassword, string? newPasswordRepeat, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void RefuseSignIn() => _signIn.SetException(new BusinessRuleException("Kullanıcı adı veya şifre hatalı."));

    public void CompleteSignIn(UserSession session) => _signIn.SetResult(session);

    public void CompleteRegister() => _register.SetResult();
}

/// <summary>Car calls answered by the test: catalog requests in any order, writes when it says so.</summary>
internal sealed class ControlledCarService : ICarService
{
    private TaskCompletionSource<int> _add = new();

    public List<(CarFilter Filter, TaskCompletionSource<IReadOnlyList<Car>> Answer)> CatalogRequests { get; } = [];

    public List<Car> Cars { get; } = [Car(1, "Anka", "Kent"), Car(2, "Kartal", "Kasa")];

    public CarFilterOptions Options { get; set; } = new(["Anka", "Kartal"], ["Manuel", "Otomatik"], ["Benzin", "Dizel"]);

    public int AddCalls { get; private set; }

    /// <summary>Thrown by the next add instead of keeping it pending.</summary>
    public Exception? AddFailure { get; set; }

    public static Car Car(int id, string brand, string model) => new()
    {
        Id = id,
        Brand = brand,
        Model = model,
        Year = 2021,
        Transmission = "Otomatik",
        FuelType = "Benzin",
        Mileage = 1000,
        Color = "Beyaz",
        Plate = "00 DMO 00" + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DailyPrice = 850.5m,
        IsListed = true,
    };

    public Task<IReadOnlyList<Car>> GetCatalogAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default)
    {
        var answer = new TaskCompletionSource<IReadOnlyList<Car>>();
        CatalogRequests.Add((filter, answer));
        return answer.Task;
    }

    public Task<CarFilterOptions> GetFilterOptionsAsync(UserSession session, CancellationToken cancellationToken = default) => Task.FromResult(Options);

    public Task<Car> GetAsync(UserSession session, int carId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Cars.Single(c => c.Id == carId));

    public Task<IReadOnlyList<Car>> GetAllAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Car>>(Cars);

    public Task<int> AddAsync(UserSession session, CarInput input, CancellationToken cancellationToken = default)
    {
        AddCalls++;
        if (AddFailure is { } failure)
        {
            return Task.FromException<int>(failure);
        }

        _add = new TaskCompletionSource<int>();
        return _add.Task;
    }

    /// <summary>The input of the last change, as the form sent it.</summary>
    public CarInput? LastUpdate { get; private set; }

    /// <summary>Thrown by changes instead of completing them.</summary>
    public Exception? UpdateFailure { get; set; }

    public Task UpdateAsync(UserSession session, int carId, CarInput input, CancellationToken cancellationToken = default)
    {
        LastUpdate = input;
        return UpdateFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    public Task DeleteAsync(UserSession session, int carId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void CompleteAdd() => _add.SetResult(3);
}

/// <summary>Rental calls; bookings and ends stay pending until the test completes them.</summary>
internal sealed class ControlledRentalService : IRentalService
{
    private TaskCompletionSource<RentalReceipt> _rent = new();
    private TaskCompletionSource<RentalEnding> _end = new();

    public RentalPolicy Policy { get; } = new() { MinRentalDays = 1, MaxRentalDays = 30, MaxAdvanceDays = 60 };

    public DateOnly Today { get; } = new(2026, 3, 2);

    public List<RentalDetails> Rentals { get; } = [Rental(9), Rental(10)];

    public List<BookedRange> Booked { get; } = [new(new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 8))];

    public int RentCalls { get; private set; }

    public int EndCalls { get; private set; }

    public RentalQuote? LastQuote { get; private set; }

    public static RentalDetails Rental(int id) =>
        new(id, 1, "Anka", "Kent", "Otomatik", "Benzin", "00 DMO 001", null, 2, "ornek", new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 7), 2551.5m, RentalStatus.Active);

    public RentalQuote Quote(int carId, decimal dailyPrice, DateOnly startDate, int days) =>
        new(carId, days, dailyPrice, dailyPrice * days, startDate, startDate.AddDays(days), false);

    public Task<RentalReceipt> RentAsync(UserSession session, RentalQuote quote, CancellationToken cancellationToken = default)
    {
        RentCalls++;
        LastQuote = quote;
        _rent = new TaskCompletionSource<RentalReceipt>();
        return _rent.Task;
    }

    public Task<IReadOnlyList<BookedRange>> GetBookedRangesAsync(UserSession session, int carId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BookedRange>>([.. Booked]);

    public Task<IReadOnlyList<RentalDetails>> GetMyActiveRentalsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>([.. Rentals]);

    public Task<IReadOnlyList<RentalDetails>> GetActiveRentalsAsync(UserSession session, CarFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RentalDetails>>([.. Rentals]);

    public bool IsUnderway(RentalDetails rental) => Today >= rental.StartDate;

    public Task<RentalEnding> EndAsync(UserSession session, int rentalId, CancellationToken cancellationToken = default)
    {
        EndCalls++;
        _end = new TaskCompletionSource<RentalEnding>();
        return _end.Task;
    }

    public void CompleteRent() => _rent.SetResult(new RentalReceipt(11, LastQuote!.Total, LastQuote.StartDate, LastQuote.EndDate));

    public void RefuseRent(string message) => _rent.SetException(new BusinessRuleException(message));

    public void CompleteEnd() => _end.SetResult(RentalEnding.Cancelled);

    public void RefuseEnd(string message) => _end.SetException(new BusinessRuleException(message));
}

/// <summary>Answers confirmations with yes and records every message instead of showing a dialog.</summary>
internal sealed class RecordingPrompts : IUserPrompts
{
    public List<string> Messages { get; } = [];

    public bool Confirm(string question)
    {
        Messages.Add(question);
        return true;
    }

    public void Inform(string message) => Messages.Add(message);

    public void Warn(string message) => Messages.Add(message);
}

internal sealed class NoFormsFactory : IFormFactory
{
    public T Create<T>(params object[] arguments)
        where T : Form => throw new InvalidOperationException("Tests do not open other forms.");
}

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User);

    public static UserSession Admin { get; } = new(1, "yonetici", UserRole.Admin);
}
