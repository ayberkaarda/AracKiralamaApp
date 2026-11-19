using System.Globalization;
using System.Text;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// Booking of one car: choose the pick-up day and the number of days, see the total and the days that
    /// are already taken, and confirm. Closes with <see cref="DialogResult.OK"/> when the rental was stored;
    /// the car list then shows <see cref="DoneMessage"/>.
    /// </summary>
    public sealed class RentalForm : AppDialog
    {
        private readonly ICarService _cars;
        private readonly IRentalService _rentals;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly int _carId;
        private readonly ExclusiveOperation _rent = new();
        private int _days;
        private bool _quoteRefused;

        private readonly VehiclePicture picCar = new() { Name = nameof(picCar), Height = 170 };
        private readonly HeadingLabel lblCarName = new() { Name = nameof(lblCarName), Text = "Araç yükleniyor…" };
        private readonly PlateChip lblPlate = new() { Name = nameof(lblPlate) };
        private readonly Label lblBrand = Value(nameof(lblBrand));
        private readonly Label lblModel = Value(nameof(lblModel));
        private readonly Label lblYear = Value(nameof(lblYear));
        private readonly Label lblTransmission = Value(nameof(lblTransmission));
        private readonly Label lblFuelType = Value(nameof(lblFuelType));
        private readonly Label lblColor = Value(nameof(lblColor));
        private readonly Label lblDailyPrice = Value(nameof(lblDailyPrice));
        private readonly DatePicker dtpStart = new() { Name = nameof(dtpStart), Format = DateTimePickerFormat.Custom, CustomFormat = "d MMMM yyyy dddd" };
        private readonly ThemedButton btnDecrease = FormLayout.Button(nameof(btnDecrease), "−", ButtonKind.Secondary);
        private readonly TextBox txtDays = new() { Name = nameof(txtDays), MaxLength = 4, TextAlign = HorizontalAlignment.Center };
        private readonly ThemedButton btnIncrease = FormLayout.Button(nameof(btnIncrease), "+", ButtonKind.Secondary);
        private readonly Label lblReturn = Value(nameof(lblReturn));
        private readonly Label lblTotal = Value(nameof(lblTotal));
        private readonly Label lblDiscount = FormLayout.Hint(string.Empty);
        private readonly Label lblBooked = FormLayout.Text(nameof(lblBooked));
        private readonly ThemedButton btnCalendar = FormLayout.Button(nameof(btnCalendar), "Müsaitlik takvimi", ButtonKind.Subtle, Glyphs.Calendar);
        private readonly ThemedButton btnConfirm = FormLayout.Button(nameof(btnConfirm), "Kiralamayı Onayla", ButtonKind.Primary, Glyphs.CheckMark);
        private readonly ThemedButton btnBack = FormLayout.Button(nameof(btnBack), "Geri Dön", ButtonKind.Secondary);

        public RentalForm(ICarService cars, IRentalService rentals, IFormFactory forms, UserSession session, int carId)
            : base("Araç kirala", "Alış gününü ve gün sayısını seçin; tutarı görüp kiralamayı onaylayın.", Glyphs.Car, new Size(900, 600), new Size(720, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(cars);
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _cars = cars;
            _rentals = rentals;
            _forms = forms;
            _session = session;
            _carId = carId;
            BuildLayout();

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

            Load += async (_, _) => await this.RunAsync(LoadCarAsync);
            btnConfirm.Click += async (_, _) => await RentAndCloseAsync();
            btnIncrease.Click += (_, _) =>
            {
                ApplyTypedDays();
                SetDays(_days + 1);
            };
            btnDecrease.Click += (_, _) =>
            {
                ApplyTypedDays();
                SetDays(_days - 1);
            };
            txtDays.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ApplyTypedDays();
                }
            };
            txtDays.Leave += (_, _) => ApplyTypedDays();
            dtpStart.ValueChanged += (_, _) => UpdateTotal();
            btnCalendar.Click += (_, _) => OpenCalendar();
        }

        /// <summary>The car as it was loaded; its price is the one the customer confirms.</summary>
        internal Car? Car { get; private set; }

        internal int Days => _days;

        internal DateOnly StartDate => DateOnly.FromDateTime(dtpStart.Value);

        /// <summary>The days already taken, as loaded with the car.</summary>
        internal IReadOnlyList<BookedRange> Booked { get; private set; } = [];

        /// <summary>What the car list says once the rental was stored.</summary>
        internal string? DoneMessage { get; private set; }

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
            lblCarName.Text = car.Brand + " " + car.Model;
            lblPlate.Text = car.Plate;
            lblBrand.Text = car.Brand;
            lblModel.Text = car.Model;
            lblYear.Text = car.Year.ToString(CultureInfo.InvariantCulture);
            lblTransmission.Text = car.Transmission;
            lblFuelType.Text = car.FuelType;
            lblColor.Text = car.Color;
            lblDailyPrice.Text = DisplayFormat.Money(car.DailyPrice) + " / gün";
            picCar.ShowCar(car.Model, car.Color, car.ImageUrl, car.Brand + " " + car.Model);
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
                    DoneMessage = $"Kiralama kaydedildi: {car.Brand} {car.Model}, {DisplayFormat.Date(quote.StartDate)} - {DisplayFormat.Date(quote.EndDate)}, {DisplayFormat.Money(quote.Total)}. "
                        + "Kiralamalarım ekranından sözleşmeyi kaydedebilirsiniz.";
                },
                btnConfirm,
                btnIncrease,
                btnDecrease,
                dtpStart);
            return rented;
        }

        /// <summary>The taken days as the customer reads them: pick-up day to return day.</summary>
        internal static string DescribeBooked(IReadOnlyList<BookedRange> booked)
        {
            ArgumentNullException.ThrowIfNull(booked);
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

        private async Task RentAndCloseAsync()
        {
            if (await RentAsync())
            {
                DialogResult = DialogResult.OK;
            }
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

        /// <summary>The return day and the total of the chosen days; a refused choice says why in the message strip.</summary>
        private void UpdateTotal()
        {
            if (Car is not { } car)
            {
                return;
            }

            try
            {
                var quote = _rentals.Quote(car.Id, car.DailyPrice, StartDate, _days);
                lblTotal.Text = DisplayFormat.Money(quote.Total);
                lblReturn.Text = DisplayFormat.Date(quote.EndDate);
                lblDiscount.Text = quote.Discounted ? "Uzun kiralama indirimi dahil." : string.Empty;
                if (_quoteRefused)
                {
                    _quoteRefused = false;
                    ClearStatus();
                }
            }
            catch (BusinessRuleException ex)
            {
                lblTotal.Text = "—";
                lblReturn.Text = "—";
                lblDiscount.Text = string.Empty;
                _quoteRefused = true;
                ShowStatus(StatusKind.Warning, ex.Message);
            }
        }

        /// <summary>Takes the typed number of days, as leaving the days box does.</summary>
        internal void ApplyDays() => ApplyTypedDays();

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

        private void OpenCalendar()
        {
            var title = Car is { } car ? car.Brand + " " + car.Model : AppText.Title;
            using var form = _forms.Create<AvailabilityCalendarForm>(_session, _carId, title);
            form.ShowDialog(this);
        }

        private static Label Value(string name)
        {
            var label = FormLayout.Text(name);
            label.Font = Theme.Fonts.BodyStrong;
            return label;
        }

        private static Label Key(string text)
        {
            var label = FormLayout.Hint(text);
            label.Margin = new Padding(0, 0, Spacing.L, Spacing.S);
            return label;
        }

        /// <summary>Captions on the left, values on the right, one pair per row.</summary>
        private static TableLayoutPanel Specs(string name, params (string Caption, Label Value)[] rows)
        {
            var table = new TableLayoutPanel
            {
                Name = name,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = rows.Length,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < rows.Length; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var (caption, value) = rows[i];
                value.Margin = new Padding(0, 0, 0, Spacing.S);
                value.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                table.Controls.Add(Key(caption), 0, i);
                table.Controls.Add(value, 1, i);
            }

            return table;
        }

        private void BuildLayout()
        {
            Name = nameof(RentalForm);
            var p = Theme.Current;

            // The car: its drawing, its name with the plate and its details.
            lblCarName.Font = Theme.Fonts.Title;
            lblCarName.ForeColor = p.Text;
            lblCarName.Margin = Padding.Empty;
            lblPlate.Margin = new Padding(Spacing.S, Spacing.XS, 0, 0);
            picCar.Margin = new Padding(0, 0, 0, Spacing.M);
            var nameRow = FormLayout.Bar("carNameRow", (lblCarName, true), (lblPlate, false));
            lblCarName.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            nameRow.Margin = new Padding(0, 0, 0, Spacing.M);
            var specs = Specs(
                "carSpecs",
                ("Marka", lblBrand),
                ("Model", lblModel),
                ("Yıl", lblYear),
                ("Vites", lblTransmission),
                ("Yakıt", lblFuelType),
                ("Renk", lblColor),
                ("Günlük ücret", lblDailyPrice));
            var carCard = FormLayout.Card("carCard", null, FormLayout.Stack("carStack", picCar, nameRow, specs));
            carCard.Margin = new Padding(0, 0, Spacing.S, 0);

            // The booking: the pick-up day, the days, the total and the taken days.
            dtpStart.Font = Theme.Fonts.Body;
            var startBox = FormLayout.Date(dtpStart);
            startBox.Width = 240;
            var start = FormLayout.Field("&Alış günü", startBox);
            btnDecrease.AccessibleName = "Bir gün azalt";
            btnIncrease.AccessibleName = "Bir gün artır";
            btnDecrease.MinimumSize = btnIncrease.MinimumSize = new Size(Sizes.ControlHeight, Sizes.ControlHeight);
            btnDecrease.Margin = new Padding(0, 0, Spacing.S, 0);
            btnIncrease.Margin = new Padding(Spacing.S, 0, 0, 0);
            var daysBox = FormLayout.Input(txtDays);
            daysBox.Width = 72;
            daysBox.Margin = Padding.Empty;
            txtDays.AccessibleName = "Gün sayısı";
            var stepper = FormLayout.Bar("daysStepper", (btnDecrease, false), (daysBox, false), (btnIncrease, false));
            var daysField = FormLayout.Field("&Gün sayısı", stepper);
            var choice = FormLayout.FieldRow("choiceRow", (start, 0), (daysField, 0), (FormLayout.Spacer("choiceSpacer"), 100));

            lblTotal.Font = Theme.Fonts.Display;
            lblTotal.Name = nameof(lblTotal);
            lblDiscount.Name = nameof(lblDiscount);
            lblDiscount.Margin = Padding.Empty;
            var summary = FormLayout.Stack("summaryStack", Specs("summarySpecs", ("İade günü", lblReturn)), Key("Toplam tutar"), lblTotal, lblDiscount);
            var summaryCard = new CardPanel
            {
                Name = "summaryCard",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Spacing.L + Spacing.XS, Spacing.M, Spacing.L, Spacing.M),
                Margin = new Padding(0, Spacing.S, 0, Spacing.L),
            };
            summaryCard.Fill = p.SurfaceAlt;
            summaryCard.Edge = p.Warm;
            summaryCard.Controls.Add(summary);

            lblBooked.ForeColor = p.TextSecondary;
            lblBooked.Margin = new Padding(0, 0, 0, Spacing.S);
            btnCalendar.Margin = Padding.Empty;
            btnCalendar.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            var bookingCard = FormLayout.Card("bookingCard", "Kiralama", FormLayout.Stack("bookingStack", choice, summaryCard, lblBooked, btnCalendar));
            bookingCard.Margin = new Padding(Spacing.S, 0, 0, 0);

            Content.Controls.Add(FormLayout.Columns("layout", carCard, 44, bookingCard));

            // No accept button: Enter in the days box takes the typed days, it never confirms a rental.
            CancelButton = btnBack;
            btnBack.DialogResult = DialogResult.Cancel;
            SetFooterNote("Başlamamış bir kiralamayı Kiralamalarım ekranından iptal edebilirsiniz.");
            SetFooterButtons(btnConfirm, btnBack);
            FinishLayout();
        }
    }
}
