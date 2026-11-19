using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>The admin's car management: list, add, change and delete cars, and the other admin screens.</summary>
    public sealed class AdminPanel : AppDialog, ISignOutPanel
    {
        private readonly ICarService _cars;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private readonly Dictionary<string, Control> _fields;
        private bool _binding;

        private readonly Label lblGreeting = new() { Name = nameof(lblGreeting), AutoSize = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleRight };
        private readonly Label lblCarCount = FormLayout.Hint(string.Empty);
        private readonly ThemedButton btnRefresh = FormLayout.Button(nameof(btnRefresh), "Yenile", ButtonKind.Subtle, Glyphs.Refresh);
        private readonly ThemedListBox lstCars = new() { Name = nameof(lstCars), AccessibleName = "Araçlar", EmptyText = "Henüz araç yok. Sağdaki formdan ilk aracı ekleyin." };
        private readonly VehiclePicture picCar = new() { Name = nameof(picCar), Size = new Size(132, 80) };
        private readonly Label lblSelection = FormLayout.Text(nameof(lblSelection));
        private readonly Label lblSelectionDetail = FormLayout.Hint(string.Empty);
        private readonly TextBox txtBrand = new() { Name = nameof(txtBrand), MaxLength = InputRules.BrandMaxLength };
        private readonly TextBox txtModel = new() { Name = nameof(txtModel), MaxLength = InputRules.ModelMaxLength };
        private readonly ThemedComboBox cmbYear = FormLayout.Picker(nameof(cmbYear), 96);
        private readonly ThemedComboBox cmbTransmission = FormLayout.Picker(nameof(cmbTransmission), 120);
        private readonly ThemedComboBox cmbFuelType = FormLayout.Picker(nameof(cmbFuelType), 120);
        private readonly TextBox txtMileage = new() { Name = nameof(txtMileage), MaxLength = 7 };
        private readonly TextBox txtColor = new() { Name = nameof(txtColor), MaxLength = InputRules.ColorMaxLength };
        private readonly TextBox txtPlate = new() { Name = nameof(txtPlate), MaxLength = InputRules.PlateMaxLength, CharacterCasing = CharacterCasing.Upper };
        private readonly TextBox txtDailyPrice = new() { Name = nameof(txtDailyPrice), MaxLength = 12 };
        private readonly TextBox txtImageUrl = new() { Name = nameof(txtImageUrl), MaxLength = InputRules.ImageUrlMaxLength, PlaceholderText = "https://… (isteğe bağlı)" };
        private readonly ThemedCheckBox chkListed = new() { Name = nameof(chkListed), Text = "Kiralamaya açık", Checked = true };
        private readonly ThemedButton btnAddCar = FormLayout.Button(nameof(btnAddCar), "Araç Ekle", ButtonKind.Secondary, Glyphs.Add);
        private readonly ThemedButton btnUpdateCar = FormLayout.Button(nameof(btnUpdateCar), "Aracı Güncelle", ButtonKind.Primary, Glyphs.Save);
        private readonly ThemedButton btnDeleteCar = FormLayout.Button(nameof(btnDeleteCar), "Aracı Sil", ButtonKind.Danger, Glyphs.Delete);
        private readonly ThemedButton btnCalendar = FormLayout.Button(nameof(btnCalendar), "Müsaitlik Takvimi", ButtonKind.Subtle, Glyphs.Calendar);
        private readonly ThemedButton btnMaintenance = FormLayout.Button(nameof(btnMaintenance), "Bakım / Hasar Kayıtları", ButtonKind.Subtle, Glyphs.Repair);
        private readonly ThemedButton btnRentals = FormLayout.Button(nameof(btnRentals), "Kiralamalar", ButtonKind.Secondary, Glyphs.List);
        private readonly ThemedButton btnUsers = FormLayout.Button(nameof(btnUsers), "Hesaplar", ButtonKind.Secondary, Glyphs.People);
        private readonly ThemedButton btnRevenue = FormLayout.Button(nameof(btnRevenue), "Gelir Raporu", ButtonKind.Secondary, Glyphs.Chart);
        private readonly ThemedButton btnSignOut = FormLayout.Button(nameof(btnSignOut), "Oturumu Kapat", ButtonKind.Secondary, Glyphs.SignOut);

        public AdminPanel(ICarService cars, IFormFactory forms, UserSession session)
            : base(
                "Yönetim paneli",
                "Araçları ekleyin, güncelleyin ve silin; kiralamalara, hesaplara ve gelir raporuna buradan geçin.",
                Glyphs.Admin,
                new Size(1180, 700),
                new Size(900, 560))
        {
            ArgumentNullException.ThrowIfNull(cars);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _cars = cars;
            _forms = forms;
            _session = session;

            var years = new List<string>();
            for (var year = DateTime.Today.Year + 1; year >= 1990; year--)
            {
                years.Add(year.ToString(CultureInfo.InvariantCulture));
            }

            _years = years;
            cmbYear.ReplaceItems(years);
            cmbTransmission.ReplaceItems(InputRules.Transmissions);
            cmbFuelType.ReplaceItems(InputRules.FuelTypes);
            txtMileage.AcceptDigitsOnly();
            txtDailyPrice.AcceptMoneyOnly();
            BuildLayout();
            _fields = new Dictionary<string, Control>(StringComparer.Ordinal)
            {
                [nameof(CarInput.Brand)] = txtBrand,
                [nameof(CarInput.Model)] = txtModel,
                [nameof(CarInput.Year)] = cmbYear,
                [nameof(CarInput.Transmission)] = cmbTransmission,
                [nameof(CarInput.FuelType)] = cmbFuelType,
                [nameof(CarInput.Mileage)] = txtMileage,
                [nameof(CarInput.Color)] = txtColor,
                [nameof(CarInput.Plate)] = txtPlate,
                [nameof(CarInput.DailyPrice)] = txtDailyPrice,
                [nameof(CarInput.ImageUrl)] = txtImageUrl,
            };
            ClearForm();
            lblGreeting.Text = AppText.DisplayName(session.UserName) + Environment.NewLine + "Yönetici hesabı";

            // Both need the car in the form, exactly like "Aracı Güncelle".
            btnUpdateCar.EnabledChanged += (_, _) => btnCalendar.Enabled = btnMaintenance.Enabled = btnUpdateCar.Enabled;
            btnCalendar.Enabled = btnMaintenance.Enabled = btnUpdateCar.Enabled;

            Load += async (_, _) => await this.RunAsync(LoadCarsAsync);
            lstCars.SelectedIndexChanged += (_, _) =>
            {
                // Binding new rows is not a choice of the user and changes nothing in the form.
                if (!_binding && lstCars.SelectedItem is CarItem item)
                {
                    SelectCar(item.Car);
                }
            };
            btnRefresh.Click += async (_, _) => await this.RunAsync(LoadCarsAsync);
            btnAddCar.Click += async (_, _) => await AddAsync();
            btnUpdateCar.Click += async (_, _) => await UpdateAsync();
            btnDeleteCar.Click += async (_, _) => await DeleteAsync();
            btnCalendar.Click += (_, _) => OpenForSelectedCar<AvailabilityCalendarForm>();
            btnMaintenance.Click += (_, _) => OpenForSelectedCar<MaintenanceForm>();
            btnRentals.Click += (_, _) => OpenRentals();
            btnUsers.Click += (_, _) => OpenAccounts();
            btnRevenue.Click += (_, _) => OpenRevenue();
            btnSignOut.Click += (_, _) => SignOut();
        }

        private readonly IReadOnlyList<string> _years;

        public bool SignedOut { get; private set; }

        /// <summary>Why the session ended, for the sign-in screen; set when the admin changed their own role or password.</summary>
        public string? SignOutNotice { get; private set; }

        /// <summary>The car whose details are in the form, or <see langword="null"/> when a new one is entered.</summary>
        internal int? SelectedCarId { get; private set; }

        /// <summary>The version of the car in the form as it was loaded; a change sends it back.</summary>
        internal int SelectedCarVersion { get; private set; }

        internal IReadOnlyList<Car> ShownCars { get; private set; } = [];

        /// <summary>The message a refused field shows, or <see langword="null"/>; for tests.</summary>
        internal string? FieldError(string field) => _fields.TryGetValue(field, out var control) ? ErrorOf(control) : null;

        /// <summary>
        /// Loads the car list; only the latest request is shown. Binding the rows is not a choice of the user: it
        /// changes nothing in the form and selects only the car that is already in the form.
        /// </summary>
        internal async Task LoadCarsAsync()
        {
            var request = _loads.Next();
            lstCars.IsLoading = true;
            try
            {
                var cars = await _cars.GetAllAsync(_session);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownCars = cars;
                _binding = true;
                try
                {
                    lstCars.ReplaceItems(cars.Select(c => new CarItem(c)));
                    lstCars.ClearSelected();
                    var index = SelectedCarId is { } id ? cars.ToList().FindIndex(c => c.Id == id) : -1;
                    if (index >= 0)
                    {
                        lstCars.SelectedIndex = index;
                    }
                }
                finally
                {
                    _binding = false;
                }

                lblCarCount.Text = string.Create(CultureInfo.CurrentCulture, $"{cars.Count} araç, {cars.Count(c => c.IsListed)} tanesi kiralamaya açık");
                btnUpdateCar.Enabled = SelectedCarId is not null;
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    lstCars.IsLoading = false;
                }
            }
        }

        /// <summary>Shows a car's details in the form, as a click on its row does.</summary>
        internal void SelectCar(Car car)
        {
            ArgumentNullException.ThrowIfNull(car);

            ClearFieldErrors();
            SelectedCarId = car.Id;
            SelectedCarVersion = car.Version;
            txtBrand.Text = car.Brand;
            txtModel.Text = car.Model;
            ShowChoice(cmbYear, _years, car.Year.ToString(CultureInfo.InvariantCulture));
            ShowChoice(cmbTransmission, InputRules.Transmissions, car.Transmission);
            ShowChoice(cmbFuelType, InputRules.FuelTypes, car.FuelType);
            txtMileage.Text = car.Mileage.ToString(CultureInfo.InvariantCulture);
            txtColor.Text = car.Color;
            txtPlate.Text = car.Plate;
            txtDailyPrice.Text = DisplayFormat.MoneyInput(car.DailyPrice);
            chkListed.Checked = car.IsListed;
            txtImageUrl.Text = car.ImageUrl ?? string.Empty;
            picCar.ShowCar(car.Model, car.Color, car.ImageUrl, car.Brand + " " + car.Model);
            lblSelection.Text = car.Brand + " " + car.Model;
            lblSelectionDetail.Text = string.Create(CultureInfo.CurrentCulture, $"Araç no {car.Id} · {CarItem.Describe(car)}");
            btnUpdateCar.Enabled = true;
        }

        /// <summary>
        /// Adds the entered car. While a car is selected the first click only clears the form, as in the
        /// original panel. Ignored while another write is running.
        /// </summary>
        internal async Task AddAsync()
        {
            if (SelectedCarId is not null)
            {
                ClearForm();
                Prompts.Inform("Bir kayıt seçili durumda. Yeni araç eklemek için seçimler kaldırıldı. Şimdi ekleyebilirsiniz.");
                return;
            }

            await WriteAsync(async () =>
            {
                await _cars.AddAsync(_session, ReadInput());
                ClearForm();
                await LoadCarsAsync();
                Prompts.Inform("Araç başarıyla eklendi.");
            });
        }

        internal async Task UpdateAsync()
        {
            if (SelectedCarId is not { } id)
            {
                Prompts.Warn("Lütfen güncellemek için bir araç seçin.");
                return;
            }

            var version = SelectedCarVersion;
            await WriteAsync(async () =>
            {
                try
                {
                    await _cars.UpdateAsync(_session, id, version, ReadInput());
                }
                catch (BusinessRuleException ex) when (ex.IsStale)
                {
                    // Changed or deleted elsewhere: the form shows the car as it is now.
                    await LoadCarsAsync();
                    if (ShownCars.FirstOrDefault(c => c.Id == id) is { } current)
                    {
                        SelectCar(current);
                    }
                    else
                    {
                        ClearForm();
                    }

                    throw;
                }

                await LoadCarsAsync();
                if (ShownCars.FirstOrDefault(c => c.Id == id) is { } updated)
                {
                    SelectCar(updated);
                }

                Prompts.Inform("Araç başarıyla güncellendi!");
            });
        }

        internal async Task DeleteAsync()
        {
            if (SelectedCarId is not { } id)
            {
                Prompts.Warn("Lütfen silmek için bir araç seçin.");
                return;
            }

            await WriteAsync(async () =>
            {
                if (!Prompts.Confirm("Seçili aracı silmek istediğinize emin misiniz?"))
                {
                    return;
                }

                try
                {
                    await _cars.DeleteAsync(_session, id);
                }
                catch (BusinessRuleException)
                {
                    await ReloadAfterRefusalAsync(id);
                    throw;
                }

                ClearForm();
                await LoadCarsAsync();
                Prompts.Inform("Araç başarıyla silindi.");
            });
        }

        /// <summary>Closes the panel and returns to the sign-in screen.</summary>
        internal void SignOut(string? notice = null)
        {
            SignedOut = true;
            SignOutNotice = notice;
            Close();
        }

        /// <summary>
        /// After a refused change or deletion the car may have been changed or deleted elsewhere: the list
        /// is loaded again, and a car that no longer exists leaves the form.
        /// </summary>
        private async Task ReloadAfterRefusalAsync(int carId)
        {
            await LoadCarsAsync();
            if (!ShownCars.Any(c => c.Id == carId))
            {
                ClearForm();
            }
        }

        /// <summary>
        /// Runs a car write exclusively; refused fields are marked next to their inputs. The update button
        /// follows the selection the write left behind, not the state before it.
        /// </summary>
        private async Task<bool> WriteAsync(Func<Task> write)
        {
            var ran = await this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    ClearFieldErrors();
                    try
                    {
                        await write();
                    }
                    catch (BusinessRuleException ex) when (ex.FieldErrors.Count > 0)
                    {
                        MarkFields(ex.FieldErrors);
                        _write.FocusAfterUnlock(FocusTarget(ex.FieldErrors.Keys.First()));
                        throw;
                    }
                },
                btnAddCar,
                btnUpdateCar,
                btnDeleteCar,
                btnRefresh);
            if (ran && !IsDisposed)
            {
                btnUpdateCar.Enabled = SelectedCarId is not null;
            }

            return ran;
        }

        private CarInput ReadInput() => new(
            txtBrand.Text,
            txtModel.Text,
            cmbYear.SelectedItem as string ?? string.Empty,
            cmbTransmission.SelectedItem as string,
            cmbFuelType.SelectedItem as string,
            txtMileage.Text,
            txtColor.Text,
            txtPlate.Text,
            txtDailyPrice.Text,
            txtImageUrl.Text,
            chkListed.Checked);

        private void MarkFields(IReadOnlyDictionary<string, string> errors)
        {
            foreach (var (field, message) in errors)
            {
                if (_fields.TryGetValue(field, out var control))
                {
                    SetError(control, message);
                }
            }
        }

        private void ClearFieldErrors()
        {
            foreach (var control in _fields.Values)
            {
                SetError(control, null);
            }
        }

        private Control? FocusTarget(string field) => _fields.GetValueOrDefault(field);

        private static void SetError(Control control, string? message)
        {
            switch (control)
            {
                case TextBox { Parent: TextField field }:
                    field.Error = message;
                    break;
                case ThemedComboBox combo:
                    combo.Error = message;
                    break;
            }
        }

        private static string? ErrorOf(Control control) => control switch
        {
            TextBox { Parent: TextField field } => field.Error,
            ThemedComboBox combo => combo.Error,
            _ => null,
        };

        /// <summary>
        /// Lists the allowed values and selects <paramref name="value"/>. A value of the original data that
        /// is in neither list is listed too, so the car can be saved without changing it; it disappears
        /// with the next car.
        /// </summary>
        private static void ShowChoice(ComboBox box, IReadOnlyList<string> allowed, string? value)
        {
            IReadOnlyList<string> items = value is null || allowed.Contains(value, StringComparer.Ordinal) ? allowed : [.. allowed, value];
            if (!box.Items.Cast<string>().SequenceEqual(items, StringComparer.Ordinal))
            {
                box.ReplaceItems(items);
            }

            box.SelectedItem = value;
        }

        private void ClearForm()
        {
            SelectedCarId = null;
            SelectedCarVersion = 0;
            _binding = true;
            try
            {
                lstCars.ClearSelected();
            }
            finally
            {
                _binding = false;
            }

            ClearFieldErrors();
            foreach (var box in new[] { txtBrand, txtModel, txtMileage, txtColor, txtPlate, txtDailyPrice, txtImageUrl })
            {
                box.Clear();
            }

            ShowChoice(cmbYear, _years, null);
            ShowChoice(cmbTransmission, InputRules.Transmissions, null);
            ShowChoice(cmbFuelType, InputRules.FuelTypes, null);
            chkListed.Checked = true;
            picCar.ShowCar(null, null, null, "Yeni araç");
            lblSelection.Text = "Yeni araç";
            lblSelectionDetail.Text = "Bilgileri girip \"Araç Ekle\" ile kaydedin.";
            btnUpdateCar.Enabled = false;
        }

        private void OpenAccounts()
        {
            bool sessionEnded;
            using (var panel = _forms.Create<UserManagementPanel>(_session))
            {
                panel.ShowDialog(this);
                sessionEnded = panel.SessionEnded;
            }

            if (sessionEnded)
            {
                // The admin changed their own role or password: back to the sign-in screen.
                SignOut(UserManagementPanel.OwnSessionEndedMessage);
            }
        }

        private void OpenRentals()
        {
            using var panel = _forms.Create<RentedCarsPanel>(_session);
            panel.ShowDialog(this);
        }

        private void OpenRevenue()
        {
            using var report = _forms.Create<RevenueReportForm>(_session);
            report.ShowDialog(this);
        }

        /// <summary>Opens a form of the car in the form (calendar or maintenance log); nothing without a car.</summary>
        private void OpenForSelectedCar<T>()
            where T : Form
        {
            if (SelectedCarId is not { } id || ShownCars.FirstOrDefault(c => c.Id == id) is not { } car)
            {
                return;
            }

            using var form = _forms.Create<T>(_session, id, car.Brand + " " + car.Model + " (" + car.Plate + ")");
            form.ShowDialog(this);
        }

        private void BuildLayout()
        {
            Name = nameof(AdminPanel);
            var p = Theme.Current;
            lblGreeting.Font = Theme.Fonts.Body;
            lblGreeting.ForeColor = p.TextSecondary;
            SetHeaderAside(lblGreeting);

            // The cars: a count, the refresh and the list.
            lblCarCount.Name = nameof(lblCarCount);
            btnRefresh.Margin = Padding.Empty;
            var toolbar = FormLayout.Bar("carToolbar", (lblCarCount, true), (btnRefresh, false));
            toolbar.Margin = new Padding(0, 0, 0, Spacing.S);
            lstCars.Describe = item => item is CarItem car ? car.Row() : new ListRow(item.ToString() ?? string.Empty);
            lstCars.LoadingText = "Araçlar yükleniyor…";
            var carsCard = FormLayout.Card("carsCard", "Araçlar", FormLayout.Page("carsPage", (toolbar, false), (lstCars, true)));

            // The car in the form: its picture and name, its fields and the actions on it.
            lblSelection.Font = Theme.Fonts.Subtitle;
            lblSelectionDetail.Name = nameof(lblSelectionDetail);
            picCar.Margin = new Padding(0, 0, Spacing.M, 0);
            var selectionText = FormLayout.Stack("selectionText", lblSelection, lblSelectionDetail);
            var selection = FormLayout.Bar("selectionRow", (picCar, false), (selectionText, true));
            selectionText.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            selection.Margin = new Padding(0, 0, 0, Spacing.M);
            var row1 = FormLayout.FieldRow(
                "carRow1",
                (FormLayout.Field("&Marka", FormLayout.Input(txtBrand)), 50),
                (FormLayout.Field("M&odel", FormLayout.Input(txtModel)), 50));
            var row2 = FormLayout.FieldRow(
                "carRow2",
                (FormLayout.Field("&Yıl", cmbYear), 0),
                (FormLayout.Field("&Vites", cmbTransmission), 50),
                (FormLayout.Field("Ya&kıt", cmbFuelType), 50));
            var row3 = FormLayout.FieldRow(
                "carRow3",
                (FormLayout.Field("K&m", FormLayout.Input(txtMileage)), 30),
                (FormLayout.Field("&Renk", FormLayout.Input(txtColor)), 35),
                (FormLayout.Field("&Plaka", FormLayout.Input(txtPlate)), 35));
            var row4 = FormLayout.FieldRow(
                "carRow4",
                (FormLayout.Field("&Günlük ücret (TL)", FormLayout.Input(txtDailyPrice)), 40),
                (FormLayout.Field("Resim &adresi", FormLayout.Input(txtImageUrl)), 60));
            chkListed.Margin = new Padding(0, 0, 0, Spacing.M);
            chkListed.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            btnAddCar.Margin = btnUpdateCar.Margin = new Padding(0, 0, Spacing.S, 0);
            btnDeleteCar.Margin = Padding.Empty;
            var actions = FormLayout.Bar("carActions", (btnAddCar, false), (btnUpdateCar, false), (FormLayout.Spacer("carActionsSpacer"), true), (btnDeleteCar, false));
            btnCalendar.Margin = new Padding(0, 0, Spacing.S, 0);
            btnMaintenance.Margin = Padding.Empty;
            var operations = FormLayout.Bar("pnlOperations", (btnCalendar, false), (btnMaintenance, false), (FormLayout.Spacer("operationsSpacer"), true));
            operations.Margin = new Padding(0, Spacing.M, 0, 0);
            var carForm = FormLayout.Stack("carForm", selection, row1, row2, row3, row4, chkListed, actions, operations);
            chkListed.Dock = DockStyle.None;
            var carCard = FormLayout.Card("carCard", "Araç bilgileri", carForm, fill: false);
            carCard.Margin = Padding.Empty;

            Content.Controls.Add(FormLayout.Columns("layout", carsCard, 48, FormLayout.Page("formColumn", (carCard, false))));

            CancelButton = btnSignOut;
            SetFooterNote("Geçmişi olan bir aracı silmek yerine kiralamaya kapatın.");
            SetFooterButtons(btnRentals, btnUsers, btnRevenue, btnSignOut);
            FinishLayout();
        }
    }
}
