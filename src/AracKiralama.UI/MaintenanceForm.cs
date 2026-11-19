using System.ComponentModel;
using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>
    /// The maintenance and damage log of one car (admin only): the records, and a form to add a record,
    /// change the selected one or cancel it. A change carries the version the form loaded; when another
    /// admin changed the record meanwhile nothing is written and the record is loaded again.
    /// </summary>
    public sealed class MaintenanceForm : Form
    {
        private readonly IMaintenanceService _maintenance;
        private readonly UserSession _session;
        private readonly int _carId;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private readonly DataGridView _grid;
        private readonly ComboBox _kind;
        private readonly DateTimePicker _start;
        private readonly DateTimePicker _end;
        private readonly TextBox _description;
        private readonly TextBox _cost;
        private readonly Button _new;
        private readonly Button _save;
        private readonly Button _cancelRecord;

        public MaintenanceForm(IMaintenanceService maintenance, UserSession session, int carId, string carTitle)
        {
            ArgumentNullException.ThrowIfNull(maintenance);
            ArgumentNullException.ThrowIfNull(session);

            _maintenance = maintenance;
            _session = session;
            _carId = carId;
            Prompts = new MessageBoxPrompts(this);

            Text = "Bakım ve Hasar Kayıtları - " + carTitle;
            Icon = AppAssets.Icon;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            Padding = new Padding(12);
            var unit = Font.Height;
            ClientSize = new Size(unit * 60, unit * 36);
            MinimumSize = new Size(unit * 40, unit * 30);

            _grid = new DataGridView { Name = "grdRecords", Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            _grid.UseReadOnlyList();
            _grid.CellClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is MaintenanceRow row)
                {
                    SelectRecord(row.Record);
                }
            };

            _kind = new ComboBox { Name = "cmbKind", DropDownStyle = ComboBoxStyle.DropDownList, Width = unit * 12 };
            _kind.Items.AddRange([new KindChoice(MaintenanceKind.Maintenance), new KindChoice(MaintenanceKind.Damage)]);
            _kind.SelectedIndex = 0;
            _start = new DateTimePicker { Name = "dtpStart", Format = DateTimePickerFormat.Short, Width = unit * 10 };
            _end = new DateTimePicker { Name = "dtpEnd", Format = DateTimePickerFormat.Short, Width = unit * 10 };
            _description = new TextBox { Name = "txtDescription", Multiline = true, MaxLength = InputRules.MaintenanceDescriptionMaxLength, Dock = DockStyle.Fill, Height = unit * 4, ScrollBars = ScrollBars.Vertical };
            _cost = new TextBox { Name = "txtCost", MaxLength = 12, Width = unit * 10 };
            _cost.AcceptMoneyOnly();

            _new = new Button { Name = "btnNew", Text = "Yeni Kayıt", AutoSize = true };
            _save = new Button { Name = "btnSave", Text = "Kaydet", AutoSize = true };
            _cancelRecord = new Button { Name = "btnCancelRecord", Text = "Kaydı İptal Et", AutoSize = true, Enabled = false };
            _new.Click += (_, _) => ClearForm();
            _save.Click += async (_, _) => await SaveAsync();
            _cancelRecord.Click += async (_, _) => await CancelRecordAsync();
            _write.HoldClosingOf(this);

            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(fields, "Tür:", _kind);
            AddField(fields, "Başlangıç günü:", _start);
            AddField(fields, "Araç hazır olacağı gün:", _end);
            AddField(fields, "Açıklama:", _description);
            AddField(fields, "Maliyet (TL, isteğe bağlı):", _cost);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.AddRange([_new, _save, _cancelRecord]);
            fields.Controls.Add(buttons, 1, fields.RowCount);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_grid, 0, 0);
            root.Controls.Add(fields, 0, 1);
            Controls.Add(root);

            ClearForm();
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        internal IReadOnlyList<MaintenanceRecord> ShownRecords { get; private set; } = [];

        /// <summary>The record in the form and the version it was loaded with, or <see langword="null"/> for a new one.</summary>
        internal MaintenanceRecord? Selected { get; private set; }

        /// <summary>Loads the records; only the latest request is shown and binding them selects nothing.</summary>
        internal async Task LoadRecordsAsync()
        {
            var request = _loads.Next();
            var records = await _maintenance.GetForCarAsync(_session, _carId);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownRecords = records;
            // No selection handler is attached: binding the rows changes nothing in the form.
            _grid.DataSource = records.Select(r => new MaintenanceRow(r)).ToList();
            _grid.ClearSelection();
        }

        /// <summary>Puts a record in the form, as a click on its row does.</summary>
        internal void SelectRecord(MaintenanceRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            Selected = record;
            _kind.SelectedIndex = record.Kind == MaintenanceKind.Damage ? 1 : 0;
            _start.Value = record.StartDate.ToDateTime(TimeOnly.MinValue);
            _end.Value = record.EndDate.ToDateTime(TimeOnly.MinValue);
            _description.Text = record.Description;
            _cost.Text = record.Cost == 0m ? string.Empty : DisplayFormat.MoneyInput(record.Cost);
            var active = record.Status == MaintenanceStatus.Active;
            _save.Enabled = active;
            _cancelRecord.Enabled = active;
        }

        /// <summary>Adds the entered record, or saves the selected one with its loaded version; ignored while a write runs.</summary>
        internal Task<bool> SaveAsync() => WriteAsync(async () =>
        {
            var input = ReadInput();
            if (Selected is { } record)
            {
                await _maintenance.UpdateAsync(_session, record.Id, record.Version, input);
                Prompts.Inform("Bakım kaydı güncellendi.");
                await ReloadAndSelectAsync(record.Id);
            }
            else
            {
                var id = await _maintenance.AddAsync(_session, _carId, input);
                Prompts.Inform("Bakım kaydı eklendi. Araç bu günlerde kiralanamaz.");
                await ReloadAndSelectAsync(id);
            }
        });

        /// <summary>Cancels the selected record after a confirmation; ignored while a write runs.</summary>
        internal Task<bool> CancelRecordAsync() => WriteAsync(async () =>
        {
            if (Selected is not { } record || !Prompts.Confirm("Seçili bakım kaydı iptal edilecek ve günleri yeniden kiralanabilir olacak.\n\nDevam etmek istiyor musunuz?"))
            {
                return;
            }

            await _maintenance.CancelAsync(_session, record.Id, record.Version);
            Prompts.Inform("Bakım kaydı iptal edildi.");
            await ReloadAndSelectAsync(record.Id);
        });

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadRecordsAsync);
        }

        private async Task<bool> WriteAsync(Func<Task> write)
        {
            var ran = await this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    try
                    {
                        await write();
                    }
                    catch (BusinessRuleException)
                    {
                        // Changed, cancelled or blocked meanwhile: show the records as they are now.
                        await ReloadAndSelectAsync(Selected?.Id);
                        throw;
                    }
                },
                _new,
                _save,
                _cancelRecord,
                _grid);
            if (ran && !IsDisposed)
            {
                // The buttons follow the record the write left in the form, not the state before it.
                var active = Selected is null || Selected.Status == MaintenanceStatus.Active;
                _save.Enabled = active;
                _cancelRecord.Enabled = Selected is not null && active;
            }

            return ran;
        }

        private async Task ReloadAndSelectAsync(int? recordId)
        {
            await LoadRecordsAsync();
            if (recordId is { } id && ShownRecords.FirstOrDefault(r => r.Id == id) is { } record)
            {
                SelectRecord(record);
            }
            else
            {
                ClearForm();
            }
        }

        private MaintenanceInput ReadInput() => new(
            (_kind.SelectedItem as KindChoice)?.Kind ?? MaintenanceKind.Maintenance,
            _description.Text,
            DateOnly.FromDateTime(_start.Value),
            DateOnly.FromDateTime(_end.Value),
            _cost.Text);

        private void ClearForm()
        {
            Selected = null;
            _grid.ClearSelection();

            var today = DateTime.Today;
            _kind.SelectedIndex = 0;
            _start.Value = today;
            _end.Value = today.AddDays(1);
            _description.Clear();
            _cost.Clear();
            _save.Enabled = true;
            _cancelRecord.Enabled = false;
        }

        private static void AddField(TableLayoutPanel fields, string caption, Control field)
        {
            var row = fields.RowCount++;
            fields.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, row);
            fields.Controls.Add(field, 1, row);
        }

        private sealed record KindChoice(MaintenanceKind Kind)
        {
            public override string ToString() => MaintenanceRow.Describe(Kind);
        }
    }

    /// <summary>A row of the maintenance list.</summary>
    internal sealed class MaintenanceRow(MaintenanceRecord record)
    {
        [Browsable(false)]
        public MaintenanceRecord Record { get; } = record;

        [DisplayName("Tür")]
        public string Kind => Describe(Record.Kind);

        [DisplayName("Başlangıç")]
        public string Start => DisplayFormat.Date(Record.StartDate);

        [DisplayName("Hazır")]
        public string End => DisplayFormat.Date(Record.EndDate);

        [DisplayName("Açıklama")]
        public string Description => Record.Description;

        [DisplayName("Maliyet")]
        public string Cost => DisplayFormat.Money(Record.Cost);

        [DisplayName("Durum")]
        public string Status => Record.Status == MaintenanceStatus.Active ? "Aktif" : "İptal edildi";

        public static string Describe(MaintenanceKind kind) => kind == MaintenanceKind.Damage ? "Hasar onarımı" : "Bakım";

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Kind} {Start}-{End}");
    }
}
