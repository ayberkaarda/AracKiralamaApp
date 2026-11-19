using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Tests.Support;
using static AracKiralama.UI.Tests.Support.StaThread;

namespace AracKiralama.UI.Tests;

/// <summary>
/// The calendar, the maintenance log, the revenue report and the contract buttons: one write at a time,
/// the latest load wins, binding selects nothing, a stale save reloads, and the layout follows the font.
/// </summary>
public sealed class OperationsUiTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    private static readonly string[] CellTexts = ["30\nGeçmiş", "28\nBakım", "31\nDolu"];

    private static readonly string[] OperationButtons = ["btnCalendar", "btnMaintenance", "btnRevenue"];

    [Fact]
    public void Calendar_AnswersOutOfOrder_ShowTheLatestMonth() => Run(() =>
    {
        var availability = new ControlledAvailabilityService();
        using var form = new AvailabilityCalendarForm(availability, Sessions.User, 1, "Anka Kent");

        var first = form.LoadMonthAsync(March);
        var second = form.LoadMonthAsync(March.AddMonths(1));
        availability.Requests[1].Answer.SetResult(ControlledAvailabilityService.Calendar(March.AddMonths(1), DayState.Booked));
        availability.Requests[0].Answer.SetResult(ControlledAvailabilityService.Calendar(March, DayState.Maintenance));

        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal(March.AddMonths(1), form.ShownCalendar!.Month);
        Assert.Equal("Nisan 2026", Find<Label>(form, "lblMonth").Text);
    });

    /// <summary>The first month comes from the database's today; the computer's clock is not asked, and navigation waits for it.</summary>
    [Fact]
    public void Calendar_OpensOnTheMonthTheDatabaseGives() => Run(() =>
    {
        var availability = new ControlledAvailabilityService();
        using var form = new AvailabilityCalendarForm(availability, Sessions.User, 1, "Anka Kent");

        Assert.True(form.MoveAsync(1).IsCompletedSuccessfully);
        Assert.Empty(availability.Requests);

        _ = form.LoadMonthAsync(null);
        availability.Requests[0].Answer.SetResult(ControlledAvailabilityService.Calendar(new DateOnly(2031, 7, 1)));
        _ = form.MoveAsync(1);

        Assert.Null(availability.Requests[0].Month);
        Assert.Equal(new DateOnly(2031, 8, 1), availability.Requests[1].Month);
        Assert.Equal("Temmuz 2031", Find<Label>(form, "lblMonth").Text);
    });

    /// <summary>Two quick "Sonraki ay" clicks before the first answer arrives ask for two different months; the latest wins.</summary>
    [Fact]
    public void Calendar_QuickClicks_AddUpAndTheLatestWins() => Run(() =>
    {
        var availability = new ControlledAvailabilityService();
        using var form = new AvailabilityCalendarForm(availability, Sessions.User, 1, "Anka Kent");
        _ = form.LoadMonthAsync(March);
        availability.Requests[0].Answer.SetResult(ControlledAvailabilityService.Calendar(March));

        var first = form.MoveAsync(1);
        var second = form.MoveAsync(1);
        availability.Requests[2].Answer.SetResult(ControlledAvailabilityService.Calendar(March.AddMonths(2)));
        availability.Requests[1].Answer.SetResult(ControlledAvailabilityService.Calendar(March.AddMonths(1)));

        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully);
        Assert.Equal([March.AddMonths(1), March.AddMonths(2)], availability.Requests.Skip(1).Select(r => r.Month!.Value));
        Assert.Equal("Mayıs 2026", Find<Label>(form, "lblMonth").Text);

        // A third click counts from the month on the screen.
        _ = form.MoveAsync(-1);
        Assert.Equal(March.AddMonths(1), availability.Requests[3].Month);
    });

    /// <summary>The report starts with this month up to today by the database's clock.</summary>
    [Fact]
    public void Report_StartsWithTheRangeTheDatabaseGives() => Run(() =>
    {
        using var form = new RevenueReportForm(new ControlledReportService(), Sessions.Admin) { Prompts = new RecordingPrompts() };

        Assert.True(form.LoadDefaultRangeAsync().IsCompletedSuccessfully);

        Assert.Equal(new DateTime(2027, 5, 1), Find<DateTimePicker>(form, "dtpFrom").Value);
        Assert.Equal(new DateTime(2027, 5, 10), Find<DateTimePicker>(form, "dtpTo").Value);
    });

    [Fact]
    public void Calendar_EveryDaySaysItsStateInWordsFromMonday() => Run(() =>
    {
        var availability = new ControlledAvailabilityService();
        using var form = new AvailabilityCalendarForm(availability, Sessions.User, 1, "Anka Kent");

        _ = form.LoadMonthAsync(March);
        availability.Requests[0].Answer.SetResult(ControlledAvailabilityService.Calendar(March, DayState.Past, DayState.Booked, DayState.Maintenance));

        // 1 March 2026 is a Sunday: the first row starts with six empty Monday-to-Saturday cells.
        Assert.All(form.DayCells.Take(6), c => Assert.Equal(string.Empty, c.Text));
        Assert.Equal("1" + Environment.NewLine + "Geçmiş", form.DayCells[6].Text);
        Assert.Equal("2" + Environment.NewLine + "Dolu", form.DayCells[7].Text);
        Assert.Equal("3" + Environment.NewLine + "Bakım", form.DayCells[8].Text);
        Assert.Equal("4" + Environment.NewLine + "Boş", form.DayCells[9].Text);
        Assert.Equal("31" + Environment.NewLine + "Boş", form.DayCells[36].Text);

        // The state is in the text; no cell gets a text colour of its own (high contrast keeps the system's).
        Assert.All(form.DayCells, c => Assert.Equal(form.ForeColor, c.ForeColor));
    });

    [Theory]
    [InlineData(9f)]
    [InlineData(12f)]
    [InlineData(20f)]
    public void Calendar_CellsFitTheirTextAtAnyFontSize(float size) => Run(() =>
    {
        using var font = new Font("Segoe UI", size);
        var cell = AvailabilityCalendarForm.CellSize(font);

        foreach (var text in CellTexts)
        {
            var needed = TextRenderer.MeasureText(text, font);
            Assert.True(needed.Width <= cell.Width && needed.Height <= cell.Height, $"{text} needs {needed}, the cell is {cell} at {size} pt.");
        }
    });

    [Fact]
    public void Maintenance_BindingTheRecordsSelectsNothing() => Run(() =>
    {
        using var form = new MaintenanceForm(new ControlledMaintenanceService(), Sessions.Admin, 1, "Anka Kent") { Prompts = new RecordingPrompts() };

        Assert.True(form.LoadRecordsAsync().IsCompletedSuccessfully);

        Assert.Single(form.ShownRecords);
        Assert.Null(form.Selected);
        Assert.False(Find<Button>(form, "btnCancelRecord").Enabled);
    });

    [Fact]
    public void Maintenance_SecondSaveWhilePending_IsIgnoredAndTheLoadedVersionIsSent() => Run(() =>
    {
        var maintenance = new ControlledMaintenanceService();
        using var form = new MaintenanceForm(maintenance, Sessions.Admin, 1, "Anka Kent") { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRecordsAsync().IsCompletedSuccessfully);
        form.SelectRecord(form.ShownRecords[0]);

        var first = form.SaveAsync();
        var second = form.SaveAsync();
        var cancel = form.CancelRecordAsync();

        Assert.False(second.Result);
        Assert.False(cancel.Result);
        Assert.Equal(1, maintenance.Writes);
        Assert.Equal((4, 1), maintenance.LastUpdate);
        Assert.False(Find<Button>(form, "btnSave").Enabled);
        Assert.False(Find<Button>(form, "btnNew").Enabled);

        maintenance.Records[0] = ControlledMaintenanceService.Record(4, 2);
        maintenance.Complete();
        Assert.True(first.Result);
        Assert.Equal(2, form.Selected!.Version);
        Assert.True(Find<Button>(form, "btnSave").Enabled);
    });

    /// <summary>Another admin changed the record: the refusal is shown and the record is loaded again with its new version.</summary>
    [Fact]
    public void Maintenance_StaleSave_ReloadsTheRecord() => Run(() =>
    {
        var maintenance = new ControlledMaintenanceService();
        using var form = new MaintenanceForm(maintenance, Sessions.Admin, 1, "Anka Kent") { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRecordsAsync().IsCompletedSuccessfully);
        form.SelectRecord(form.ShownRecords[0]);

        var save = form.SaveAsync();
        maintenance.Records[0] = ControlledMaintenanceService.Record(4, 3);
        maintenance.Records[0].Description = "Başka yönetici";
        maintenance.RefuseAsStale("Bakım kaydı siz düzenlerken başka biri tarafından değiştirildi.");

        Assert.True(save.Result);
        Assert.Equal(3, form.Selected!.Version);
        Assert.Equal("Başka yönetici", Find<TextBox>(form, "txtDescription").Text);
        Assert.Contains(NoDialogs.Requested, r => r.Contains("başka biri tarafından değiştirildi", StringComparison.Ordinal));
    });

    /// <summary>A refusal of the input (a typo, days a rental holds) keeps everything the admin typed, for a new and a selected record.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Maintenance_RefusedInput_KeepsWhatWasTyped(bool selected) => Run(() =>
    {
        var maintenance = new ControlledMaintenanceService();
        using var form = new MaintenanceForm(maintenance, Sessions.Admin, 1, "Anka Kent") { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadRecordsAsync().IsCompletedSuccessfully);
        if (selected)
        {
            form.SelectRecord(form.ShownRecords[0]);
        }

        Find<TextBox>(form, "txtDescription").Text = "Yazılan açıklama";
        Find<TextBox>(form, "txtCost").Text = "99,5";
        var save = form.SaveAsync();
        maintenance.Records[0] = ControlledMaintenanceService.Record(4, 1);
        maintenance.Refuse("Araç seçtiğiniz tarihlerin bir kısmında kiralanmış.");

        Assert.True(save.Result);
        Assert.Equal("Yazılan açıklama", Find<TextBox>(form, "txtDescription").Text);
        Assert.Equal("99,5", Find<TextBox>(form, "txtCost").Text);
        Assert.Equal(selected ? 4 : null, form.Selected?.Id);
        Assert.True(Find<Button>(form, "btnSave").Enabled);
    });

    [Fact]
    public void Report_AnswersOutOfOrder_ShowTheLatestAndTheCsvIsTheShownReport() => Run(() =>
    {
        var reports = new ControlledReportService();
        var saveFiles = new RecordingSaveFilePrompt("rapor.csv");
        var written = new List<(string Path, byte[] Content)>();
        using var form = new RevenueReportForm(reports, Sessions.Admin)
        {
            Prompts = new RecordingPrompts(),
            SaveFiles = saveFiles,
            WriteFile = (path, content) =>
            {
                written.Add((path, content));
                return Task.CompletedTask;
            },
        };
        Assert.False(Find<Button>(form, "btnExportCsv").Enabled);

        _ = form.LoadReportAsync();
        _ = form.LoadReportAsync();
        reports.Requests[1].SetResult(ControlledReportService.Report(200m));
        reports.Requests[0].SetResult(ControlledReportService.Report(100m));

        Assert.Equal(200m, form.ShownReport!.Total.Revenue);
        Assert.Equal("Toplam: 1 kiralama, 2 gün, 200,00 TL", Find<Label>(form, "lblTotal").Text);
        Assert.True(form.ExportCsvAsync().Result);
        var (path, content) = Assert.Single(written);
        Assert.Equal("rapor.csv", path);
        Assert.Equal(reports.ToCsv(form.ShownReport).Content, content);
        Assert.Equal(["gelir-raporu-2026-03-01-2026-03-31.csv"], saveFiles.Suggested);
    });

    [Fact]
    public void Report_SecondExportWhilePending_IsIgnoredAndACancelledDialogWritesNothing() => Run(() =>
    {
        var reports = new ControlledReportService();
        var pending = new TaskCompletionSource();
        var writes = 0;
        using var form = new RevenueReportForm(reports, Sessions.Admin)
        {
            Prompts = new RecordingPrompts(),
            SaveFiles = new RecordingSaveFilePrompt("rapor.csv"),
            WriteFile = (_, _) =>
            {
                writes++;
                return pending.Task;
            },
        };
        _ = form.LoadReportAsync();
        reports.Requests[0].SetResult(ControlledReportService.Report(100m));

        var first = form.ExportCsvAsync();
        var second = form.ExportCsvAsync();
        Assert.False(second.Result);
        Assert.False(Find<Button>(form, "btnExportCsv").Enabled);
        pending.SetResult();
        Assert.True(first.Result);
        Assert.Equal(1, writes);

        form.SaveFiles = new RecordingSaveFilePrompt(null);
        Assert.False(form.ExportCsvAsync().Result);
        Assert.Equal(1, writes);
    });

    [Fact]
    public void Contract_SecondClickWhilePending_IsIgnoredAndEveryContractButtonWaits() => Run(() =>
    {
        var rentals = new ControlledRentalService();
        var contracts = new ControlledContractService();
        var written = new List<string>();
        using var form = new ProfileForm(rentals, contracts, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        form.Contracts.SaveFiles = new RecordingSaveFilePrompt("sozlesme.html");
        form.Contracts.WriteFile = (path, _) =>
        {
            written.Add(path);
            return Task.CompletedTask;
        };
        Assert.True(form.LoadRentalsAsync().IsCompletedSuccessfully);
        Assert.Equal(2, form.ContractButtons.Count);

        var first = form.SaveContractAsync(form.ShownRentals[0]);
        var second = form.SaveContractAsync(form.ShownRentals[1]);

        Assert.False(second.Result);
        Assert.Equal(1, contracts.Calls);
        Assert.All(form.ContractButtons, b => Assert.False(b.Enabled));
        Assert.All(form.EndButtons, b => Assert.True(b.Enabled));
        contracts.Complete();
        Assert.True(first.Result);
        Assert.Equal(["sozlesme.html"], written);
        Assert.All(form.ContractButtons, b => Assert.True(b.Enabled));
    });

    [Fact]
    public void AdminPanel_CarOperationsNeedTheCarInTheForm() => Run(() =>
    {
        var cars = new ControlledCarService();
        using var form = new AdminPanel(cars, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadCarsAsync().IsCompletedSuccessfully);

        Assert.False(Find<Button>(form, "btnCalendar").Enabled);
        Assert.False(Find<Button>(form, "btnMaintenance").Enabled);
        Assert.True(Find<Button>(form, "btnRevenue").Enabled);

        form.SelectCar(cars.Cars[0]);

        Assert.True(Find<Button>(form, "btnCalendar").Enabled);
        Assert.True(Find<Button>(form, "btnMaintenance").Enabled);
    });

    /// <summary>The operation buttons below the car's picture size themselves from the font and stay inside the panel.</summary>
    [Fact]
    public void AdminPanel_OperationButtonsFitTheirText() => Run(() =>
    {
        using var form = new AdminPanel(new ControlledCarService(), new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        var panel = Find<FlowLayoutPanel>(form, "pnlOperations");

        foreach (var name in OperationButtons)
        {
            var button = Find<Button>(form, name);
            Assert.True(button.AutoSize);
            Assert.True(TextRenderer.MeasureText(button.Text, button.Font).Width < button.PreferredSize.Width);
            Assert.Same(panel, button.Parent);
        }
    });
}
