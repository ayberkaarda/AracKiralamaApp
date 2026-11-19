using System.ComponentModel;
using System.Globalization;
using AracKiralama.Business.Services;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>
    /// The admin's revenue report: rentals by month or day and by car between two days, and the report as
    /// a CSV file. Only the latest requested report is shown, and the file is the report on the screen.
    /// </summary>
    public sealed class RevenueReportForm : Form
    {
        private readonly IReportService _reports;
        private readonly UserSession _session;
        private readonly RequestSequence _loads = new();
        private readonly ExclusiveOperation _save = new();
        private readonly DateTimePicker _from;
        private readonly DateTimePicker _to;
        private readonly ComboBox _grouping;
        private readonly Button _show;
        private readonly Button _export;
        private readonly DataGridView _periods;
        private readonly DataGridView _cars;
        private readonly Label _total;
        private readonly Label _note;

        public RevenueReportForm(IReportService reports, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(reports);
            ArgumentNullException.ThrowIfNull(session);

            _reports = reports;
            _session = session;
            Prompts = new MessageBoxPrompts(this);
            SaveFiles = new SaveFileDialogPrompt(this);

            Text = "Gelir Raporu";
            Icon = AppAssets.Icon;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            Padding = new Padding(12);
            var unit = Font.Height;
            ClientSize = new Size(unit * 62, unit * 38);
            MinimumSize = new Size(unit * 44, unit * 30);

            var today = DateTime.Today;
            _from = new DateTimePicker { Name = "dtpFrom", Format = DateTimePickerFormat.Short, Width = unit * 9, Value = new DateTime(today.Year, today.Month, 1) };
            _to = new DateTimePicker { Name = "dtpTo", Format = DateTimePickerFormat.Short, Width = unit * 9, Value = today };
            _grouping = new ComboBox { Name = "cmbGrouping", DropDownStyle = ComboBoxStyle.DropDownList, Width = unit * 8 };
            _grouping.Items.AddRange(["Aylık", "Günlük"]);
            _grouping.SelectedIndex = 0;
            _show = new Button { Name = "btnShow", Text = "Raporu Göster", AutoSize = true };
            _export = new Button { Name = "btnExportCsv", Text = "CSV Olarak Kaydet", AutoSize = true, Enabled = false };
            _show.Click += async (_, _) => await this.RunAsync(LoadReportAsync);
            _export.Click += async (_, _) => await ExportCsvAsync();
            _save.HoldClosingOf(this);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            bar.Controls.AddRange(
            [
                Caption("Başlangıç:"), _from, Caption("Bitiş:"), _to, Caption("Gruplama:"), _grouping, _show, _export,
            ]);

            _periods = Grid("grdPeriods");
            _cars = Grid("grdCars");
            _total = new Label { Name = "lblTotal", AutoSize = true, Margin = new Padding(0, 8, 0, 0), Font = new Font(Font, FontStyle.Bold) };

            _note = new Label { Name = "lblNote", AutoSize = true, MaximumSize = new Size(unit * 58, 0), Margin = new Padding(0, 4, 0, 0) };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(bar, 0, 0);
            root.Controls.Add(Caption("Dönemlere göre"), 0, 1);
            root.Controls.Add(_periods, 0, 2);
            root.Controls.Add(Caption("Araçlara göre"), 0, 3);
            root.Controls.Add(_cars, 0, 4);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_total, 0, 5);
            root.Controls.Add(_note, 0, 6);
            Controls.Add(root);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The save dialog; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal ISaveFilePrompt SaveFiles { get; set; }

        /// <summary>Where the file is written; replaced in tests.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal Func<string, byte[], Task> WriteFile { get; set; } = (path, content) => ExportFileWriter.WriteAsync(path, content);

        /// <summary>The report on the screen, which the CSV file contains.</summary>
        internal RevenueReport? ShownReport { get; private set; }

        /// <summary>Puts this month up to today by the database's clock into the range; the pickers start with the computer's.</summary>
        internal async Task LoadDefaultRangeAsync()
        {
            var range = await _reports.GetDefaultRangeAsync(_session);
            if (!IsDisposed)
            {
                _from.Value = range.From.ToDateTime(TimeOnly.MinValue);
                _to.Value = range.To.ToDateTime(TimeOnly.MinValue);
            }
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadDefaultRangeAsync);
        }

        internal void SetRange(DateOnly from, DateOnly to, RevenueGrouping grouping)
        {
            _from.Value = from.ToDateTime(TimeOnly.MinValue);
            _to.Value = to.ToDateTime(TimeOnly.MinValue);
            _grouping.SelectedIndex = grouping == RevenueGrouping.Day ? 1 : 0;
        }

        /// <summary>Asks for the report of the chosen days; an answer that arrives after a newer request is dropped.</summary>
        internal async Task LoadReportAsync()
        {
            var request = _loads.Next();
            var grouping = _grouping.SelectedIndex == 1 ? RevenueGrouping.Day : RevenueGrouping.Month;
            var report = await _reports.GetRevenueAsync(_session, DateOnly.FromDateTime(_from.Value), DateOnly.FromDateTime(_to.Value), grouping);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownReport = report;
            _periods.DataSource = report.Periods.Select(l => new RevenueRow(l)).ToList();
            _periods.ClearSelection();
            _cars.DataSource = report.Cars.Select(l => new RevenueRow(l)).ToList();
            _cars.ClearSelection();
            _total.Text = string.Create(
                DisplayFormat.Turkish,
                $"Toplam: {report.Total.Rentals} kiralama, {report.Total.Days} gün, {DisplayFormat.Money(report.Total.Revenue)}");
            _note.Text = report.Note;
            _export.Enabled = true;
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
                _export);
            return written;
        }

        private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) };

        private static DataGridView Grid(string name)
        {
            var grid = new DataGridView { Name = name, Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            grid.UseReadOnlyList();
            return grid;
        }

        /// <summary>A line of the report as the lists show it.</summary>
        private sealed class RevenueRow(RevenueLine line)
        {
            [DisplayName("Ad")]
            public string Label => line.Label;

            [DisplayName("Plaka")]
            public string Plate => line.Plate ?? string.Empty;

            [DisplayName("Kiralama")]
            public int Rentals => line.Rentals;

            [DisplayName("Gün")]
            public int Days => line.Days;

            [DisplayName("Gelir")]
            public string Revenue => DisplayFormat.Money(line.Revenue);

            public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Label} {Revenue}");
        }
    }
}
