using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Tests.Support;
using static AracKiralama.UI.Tests.Support.StaThread;

namespace AracKiralama.UI.Tests;

/// <summary>Every write button runs through an exclusive operation: a second click while the first write waits is ignored.</summary>
public sealed class ExclusiveWriteTests
{
    [Fact]
    public void ExclusiveOperation_Failure_RestoresControlsAndAllowsTheNextRun() => Run(() =>
    {
        var operation = new ExclusiveOperation();
        using var enabled = new Button();
        using var disabled = new Button { Enabled = false };
        var pending = new TaskCompletionSource();

        var run = operation.RunAsync(() => pending.Task, enabled, disabled);
        Assert.True(operation.IsRunning);
        Assert.False(enabled.Enabled);
        Assert.False(operation.RunAsync(() => Task.CompletedTask).Result);

        pending.SetException(new InvalidOperationException("boom"));

        Assert.IsType<InvalidOperationException>(run.Exception?.InnerException);
        Assert.False(operation.IsRunning);
        Assert.True(enabled.Enabled);
        Assert.False(disabled.Enabled);
        Assert.True(operation.RunAsync(() => Task.CompletedTask).Result);
    });

    [Fact]
    public void SignIn_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new LoginForm(auth, new NoFormsFactory());
        Find<Control>(form, "txtUserName").Text = "ornek";
        Find<Control>(form, "txtPassword").Text = "Gizli.Sifre1";

        var first = form.SignInAsync();
        var second = form.SignInAsync();

        Assert.True(second.IsCompletedSuccessfully);
        Assert.Null(second.Result);
        Assert.Equal(1, auth.SignInCalls);
        Assert.False(Find<Control>(form, "btnSignIn").Enabled);
        Assert.False(Find<Control>(form, "btnRegister").Enabled);

        auth.CompleteSignIn(Sessions.User);
        Assert.Same(Sessions.User, first.Result);
        Assert.True(Find<Control>(form, "btnSignIn").Enabled);
    });

    [Fact]
    public void Register_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new RegisterForm(auth) { Prompts = new RecordingPrompts() };

        var first = form.RegisterAsync();
        var second = form.RegisterAsync();

        Assert.False(second.Result);
        Assert.Equal(1, auth.RegisterCalls);
        auth.CompleteRegister();
        Assert.True(first.Result);
    });

    [Fact]
    public void AddCar_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var cars = new ControlledCarService();
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };

        var first = form.AddAsync();
        var second = form.AddAsync();

        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(1, cars.AddCalls);
        Assert.False(Find<Button>(form, "btnAddCar").Enabled);
        Assert.False(Find<Button>(form, "btnDeleteCar").Enabled);

        cars.CompleteAdd();
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(Find<Button>(form, "btnAddCar").Enabled);
    });

    [Fact]
    public void Rent_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var cars = new ControlledCarService();
        var rentals = new ControlledRentalService();
        var prompts = new RecordingPrompts();
        using var form = new RentalForm(cars, rentals, Sessions.User, 1) { Prompts = prompts };
        Assert.True(form.LoadCarAsync().IsCompletedSuccessfully);
        Find<TextBox>(form, "txtDays").Text = "3";
        Find<DateTimePicker>(form, "dtpStart").Value = new DateTime(2026, 3, 10);

        var first = form.RentAsync();
        var second = form.RentAsync();

        Assert.False(second.Result);
        Assert.Equal(1, rentals.RentCalls);
        Assert.Equal(3, rentals.LastQuote!.Days);
        Assert.Equal(new DateOnly(2026, 3, 10), rentals.LastQuote.StartDate);
        Assert.Equal(850.5m, rentals.LastQuote.DailyPrice);
        Assert.Contains("Toplam Tutar: 2551,50 TL", prompts.Messages[0], StringComparison.Ordinal);
        Assert.Contains("13.03.2026", prompts.Messages[0], StringComparison.Ordinal);
        Assert.False(Find<DateTimePicker>(form, "dtpStart").Enabled);

        rentals.CompleteRent();
        Assert.True(first.Result);
    });

    [Fact]
    public void Rent_ShowsTheTakenDaysAndCapsTheDaysToTheSettings() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        using var form = new RentalForm(new ControlledCarService(), rentals, Sessions.User, 1) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadCarAsync().IsCompletedSuccessfully);
        Find<TextBox>(form, "txtDays").Text = "999";

        _ = form.RentAsync();

        Assert.Equal(rentals.Policy.MaxRentalDays, rentals.LastQuote!.Days);
        Assert.Contains("05.03.2026 - 08.03.2026", Find<Label>(form, "lblBooked").Text, StringComparison.Ordinal);
        Assert.Equal(new DateTime(2026, 3, 2), Find<DateTimePicker>(form, "dtpStart").MinDate);
    });

    [Fact]
    public void EndRental_EveryEndButtonStaysDisabledUntilTheEndFinishes() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        using var form = new ProfileForm(rentals, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);
        Assert.Equal(2, form.EndButtons.Count);
        Assert.All(form.EndButtons, b => Assert.Equal("Kiralamayı İptal Et", b.Text));

        var first = form.EndAsync(form.ShownRentals[0]);
        var second = form.EndAsync(form.ShownRentals[1]);

        Assert.False(second.Result);
        Assert.Equal(1, rentals.EndCalls);
        Assert.All(form.EndButtons, b => Assert.False(b.Enabled));

        rentals.Rentals.RemoveAt(0);
        rentals.CompleteEnd();
        Assert.True(first.Result);
        Assert.Single(form.EndButtons);
        Assert.True(form.EndButtons[0].Enabled);
    });

    [Fact]
    public void AdminEndRental_SecondClickWhilePending_IsIgnored() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        using var form = new RentedCarsPanel(rentals, new ControlledCarService(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);

        _ = form.EndAsync(form.ShownRentals[0]);
        var second = form.EndAsync(form.ShownRentals[1]);

        Assert.False(second.Result);
        Assert.Equal(1, rentals.EndCalls);
    });

    [Fact]
    public void AddCar_InvalidFields_AreMarkedNextToTheirControls() => Run(() =>
    {
        var cars = new ControlledCarService
        {
            AddFailure = new BusinessRuleException(
                "Lütfen gerekli alanları doldurun.",
                new Dictionary<string, string> { [nameof(CarInput.Brand)] = "Marka boş bırakılamaz.", [nameof(CarInput.Plate)] = "Plaka boş bırakılamaz." }),
        };
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        var errors = (ErrorProvider)typeof(AdminPanel).GetField("errorProvider1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(form)!;

        Assert.True(form.AddAsync().IsCompletedSuccessfully);

        Assert.Equal("Marka boş bırakılamaz.", errors.GetError(Find<TextBox>(form, "txtBrand")));
        Assert.Equal("Plaka boş bırakılamaz.", errors.GetError(Find<TextBox>(form, "txtPlate")));
        Assert.Empty(errors.GetError(Find<TextBox>(form, "txtModel")));
        Assert.Contains(NoDialogs.Requested, r => r.Contains("Lütfen gerekli alanları doldurun.", StringComparison.Ordinal));
    });
}
