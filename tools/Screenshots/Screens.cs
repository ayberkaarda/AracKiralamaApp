using System.Globalization;
using AracKiralama.UI;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.Screenshots;

/// <summary>The screens shown in the README, each prepared with the sample fleet as a customer or an admin would see it.</summary>
internal static class Screens
{
    /// <summary>File name (kebab-case, without extension) and how to build the screen.</summary>
    public static IReadOnlyList<(string Name, Func<SyntheticFleet, Form> Build)> All { get; } =
    [
        ("giris", fleet => new LoginForm(fleet, new UnusedFormFactory())),
        ("hesap-olusturma", fleet => new RegisterForm(fleet)),
        ("ilk-kurulum", FirstAdminSetup),
        ("sifre-degistirme", fleet => new ChangePasswordForm(fleet, fleet.Customer)),
        ("araclar", CarList),
        ("arac-kiralama", Rental),
        ("kiralamalarim", MyRentals),
        ("kiralamalar", AllRentals),
        ("yonetim-paneli", AdminPanel),
        ("hesaplar", Accounts),
        ("musaitlik-takvimi", Calendar),
        ("bakim-kayitlari", Maintenance),
        ("gelir-raporu", Revenue),
        ("onay-penceresi", Confirmation),
    ];

    /// <summary>The maintenance log with the active record picked.</summary>
    private static MaintenanceForm Maintenance(SyntheticFleet fleet)
    {
        var form = new MaintenanceForm(fleet, fleet.Admin, 3, "Toyota Corolla (00 TC 330)");
        Run(form.LoadRecordsAsync());
        Find<ListBox>(form, "lstRecords").SelectedIndex = 0;
        return form;
    }

    /// <summary>The revenue of this year by month and by car.</summary>
    private static RevenueReportForm Revenue(SyntheticFleet fleet)
    {
        var form = new RevenueReportForm(fleet, fleet.Admin);
        Run(form.LoadDefaultRangeAsync());
        Run(form.LoadReportAsync());
        return form;
    }

    /// <summary>The question asked before a booking starts to be cancelled.</summary>
    private static MessageDialog Confirmation(SyntheticFleet fleet)
    {
        var rental = fleet.GetMyActiveRentalsAsync(fleet.Customer).Result[1];
        return new MessageDialog(RentalCards.Question(rental, underway: false), "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
    }

    /// <summary>A month with past, free, booked and maintenance days, as a customer sees it before booking.</summary>
    private static AvailabilityCalendarForm Calendar(SyntheticFleet fleet)
    {
        var form = new AvailabilityCalendarForm(fleet, fleet.Customer, 3, "Toyota Corolla (00 TC 330)");
        Run(form.LoadMonthAsync(null));
        return form;
    }

    /// <summary>The admin panel with a car picked from the list.</summary>
    private static AdminPanel AdminPanel(SyntheticFleet fleet)
    {
        var form = new AdminPanel(fleet, new UnusedFormFactory(), fleet.Admin);
        Run(form.LoadCarsAsync());
        Find<ListBox>(form, "lstCars").SelectedIndex = 2;
        return form;
    }

    /// <summary>The accounts with a customer picked; the list masks the contact data, the fields show it in full.</summary>
    private static UserManagementPanel Accounts(SyntheticFleet fleet)
    {
        var form = new UserManagementPanel(fleet, fleet.Admin);
        Run(form.LoadUsersAsync());
        Find<ListBox>(form, "lstUsers").SelectedIndex = 2;
        return form;
    }

    private static ProfileForm MyRentals(SyntheticFleet fleet)
    {
        var form = new ProfileForm(fleet, fleet, new UnusedFormFactory(), fleet.Customer);
        Run(form.LoadRentalsAsync());
        return form;
    }

    private static RentedCarsPanel AllRentals(SyntheticFleet fleet)
    {
        var form = new RentedCarsPanel(fleet, fleet, fleet, fleet.Admin);
        Run(form.RefreshAsync());
        return form;
    }

    /// <summary>The customer's car list right after a booking, with the result shown inline.</summary>
    private static CarListForm CarList(SyntheticFleet fleet)
    {
        var form = new CarListForm(fleet, new UnusedFormFactory(), fleet.Customer);
        Run(form.RefreshAsync());
        form.ShowStatus(UI.Theming.StatusKind.Success, "Kiralama kaydedildi: Toyota Corolla, 14.03.2026 - 17.03.2026, 5.670,00 TL. Kiralamalarım ekranından sözleşmeyi kaydedebilirsiniz.");
        return form;
    }

    /// <summary>The booking of a car for five days from the day after tomorrow.</summary>
    private static RentalForm Rental(SyntheticFleet fleet)
    {
        var form = new RentalForm(fleet, fleet, new UnusedFormFactory(), fleet.Customer, 1);
        Run(form.LoadCarAsync());
        Find<DateTimePicker>(form, "dtpStart").Value = SyntheticFleet.SampleToday.AddDays(2).ToDateTime(TimeOnly.MinValue);
        Find<TextBox>(form, "txtDays").Text = "5";
        form.ApplyDays();
        return form;
    }

    /// <summary>The keyed setup of a database that has accounts but no admin.</summary>
    private static FirstAdminSetupForm FirstAdminSetup(SyntheticFleet fleet) => new(fleet) { RequireSetupKey = true };

    /// <summary>The in-memory services complete synchronously; anything else is a bug in the tool.</summary>
    internal static void Run(Task task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException("A screen did not finish loading synchronously.");
        }

        task.GetAwaiter().GetResult();
    }

    internal static T Find<T>(Control root, string name)
        where T : Control =>
        root.Controls.Find(name, searchAllChildren: true).OfType<T>().SingleOrDefault()
        ?? throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Control '{0}' was not found.", name));

    /// <summary>No other window is opened while a screen is drawn.</summary>
    private sealed class UnusedFormFactory : IFormFactory
    {
        public T Create<T>(params object[] arguments)
            where T : Form => throw new InvalidOperationException("Screens are not opened while capturing.");
    }
}
