using System.Globalization;
using System.Text;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>
    /// Booking of one car: choose the pick-up day and the number of days, see the total and the days that
    /// are already taken, and confirm. Closes with <see cref="DialogResult.OK"/> when the rental was stored.
    /// </summary>
    public partial class RentalForm : Form
    {
        private readonly ICarService _cars;
        private readonly IRentalService _rentals;
        private readonly UserSession _session;
        private readonly int _carId;
        private readonly ExclusiveOperation _rent = new();
        private int _days;

        public RentalForm(ICarService cars, IRentalService rentals, IFormFactory forms, UserSession session, int carId)
        {
            ArgumentNullException.ThrowIfNull(forms);
            _forms = forms;
            ArgumentNullException.ThrowIfNull(cars);
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(session);

            _cars = cars;
            _rentals = rentals;
            _session = session;
            _carId = carId;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            txtDays.AcceptDigitsOnly();
            var policy = _rentals.Policy;
            var today = _rentals.Today.ToDateTime(TimeOnly.MinValue);
            dtpStart.MinDate = today;
            dtpStart.MaxDate = today.AddDays(policy.MaxAdvanceDays);
            dtpStart.Value = today;
            _days = policy.MinRentalDays;
            txtDays.Text = _days.ToString(CultureInfo.InvariantCulture);
            btnConfirm.Enabled = false;
            _rent.HoldClosingOf(this);

            var calendar = new Button { Name = "btnCalendar", Text = "Müsaitlik Takvimi", AutoSize = true, Location = new Point(btnBack.Right + 16, btnBack.Top) };
            calendar.Click += (_, _) =>
            {
                var title = Car is { } car ? car.Brand + " " + car.Model : AppText.Title;
                using var form = _forms.Create<AvailabilityCalendarForm>(_session, _carId, title);
                form.ShowDialog(this);
            };
            Controls.Add(calendar);
        }

        private readonly IFormFactory _forms;

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The car as it was loaded; its price is the one the customer confirms.</summary>
        internal Car? Car { get; private set; }

        internal int Days => _days;

        internal DateOnly StartDate => DateOnly.FromDateTime(dtpStart.Value);

        /// <summary>The days already taken, as loaded with the car.</summary>
        internal IReadOnlyList<BookedRange> Booked { get; private set; } = [];

        internal async Task LoadCarAsync()
        {
            var car = await _cars.GetAsync(_session, _carId);
            var booked = await _rentals.GetBookedRangesAsync(_session, _carId);
            if (IsDisposed)
            {
                return;
            }

            Car = car;
            Booked = booked;
            lblBrand.Text = car.Brand;
            lblModel.Text = car.Model;
            lblYear.Text = car.Year.ToString(CultureInfo.InvariantCulture);
            lblTransmission.Text = car.Transmission;
            lblFuelType.Text = car.FuelType;
            lblColor.Text = car.Color;
            lblDailyPrice.Text = DisplayFormat.Money(car.DailyPrice);
            picCar.ShowCarPicture(car.ImageUrl);
            lblBooked.Text = DescribeBooked(booked);
            btnConfirm.Enabled = true;
            UpdateTotal();
        }

        /// <summary>
        /// Shows the price and days, asks for confirmation and books the car; ignored while a previous
        /// attempt is still running. Returns whether the rental was stored.
        /// </summary>
        internal async Task<bool> RentAsync()
        {
            if (Car is not { } car)
            {
                return false;
            }

            ApplyTypedDays();
            var rented = false;
            await this.RunExclusiveAsync(
                _rent,
                async () =>
                {
                    var quote = _rentals.Quote(car.Id, car.DailyPrice, StartDate, _days);
                    var question =
                        $"Toplam Tutar: {DisplayFormat.Money(quote.Total)}{(quote.Discounted ? " (uzun kiralama indirimi dahil)" : string.Empty)}\n"
                        + $"Kiralama Başlangıç Tarihi: {DisplayFormat.Date(quote.StartDate)}\n"
                        + $"Kiralama Bitiş (İade) Tarihi: {DisplayFormat.Date(quote.EndDate)}\n\n"
                        + "Kiralamayı onaylıyor musunuz?";
                    if (!Prompts.Confirm(question))
                    {
                        Prompts.Warn("Kiralama iptal edildi.");
                        return;
                    }

                    try
                    {
                        await _rentals.RentAsync(_session, quote);
                    }
                    catch (BusinessRuleException)
                    {
                        // The days or the price may have changed meanwhile: show them as they are now.
                        await ReloadCarAfterRefusalAsync();
                        throw;
                    }

                    rented = true;
                },
                btnConfirm,
                btnIncrease,
                btnDecrease,
                dtpStart);
            return rented;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadCarAsync);
        }

        /// <summary>The taken days as the customer reads them: pick-up day to return day.</summary>
        internal static string DescribeBooked(IReadOnlyList<BookedRange> booked)
        {
            if (booked.Count == 0)
            {
                return "Bu araç için dolu tarih yok.";
            }

            var text = new StringBuilder("Dolu tarihler (alış - iade):");
            foreach (var range in booked.Take(4))
            {
                text.Append('\n').Append(DisplayFormat.Date(range.StartDate)).Append(" - ").Append(DisplayFormat.Date(range.EndDate));
            }

            if (booked.Count > 4)
            {
                text.Append("\n...");
            }

            return text.ToString();
        }

        /// <summary>
        /// Loads the car and its taken days again after a refused booking. When the car itself is gone
        /// or closed, the booking's own refusal is the message the customer sees.
        /// </summary>
        private async Task ReloadCarAfterRefusalAsync()
        {
            try
            {
                await LoadCarAsync();
            }
            catch (BusinessRuleException)
            {
                // The refusal that follows already says why the car cannot be booked.
            }
        }

        private void UpdateTotal()
        {
            if (Car is { } car)
            {
                try
                {
                    var quote = _rentals.Quote(car.Id, car.DailyPrice, StartDate, _days);
                    lblTotal.Text = "Toplam Tutar: " + DisplayFormat.Money(quote.Total);
                }
                catch (BusinessRuleException ex)
                {
                    lblTotal.Text = ex.Message;
                }
            }
        }

        /// <summary>Takes the typed number of days, kept within the settings.</summary>
        private void ApplyTypedDays()
        {
            var policy = _rentals.Policy;
            _days = int.TryParse(txtDays.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var typed)
                ? Math.Clamp(typed, policy.MinRentalDays, policy.MaxRentalDays)
                : policy.MinRentalDays;
            txtDays.Text = _days.ToString(CultureInfo.InvariantCulture);
            UpdateTotal();
        }

        private void SetDays(int days)
        {
            _days = Math.Clamp(days, _rentals.Policy.MinRentalDays, _rentals.Policy.MaxRentalDays);
            txtDays.Text = _days.ToString(CultureInfo.InvariantCulture);
            UpdateTotal();
        }

        private void btnIncrease_Click(object? sender, EventArgs e)
        {
            ApplyTypedDays();
            SetDays(_days + 1);
        }

        private void btnDecrease_Click(object? sender, EventArgs e)
        {
            ApplyTypedDays();
            SetDays(_days - 1);
        }

        private void txtDays_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplyTypedDays();
            }
        }

        private void txtDays_Leave(object? sender, EventArgs e) => ApplyTypedDays();

        private void dtpStart_ValueChanged(object? sender, EventArgs e) => UpdateTotal();

        private void btnBack_Click(object? sender, EventArgs e) => Close();

        private async void btnConfirm_Click(object? sender, EventArgs e)
        {
            if (await RentAsync())
            {
                Prompts.Inform("Kiralama işlemi onaylandı ve kaydedildi. Anasayfaya yönlendiriliyorsunuz.");
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
