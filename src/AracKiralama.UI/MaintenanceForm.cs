using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// The maintenance and damage log of one car (admin only): the records, and a form to add a record,
    /// change the selected one or cancel it. A change carries the version the form loaded; when another
    /// admin changed the record meanwhile nothing is written and the record is loaded again. Any other
    /// refusal leaves the entered values in the form.
    /// </summary>
    public sealed class MaintenanceForm : AppDialog
    {
        private readonly IMaintenanceService _maintenance;
        private readonly UserSession _session;
        private readonly int _carId;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private bool _binding;

        private readonly ThemedListBox lstRecords = new() { Name = nameof(lstRecords), AccessibleName = "Bakım kayıtları", EmptyText = "Bu araç için bakım veya hasar kaydı yok.", LoadingText = "Kayıtlar yükleniyor…" };
        private readonly Label lblSelection = FormLayout.Text(nameof(lblSelection));
        private readonly Label lblSelectionDetail = FormLayout.Hint(string.Empty);
        private readonly ThemedComboBox cmbKind = FormLayout.Picker(nameof(cmbKind), 170);
        private readonly DatePicker dtpStart = new() { Name = nameof(dtpStart), Format = DateTimePickerFormat.Short };
        private readonly DatePicker dtpEnd = new() { Name = nameof(dtpEnd), Format = DateTimePickerFormat.Short };
        private readonly TextBox txtDescription = new() { Name = nameof(txtDescription), Multiline = true, MaxLength = InputRules.MaintenanceDescriptionMaxLength, ScrollBars = ScrollBars.Vertical };
        private readonly TextBox txtCost = new() { Name = nameof(txtCost), MaxLength = 12, PlaceholderText = "0,00" };
        private readonly ThemedButton btnNew = FormLayout.Button(nameof(btnNew), "Yeni Kayıt", ButtonKind.Secondary, Glyphs.Add);
        private readonly ThemedButton btnSave = FormLayout.Button(nameof(btnSave), "Kaydet", ButtonKind.Primary, Glyphs.Save);
        private readonly ThemedButton btnCancelRecord = FormLayout.Button(nameof(btnCancelRecord), "Kaydı İptal Et", ButtonKind.Danger, Glyphs.Cancel);
        private readonly ThemedButton btnClose = FormLayout.Button(nameof(btnClose), "Kapat", ButtonKind.Secondary);

        public MaintenanceForm(IMaintenanceService maintenance, UserSession session, int carId, string carTitle)
            : base("Bakım ve hasar kayıtları", carTitle, Glyphs.Repair, new Size(1040, 620), new Size(860, 540), modal: true)
        {
            ArgumentNullException.ThrowIfNull(maintenance);
            ArgumentNullException.ThrowIfNull(session);

            _maintenance = maintenance;
            _session = session;
            _carId = carId;
            cmbKind.Items.AddRange([new KindChoice(MaintenanceKind.Maintenance), new KindChoice(MaintenanceKind.Damage)]);
            txtCost.AcceptMoneyOnly();
            BuildLayout();
            _write.HoldClosingOf(this);
            ClearForm();

            Load += async (_, _) => await this.RunAsync(LoadRecordsAsync);
            lstRecords.SelectedIndexChanged += (_, _) =>
            {
                if (!_binding && lstRecords.SelectedItem is MaintenanceItem item)
                {
                    SelectRecord(item.Record);
                }
            };
            btnNew.Click += (_, _) => ClearForm();
            btnSave.Click += async (_, _) => await SaveAsync();
            btnCancelRecord.Click += async (_, _) => await CancelRecordAsync();
            btnClose.Click += (_, _) => Close();
        }

        internal IReadOnlyList<MaintenanceRecord> ShownRecords { get; private set; } = [];

        /// <summary>The record in the form and the version it was loaded with, or <see langword="null"/> for a new one.</summary>
        internal MaintenanceRecord? Selected { get; private set; }

        /// <summary>Loads the records; only the latest request is shown and binding them changes nothing in the form.</summary>
        internal async Task LoadRecordsAsync()
        {
            var request = _loads.Next();
            lstRecords.IsLoading = true;
            try
            {
                var records = await _maintenance.GetForCarAsync(_session, _carId);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownRecords = records;
                _binding = true;
                try
                {
                    lstRecords.ReplaceItems(records.Select(r => new MaintenanceItem(r)));
                    lstRecords.ClearSelected();
                    var index = Selected is { } selected ? records.ToList().FindIndex(r => r.Id == selected.Id) : -1;
                    if (index >= 0)
                    {
                        lstRecords.SelectedIndex = index;
                    }
                }
                finally
                {
                    _binding = false;
                }
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    lstRecords.IsLoading = false;
                }
            }
        }

        /// <summary>Puts a record in the form, as a click on its row does.</summary>
        internal void SelectRecord(MaintenanceRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            Selected = record;
            cmbKind.SelectedIndex = record.Kind == MaintenanceKind.Damage ? 1 : 0;
            dtpStart.Value = record.StartDate.ToDateTime(TimeOnly.MinValue);
            dtpEnd.Value = record.EndDate.ToDateTime(TimeOnly.MinValue);
            txtDescription.Text = record.Description;
            txtCost.Text = record.Cost == 0m ? string.Empty : DisplayFormat.MoneyInput(record.Cost);
            var active = record.Status == MaintenanceStatus.Active;
            btnSave.Enabled = active;
            btnCancelRecord.Enabled = active;
            lblSelection.Text = MaintenanceItem.Describe(record.Kind) + " kaydı";
            lblSelectionDetail.Text = string.Create(CultureInfo.CurrentCulture, $"Kayıt no {record.Id} · {MaintenanceItem.StatusOf(record)}");
        }

        /// <summary>Adds the entered record, or saves the selected one with its loaded version; ignored while a write runs.</summary>
        internal Task<bool> SaveAsync() => WriteAsync(async () =>
        {
            var input = ReadInput();
            if (Selected is { } record)
            {
                await _maintenance.UpdateAsync(_session, record.Id, record.Version, input);
                await ReloadAndSelectAsync(record.Id);
                Prompts.Inform("Bakım kaydı güncellendi.");
            }
            else
            {
                var id = await _maintenance.AddAsync(_session, _carId, input);
                await ReloadAndSelectAsync(id);
                Prompts.Inform("Bakım kaydı eklendi. Araç bu günlerde kiralanamaz.");
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
            await ReloadAndSelectAsync(record.Id);
            Prompts.Inform("Bakım kaydı iptal edildi.");
        });

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
                    catch (BusinessRuleException ex) when (ex.IsStale)
                    {
                        // Changed or cancelled meanwhile: show the records as they are now. Any other
                        // refusal (a typo, days a rental holds) keeps what the admin typed.
                        await ReloadAndSelectAsync(Selected?.Id);
                        throw;
                    }
                },
                btnNew,
                btnSave,
                btnCancelRecord,
                lstRecords);
            if (ran && !IsDisposed)
            {
                // The buttons follow the record the write left in the form, not the state before it.
                var active = Selected is null || Selected.Status == MaintenanceStatus.Active;
                btnSave.Enabled = active;
                btnCancelRecord.Enabled = Selected is not null && active;
            }

            return ran;
        }

        private async Task ReloadAndSelectAsync(int? recordId)
        {
            await LoadRecordsAsync();
            if (recordId is { } id && ShownRecords.FirstOrDefault(r => r.Id == id) is { } record)
            {
                SelectRecord(record);
                _binding = true;
                try
                {
                    lstRecords.SelectedIndex = ShownRecords.ToList().IndexOf(record);
                }
                finally
                {
                    _binding = false;
                }
            }
            else
            {
                ClearForm();
            }
        }

        private MaintenanceInput ReadInput() => new(
            (cmbKind.SelectedItem as KindChoice)?.Kind ?? MaintenanceKind.Maintenance,
            txtDescription.Text,
            DateOnly.FromDateTime(dtpStart.Value),
            DateOnly.FromDateTime(dtpEnd.Value),
            txtCost.Text);

        private void ClearForm()
        {
            Selected = null;
            _binding = true;
            try
            {
                lstRecords.ClearSelected();
            }
            finally
            {
                _binding = false;
            }

            var today = DateTime.Today;
            cmbKind.SelectedIndex = 0;
            dtpStart.Value = today;
            dtpEnd.Value = today.AddDays(1);
            txtDescription.Clear();
            txtCost.Clear();
            btnSave.Enabled = true;
            btnCancelRecord.Enabled = false;
            lblSelection.Text = "Yeni kayıt";
            lblSelectionDetail.Text = "Günleri, türü ve açıklamayı girip \"Kaydet\" ile ekleyin.";
        }

        private void BuildLayout()
        {
            Name = nameof(MaintenanceForm);
            lstRecords.Describe = item => item is MaintenanceItem record ? record.Row() : new ListRow(item.ToString() ?? string.Empty);
            var recordsCard = FormLayout.Card("recordsCard", "Kayıtlar", lstRecords);

            var badge = new GlyphBadge(Glyphs.Repair, Sizes.HeaderBadge, 14F) { Name = "selectionBadge", Margin = new Padding(0, 0, Spacing.M, 0) };
            lblSelection.Font = Theme.Fonts.Subtitle;
            lblSelectionDetail.Name = nameof(lblSelectionDetail);
            var selectionText = FormLayout.Stack("selectionText", lblSelection, lblSelectionDetail);
            var selection = FormLayout.Bar("selectionRow", (badge, false), (selectionText, true));
            selectionText.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            selection.Margin = new Padding(0, 0, 0, Spacing.M);

            var start = FormLayout.Date(dtpStart);
            var end = FormLayout.Date(dtpEnd);
            var kindRow = FormLayout.FieldRow("kindRow", (FormLayout.Field("&Tür", cmbKind), 0), (FormLayout.Field("&Maliyet (TL, isteğe bağlı)", FormLayout.Input(txtCost)), 100));
            var days = FormLayout.FieldRow(
                "daysRow",
                (FormLayout.Field("&Başlangıç günü", start), 50),
                (FormLayout.Field("Araç &hazır olacağı gün", end), 50));
            var description = FormLayout.Field("Açıkla&ma", FormLayout.Input(txtDescription, Sizes.NoteHeight));
            description.Margin = new Padding(0, 0, 0, Spacing.M);
            btnNew.Margin = btnSave.Margin = new Padding(0, 0, Spacing.S, 0);
            btnCancelRecord.Margin = Padding.Empty;
            var actions = FormLayout.Bar("recordActions", (btnNew, false), (btnSave, false), (FormLayout.Spacer("recordActionsSpacer"), true), (btnCancelRecord, false));
            var recordCard = FormLayout.Card("recordCard", "Kayıt", FormLayout.Stack("recordForm", selection, kindRow, days, description, actions), fill: false);
            recordCard.Margin = Padding.Empty;

            Content.Controls.Add(FormLayout.Columns("layout", recordsCard, 48, FormLayout.Page("formColumn", (recordCard, false))));

            CancelButton = btnClose;
            SetFooterNote("Aktif kayıtların günlerinde araç kiralanamaz; kayıtlar silinmez, iptal edilir.");
            SetFooterButtons(btnClose);
            FinishLayout();
        }

        private sealed record KindChoice(MaintenanceKind Kind)
        {
            public override string ToString() => MaintenanceItem.Describe(Kind);
        }
    }

    /// <summary>A record in the maintenance list: kind and days first, the description and cost below, its state as a pill.</summary>
    internal sealed record MaintenanceItem(MaintenanceRecord Record)
    {
        public static string Describe(MaintenanceKind kind) => kind == MaintenanceKind.Damage ? "Hasar onarımı" : "Bakım";

        public static string StatusOf(MaintenanceRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            return record.Status == MaintenanceStatus.Active ? "Aktif" : "İptal edildi";
        }

        public ListRow Row() => new(
            $"{Describe(Record.Kind)} · {DisplayFormat.Date(Record.StartDate)} → {DisplayFormat.Date(Record.EndDate)}",
            Record.Cost == 0m ? Record.Description : Record.Description + " · " + DisplayFormat.Money(Record.Cost),
            RowTone.Neutral,
            StatusOf(Record),
            Record.Status == MaintenanceStatus.Active ? RowTone.Accent : RowTone.Warning);

        public override string ToString() => $"{Describe(Record.Kind)} {DisplayFormat.Date(Record.StartDate)} - {DisplayFormat.Date(Record.EndDate)}: {Record.Description}";
    }
}
