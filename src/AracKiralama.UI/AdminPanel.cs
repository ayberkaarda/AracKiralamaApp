using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>The admin's car management: list, add, change and delete cars, and the other admin panels.</summary>
    public partial class AdminPanel : Form, ISignOutPanel
    {
        private readonly ICarService _cars;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private bool _binding;

        public AdminPanel(ICarService cars, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(cars);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _cars = cars;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            for (var year = DateTime.Today.Year + 1; year >= 1990; year--)
            {
                cmbYear.Items.Add(year.ToString(CultureInfo.InvariantCulture));
            }

            cmbTransmission.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTransmission.ReplaceItems(InputRules.Transmissions);
            cmbFuelType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFuelType.ReplaceItems(InputRules.FuelTypes);
            txtBrand.MaxLength = InputRules.BrandMaxLength;
            txtModel.MaxLength = InputRules.ModelMaxLength;
            txtColor.MaxLength = InputRules.ColorMaxLength;
            txtPlate.MaxLength = InputRules.PlateMaxLength;
            txtPlate.CharacterCasing = CharacterCasing.Upper;
            txtImageUrl.MaxLength = InputRules.ImageUrlMaxLength;
            txtMileage.MaxLength = 7;
            txtMileage.AcceptDigitsOnly();
            txtDailyPrice.MaxLength = 12;
            txtDailyPrice.AcceptMoneyOnly();
            dataGridView1.UseReadOnlyList();
            btnUpdateCar.Enabled = false;
        }

        public bool SignedOut { get; private set; }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The car whose details are in the form, or <see langword="null"/> when a new one is entered.</summary>
        internal int? SelectedCarId { get; private set; }

        internal IReadOnlyList<Car> ShownCars { get; private set; } = [];

        /// <summary>Loads the car grid; only the latest request is shown. Binding the rows selects nothing.</summary>
        internal async Task LoadCarsAsync()
        {
            var request = _loads.Next();
            var cars = await _cars.GetAllAsync(_session);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownCars = cars;
            _binding = true;
            try
            {
                dataGridView1.DataSource = cars.Select(c => new CarRow(c)).ToList();
                dataGridView1.ClearSelection();
            }
            finally
            {
                _binding = false;
            }

            btnUpdateCar.Enabled = SelectedCarId is not null;
        }

        /// <summary>Shows a car's details in the form, as a click on its row does.</summary>
        internal void SelectCar(Car car)
        {
            ArgumentNullException.ThrowIfNull(car);

            errorProvider1.Clear();
            SelectedCarId = car.Id;
            txtId.Text = car.Id.ToString(CultureInfo.InvariantCulture);
            txtBrand.Text = car.Brand;
            txtModel.Text = car.Model;
            cmbYear.Text = car.Year.ToString(CultureInfo.InvariantCulture);
            cmbTransmission.SelectedItem = InputRules.Transmissions.FirstOrDefault(t => t == car.Transmission);
            cmbFuelType.SelectedItem = InputRules.FuelTypes.FirstOrDefault(f => f == car.FuelType);
            txtMileage.Text = car.Mileage.ToString(CultureInfo.InvariantCulture);
            txtColor.Text = car.Color;
            txtPlate.Text = car.Plate;
            txtDailyPrice.Text = DisplayFormat.MoneyInput(car.DailyPrice);
            txtStatus.Text = CarRow.Describe(car);
            chkListed.Checked = car.IsListed;
            txtImageUrl.Text = car.ImageUrl ?? string.Empty;
            picCar.ShowCarPicture(car.ImageUrl);
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
                Prompts.Inform("Araç başarıyla eklendi.");
                ClearForm();
                await LoadCarsAsync();
            });
        }

        internal async Task UpdateAsync()
        {
            if (SelectedCarId is not { } id)
            {
                Prompts.Warn("Lütfen güncellemek için bir araç seçin.");
                return;
            }

            await WriteAsync(async () =>
            {
                await _cars.UpdateAsync(_session, id, ReadInput());
                Prompts.Inform("Araç başarıyla güncellendi!");
                await LoadCarsAsync();
                if (ShownCars.FirstOrDefault(c => c.Id == id) is { } updated)
                {
                    SelectCar(updated);
                }
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

                await _cars.DeleteAsync(_session, id);
                Prompts.Inform("Araç başarıyla silindi.");
                ClearForm();
                await LoadCarsAsync();
            });
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadCarsAsync);
        }

        /// <summary>Runs a car write exclusively; invalid fields are marked as the original form marked empty ones.</summary>
        private Task<bool> WriteAsync(Func<Task> write) =>
            this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    errorProvider1.Clear();
                    try
                    {
                        await write();
                    }
                    catch (BusinessRuleException ex) when (ex.FieldErrors.Count > 0)
                    {
                        MarkFields(ex.FieldErrors);
                        _write.FocusAfterUnlock(FieldControl(ex.FieldErrors.Keys.First()));
                        throw;
                    }
                },
                btnAddCar,
                btnUpdateCar,
                btnDeleteCar,
                btnRefresh);

        private CarInput ReadInput() => new(
            txtBrand.Text,
            txtModel.Text,
            cmbYear.Text,
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
                if (FieldControl(field) is { } control)
                {
                    errorProvider1.SetError(control, message);
                }
            }
        }

        private Control? FieldControl(string field) => field switch
        {
            nameof(CarInput.Brand) => txtBrand,
            nameof(CarInput.Model) => txtModel,
            nameof(CarInput.Year) => cmbYear,
            nameof(CarInput.Transmission) => cmbTransmission,
            nameof(CarInput.FuelType) => cmbFuelType,
            nameof(CarInput.Mileage) => txtMileage,
            nameof(CarInput.Color) => txtColor,
            nameof(CarInput.Plate) => txtPlate,
            nameof(CarInput.DailyPrice) => txtDailyPrice,
            nameof(CarInput.ImageUrl) => txtImageUrl,
            _ => null,
        };

        private void ClearForm()
        {
            SelectedCarId = null;
            dataGridView1.ClearSelection();
            errorProvider1.Clear();
            foreach (var box in new[] { txtId, txtBrand, txtModel, txtMileage, txtColor, txtPlate, txtDailyPrice, txtStatus, txtImageUrl })
            {
                box.Clear();
            }

            cmbYear.SelectedIndex = -1;
            cmbYear.Text = string.Empty;
            cmbTransmission.SelectedIndex = -1;
            cmbFuelType.SelectedIndex = -1;
            chkListed.Checked = true;
            picCar.ShowCarPicture(null);
            btnUpdateCar.Enabled = false;
        }

        private async void btnRefresh_Click(object? sender, EventArgs e) => await this.RunAsync(LoadCarsAsync);

        private void btnUsers_Click(object? sender, EventArgs e)
        {
            using var panel = _forms.Create<UserManagementPanel>(_session);
            panel.ShowDialog(this);
        }

        private void btnRentals_Click(object? sender, EventArgs e)
        {
            using var panel = _forms.Create<RentedCarsPanel>(_session);
            panel.ShowDialog(this);
        }

        private void dataGridView1_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dataGridView1.Rows[e.RowIndex].DataBoundItem is CarRow row)
            {
                SelectCar(row.Car);
            }
        }

        private void dataGridView1_SelectionChanged(object? sender, EventArgs e)
        {
            // Binding new rows is not a choice of the user and changes nothing in the form.
            if (!_binding)
            {
                btnUpdateCar.Enabled = SelectedCarId is not null;
            }
        }

        private async void btnAddCar_Click(object? sender, EventArgs e) => await AddAsync();

        private async void btnUpdateCar_Click(object? sender, EventArgs e) => await UpdateAsync();

        private async void btnDeleteCar_Click(object? sender, EventArgs e) => await DeleteAsync();

        private void btnSignOut_Click(object? sender, EventArgs e)
        {
            SignedOut = true;
            Close();
        }
    }
}
