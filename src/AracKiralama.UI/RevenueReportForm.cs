using System.ComponentModel;
using System.Globalization;
using AracKiralama.Business.Services;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// The admin's revenue report: rentals by month or day and by car between two days, and the report as
    /// a CSV file. Only the latest requested report is shown, and the file is the report on the screen.
    /// </summary>
    public sealed class RevenueReportForm : AppDialog
    {
        private readonly IReportService _reports;
        private readonly UserSession _session;
        private readonly RequestSequence _loads = new();
        private readonly ExclusiveOperation _save = new();

        private readonly DatePicker dtpFrom = new() { Name = nameof(dtpFrom), Format = DateTimePickerFormat.Short };
        private readonly DatePicker dtpTo = new() { Name = nameof(dtpTo), Format = DateTimePickerFormat.Short };
        private readonly ThemedComboBox cmbGrouping = FormLayout.Picker(nameof(cmbGrouping), 120);
        private readonly ThemedButton btnShow = FormLayout.Button(nameof(btnShow), "Raporu Göster", ButtonKind.Primary, Glyphs.Chart);
        private readonly ThemedButton btnExportCsv = FormLayout.Button(nameof(btnExportCsv), "CSV Olarak Kaydet", ButtonKind.Secondary, Glyphs.Download);
        private readonly Label lblTotalRentals = Figure(nameof(lblTotalRentals));
        private readonly Label lblTotalDays = Figure(nameof(lblTotalDays));
        private readonly Label lblTotalRevenue = Figure(nameof(lblTotalRevenue));
        private readonly ThemedListBox lstPeriods = new() { Name = nameof(lstPeriods), AccessibleName = "Dönemlere göre gelir", EmptyText = "Raporu görmek için günleri seçip \"Raporu Göster\"e basın.", LoadingText = "Rapor hazırlanıyor…" };
        private readonly ThemedListBox lstCars = new() { Name = nameof(lstCars), AccessibleName = "Araçlara göre gelir", EmptyText = "Seçilen günlerde gelir yok.", LoadingText = "Rapor hazırlanıyor…" };
        private readonly Label lblNote = FormLayout.Hint(string.Empty);
        private readonly ThemedButton btnClose = FormLayout.Button(nameof(btnClose), "Kapat", ButtonKind.Secondary);

        public RevenueReportForm(IReportService reports, UserSession session)
            : base("Gelir raporu", "Seçtiğiniz günlerde teslim alınan kiralamaların geliri, dönemlere ve araçlara göre.", Glyphs.Chart, new Size(1040, 700), new Size(860, 600), modal: true)
        {
            ArgumentNullException.ThrowIfNull(reports);
            ArgumentNullException.ThrowIfNull(session);

            _reports = reports;
            _session = session;
            SaveFiles = new SaveFileDialogPrompt(this);

            var today = DateTime.Today;
            dtpFrom.Value = new DateTime(today.Year, today.Month, 1);
            dtpTo.Value = today;
            cmbGrouping.Items.AddRange(["Aylık", "Günlük"]);
            cmbGrouping.SelectedIndex = 0;
            btnExportCsv.Enabled = false;
            BuildLayout();
            _save.HoldClosingOf(this);
            ShowTotals(null);

            Load += async (_, _) => await this.RunAsync(LoadDefaultRangeAsync);
            btnShow.Click += async (_, _) => await this.RunAsync(LoadReportAsync);
            btnExportCsv.Click += async (_, _) => await ExportCsvAsync();
            btnClose.Click += (_, _) => Close();
        }

        /// <summary>The save dialog; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal ISaveFilePrompt SaveFiles { get; set; }

        /// <summary>Where the file is written; replaced in tests.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<string, byte[], Task> WriteFile { get; set; } = (path, content) => ExportFileWriter.WriteAsync(path, content);

        /// <summary>The report on the screen, which the CSV file contains.</summary>
        internal RevenueReport? ShownReport { get; private set; }

        /// <summary>The totals as one sentence, for screen readers and tests.</summary>
        internal string TotalText { get; private set; } = string.Empty;

        /// <summary>Puts this month up to today by the database's clock into the range; the pickers start with the computer's.</summary>
        internal async Task LoadDefaultRangeAsync()
        {
            var range = await _reports.GetDefaultRangeAsync(_session);
            if (!IsDisposed)
            {
                dtpFrom.Value = range.From.ToDateTime(TimeOnly.MinValue);
                dtpTo.Value = range.To.ToDateTime(TimeOnly.MinValue);
            }
        }

        internal void SetRange(DateOnly from, DateOnly to, RevenueGrouping grouping)
        {
            dtpFrom.Value = from.ToDateTime(TimeOnly.MinValue);
            dtpTo.Value = to.ToDateTime(TimeOnly.MinValue);
            cmbGrouping.SelectedIndex = grouping == RevenueGrouping.Day ? 1 : 0;
        }

        /// <summary>Asks for the report of the chosen days; an answer that arrives after a newer request is dropped.</summary>
        internal async Task LoadReportAsync()
        {
            var request = _loads.Next();
            var grouping = cmbGrouping.SelectedIndex == 1 ? RevenueGrouping.Day : RevenueGrouping.Month;
            lstPeriods.IsLoading = lstCars.IsLoading = true;
            try
            {
                var report = await _reports.GetRevenueAsync(_session, DateOnly.FromDateTime(dtpFrom.Value), DateOnly.FromDateTime(dtpTo.Value), grouping);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownReport = report;
                lstPeriods.EmptyText = "Seçilen günlerde gelir yok.";
                lstPeriods.ReplaceItems(Lines(report.Periods));
                lstCars.ReplaceItems(Lines(report.Cars));
                lstPeriods.ClearSelected();
                lstCars.ClearSelected();
                ShowTotals(report.Total);
                lblNote.Text = report.Note;
                btnExportCsv.Enabled = true;
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    lstPeriods.IsLoading = lstCars.IsLoading = false;
                }
            }
        }

        /// <summary>Saves the shown report as CSV; ignored while a save runs. Returns whether a file was written.</summary>
        internal async Task<bool> ExportCsvAsync()
        {
            var written = false;
            await this.RunExclusiveAsync(
                _save,
                async () =>
                {
                    if (ShownReport is not { } report)
                    {
                        return;
                    }

                    var file = _reports.ToCsv(report);
                    if (SaveFiles.AskSavePath(file.SuggestedName, ExportFilters.Csv) is not { } path)
                    {
                        return;
                    }

                    await WriteFile(path, file.Content);
                    written = true;
                    Prompts.Inform("Rapor kaydedildi.");
                },
                btnExportCsv);
            return written;
        }

        /// <summary>A report line drawn with its revenue at the right and a bar of its share of the largest line.</summary>
        internal static IEnumerable<RevenueItem> Lines(IReadOnlyList<RevenueLine> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);

            var largest = lines.Count == 0 ? 0m : lines.Max(l => l.Revenue);
            return lines.Select(line => new RevenueItem(line, largest <= 0 ? 0 : (double)(line.Revenue / largest)));
        }

        private void ShowTotals(RevenueLine? total)
        {
            if (total is null)
            {
                lblTotalRentals.Text = lblTotalDays.Text = lblTotalRevenue.Text = "—";
                TotalText = string.Empty;
                return;
            }

            lblTotalRentals.Text = total.Rentals.ToString("#,0", DisplayFormat.Turkish);
            lblTotalDays.Text = total.Days.ToString("#,0", DisplayFormat.Turkish);
            lblTotalRevenue.Text = DisplayFormat.Money(total.Revenue);
            TotalText = string.Create(DisplayFormat.Turkish, $"Toplam: {total.Rentals} kiralama, {total.Days} gün, {DisplayFormat.Money(total.Revenue)}");
            AccessibleDescription = TotalText;
        }

        private static Label Figure(string name)
        {
            var label = FormLayout.Text(name);
            label.Font = Theme.Fonts.Figure;
            label.Margin = new Padding(0, Spacing.XS, 0, 0);
            return label;
        }

        /// <summary>A small card with a caption above a figure; the revenue's card carries the signal edge.</summary>
        private static CardPanel Tile(string name, string caption, Label figure, bool marked = false)
        {
            var label = FormLayout.Hint(caption);
            label.Margin = Padding.Empty;
            var card = FormLayout.Card(name, null, FormLayout.Stack(name + "Stack", label, figure), fill: false);
            card.Padding = new Padding(Spacing.L + (marked ? Spacing.XS : 0), Spacing.M, Spacing.L, Spacing.M);
            card.Margin = Padding.Empty;
            if (marked)
            {
                card.Edge = Theme.Current.Warm;
            }

            return card;
        }

        private void BuildLayout()
        {
            Name = nameof(RevenueReportForm);
            var from = FormLayout.Date(dtpFrom);
            var to = FormLayout.Date(dtpTo);
            btnExportCsv.Margin = Padding.Empty;
            var filters = FormLayout.FieldRow(
                "filterRow",
                (FormLayout.Field("&Başlangıç", from), 0),
                (FormLayout.Field("Bi&tiş", to), 0),
                (FormLayout.Field("&Gruplama", cmbGrouping), 0),
                (FormLayout.FieldAction(btnShow), 0),
                (FormLayout.Spacer("filterSpacer"), 100),
                (FormLayout.FieldAction(btnExportCsv), 0));
            var filterCard = FormLayout.Card("filterCard", null, filters, fill: false);
            filterCard.Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, 0);
            filterCard.Margin = new Padding(0, 0, 0, Spacing.M);

            var tiles = new TableLayoutPanel
            {
                Name = "totalTiles",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, Spacing.M),
            };
            tiles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var rentals = Tile("rentalsTile", "Kiralama", lblTotalRentals);
            var days = Tile("daysTile", "Kiralanan gün", lblTotalDays);
            var revenue = Tile("revenueTile", "Gelir", lblTotalRevenue, marked: true);
            rentals.Margin = days.Margin = new Padding(0, 0, Spacing.M, 0);
            foreach (var (tile, column) in new[] { (rentals, 0), (days, 1), (revenue, 2) })
            {
                tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
                tile.Dock = DockStyle.Fill;
                tiles.Controls.Add(tile, column, 0);
            }

            foreach (var list in new[] { lstPeriods, lstCars })
            {
                list.Describe = item => item is RevenueItem line ? line.Row() : new ListRow(item.ToString() ?? string.Empty);
            }

            var periodsCard = FormLayout.Card("periodsCard", "Dönemlere göre", lstPeriods);
            var carsCard = FormLayout.Card("carsCard", "Araçlara göre", lstCars);
            var lists = FormLayout.Columns("lists", periodsCard, 45, carsCard);
            lblNote.Name = nameof(lblNote);
            lblNote.Margin = new Padding(0, Spacing.M, 0, 0);

            Content.Controls.Add(FormLayout.Page("reportPage", (filterCard, false), (tiles, false), (lists, true), (lblNote, false)));

            AcceptButton = btnShow;
            CancelButton = btnClose;
            SetFooterNote("CSV dosyası ekrandaki raporun aynısıdır.");
            SetFooterButtons(btnClose);
            FinishLayout();
        }
    }

    /// <summary>A line of the report as the lists show it: the label (and plate), the counts below, the revenue at the right with a bar.</summary>
    internal sealed record RevenueItem(RevenueLine Line, double Share)
    {
        public ListRow Row() => new(
            Line.Plate is { Length: > 0 } plate ? Line.Label + " · " + plate : Line.Label,
            string.Create(CultureInfo.CurrentCulture, $"{Line.Rentals} kiralama · {Line.Days} gün"),
            Value: DisplayFormat.Money(Line.Revenue),
            Share: Share);

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Line.Label} {DisplayFormat.Money(Line.Revenue)}");
    }
}
