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
        using var form = new RentalForm(cars, rentals, new NoFormsFactory(), Sessions.User, 1) { Prompts = prompts };
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
        using var form = new RentalForm(new ControlledCarService(), rentals, new NoFormsFactory(), Sessions.User, 1) { Prompts = new RecordingPrompts() };
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
        using var form = new ProfileForm(rentals, new ControlledContractService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
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
        using var form = new RentedCarsPanel(rentals, new ControlledCarService(), new ControlledContractService(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);

        _ = form.EndAsync(form.ShownRentals[0]);
        var second = form.EndAsync(form.ShownRentals[1]);

        Assert.False(second.Result);
        Assert.Equal(1, rentals.EndCalls);
    });

    /// <summary>
    /// A rental ended elsewhere meanwhile: the refusal is shown and the list is loaded again, so the
    /// ended rental and its button do not stay on the screen.
    /// </summary>
    [Fact]
    public void EndRental_AlreadyEnded_RefreshesTheList() => Run(() =>
    {
        const string NotFound = "Kiralama bulunamadı veya zaten sona ermiş.";
        var shownBefore = NoDialogs.Requested.Count(r => r.Contains(NotFound, StringComparison.Ordinal));
        var rentals = new ControlledRentalService();
        using var profile = new ProfileForm(rentals, new ControlledContractService(), new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        using var panel = new RentedCarsPanel(rentals, new ControlledCarService(), new ControlledContractService(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(profile.LoadRentalsAsync().IsCompletedSuccessfully);
        Assert.True(panel.LoadRentalsAsync().IsCompletedSuccessfully);
        rentals.Rentals.RemoveAt(0);

        var fromProfile = profile.EndAsync(profile.ShownRentals[0]);
        rentals.RefuseEnd(NotFound);
        var fromPanel = panel.EndAsync(panel.ShownRentals[0]);
        rentals.RefuseEnd(NotFound);

        Assert.False(fromProfile.Result);
        Assert.False(fromPanel.Result);
        Assert.Equal([10], profile.ShownRentals.Select(r => r.Id));
        Assert.Equal([10], panel.ShownRentals.Select(r => r.Id));
        Assert.True(Assert.Single(profile.EndButtons).Enabled);
        Assert.Equal(shownBefore + 2, NoDialogs.Requested.Count(r => r.Contains(NotFound, StringComparison.Ordinal)));
    });

    /// <summary>A booking refused because the days were taken meanwhile shows the taken days as they are now.</summary>
    [Fact]
    public void Rent_RefusedAsOverlapping_ShowsTheTakenDaysAgain() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        using var form = new RentalForm(new ControlledCarService(), rentals, new NoFormsFactory(), Sessions.User, 1) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadCarAsync().IsCompletedSuccessfully);
        rentals.Booked.Add(new(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 12)));

        var rent = form.RentAsync();
        rentals.RefuseRent("Araç seçtiğiniz tarihlerin bir kısmında başka bir kiralamada. Lütfen başka tarihler seçin.");

        Assert.False(rent.Result);
        Assert.Equal(2, form.Booked.Count);
        Assert.Contains("10.03.2026 - 12.03.2026", Find<Label>(form, "lblBooked").Text, StringComparison.Ordinal);
        Assert.True(Find<Button>(form, "btnConfirm").Enabled);
    });

    /// <summary>A car deleted elsewhere meanwhile: after the refusal the list is loaded again and the form no longer holds the car.</summary>
    [Fact]
    public void UpdateCar_DeletedMeanwhile_RefreshesTheListAndClearsTheForm() => Run(() =>
    {
        var cars = new ControlledCarService { UpdateFailure = BusinessRuleException.Stale("Araç bulunamadı.") };
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadCarsAsync().IsCompletedSuccessfully);
        form.SelectCar(cars.Cars[0]);
        cars.Cars.RemoveAt(0);

        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);

        Assert.Equal([2], form.ShownCars.Select(c => c.Id));
        Assert.Null(form.SelectedCarId);
        Assert.Empty(Find<TextBox>(form, "txtBrand").Text);
        Assert.False(Find<Button>(form, "btnUpdateCar").Enabled);
    });

    /// <summary>
    /// Another admin raised the price while this form was open: the save carries the loaded version, is
    /// refused as stale, and the form then shows the car with the other admin's price and version.
    /// </summary>
    [Fact]
    public void UpdateCar_ChangedElsewhere_ReloadsTheCarWithItsNewVersion() => Run(() =>
    {
        var cars = new ControlledCarService();
        cars.Cars[0].Version = 1;
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadCarsAsync().IsCompletedSuccessfully);
        form.SelectCar(cars.Cars[0]);
        var changed = ControlledCarService.Car(1, "Anka", "Kent");
        changed.DailyPrice = 1200m;
        changed.Version = 2;
        cars.Cars[0] = changed;
        cars.UpdateFailure = BusinessRuleException.Stale("Araç siz düzenlerken başka biri tarafından değiştirildi.");
        Find<TextBox>(form, "txtColor").Text = "Kırmızı";

        Assert.True(form.UpdateAsync().IsCompletedSuccessfully);

        Assert.Equal((1, 1), cars.LastVersion);
        Assert.Equal(1, form.SelectedCarId);
        Assert.Equal(2, form.SelectedCarVersion);
        Assert.Equal(DisplayFormat.MoneyInput(1200m), Find<TextBox>(form, "txtDailyPrice").Text);
        Assert.Equal("Beyaz", Find<TextBox>(form, "txtColor").Text);
        Assert.True(Find<Button>(form, "btnUpdateCar").Enabled);
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
