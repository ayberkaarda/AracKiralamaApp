using System.Globalization;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>The customer's car list: filter by brand, gearbox and fuel, book a car, open the own rentals.</summary>
    public sealed class CarListForm : AppDialog, ISignOutPanel
    {
        private readonly ICarService _cars;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly RequestSequence _loads = new();
        private readonly CarFilterBoxes _filter;

        private readonly Label lblGreeting = new() { Name = nameof(lblGreeting), AutoSize = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleRight };
        private readonly ThemedComboBox cmbBrand = FormLayout.Picker(nameof(cmbBrand), 170);
        private readonly ThemedComboBox cmbTransmission = FormLayout.Picker(nameof(cmbTransmission), 150);
        private readonly ThemedComboBox cmbFuelType = FormLayout.Picker(nameof(cmbFuelType), 150);
        private readonly Label lblCount = FormLayout.Hint(string.Empty);
        private readonly CardGrid grdCars = new() { Name = nameof(grdCars), EmptyText = "Bu seçimlere uyan, kiralamaya açık araç yok.", LoadingText = "Araçlar yükleniyor…" };
        private readonly ThemedButton btnProfile = FormLayout.Button(nameof(btnProfile), "Kiralamalarım", ButtonKind.Secondary, Glyphs.List);
        private readonly ThemedButton btnSignOut = FormLayout.Button(nameof(btnSignOut), "Çıkış Yap", ButtonKind.Secondary, Glyphs.SignOut);

        public CarListForm(ICarService cars, IFormFactory forms, UserSession session)
            : base("Araçlar", "Kiralamaya açık araçlar; marka, vites ve yakıt türüne göre süzün.", Glyphs.Car, new Size(1040, 700), new Size(720, 560))
        {
            ArgumentNullException.ThrowIfNull(cars);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _cars = cars;
            _forms = forms;
            _session = session;
            BuildLayout();
            _filter = new CarFilterBoxes(cmbBrand, cmbTransmission, cmbFuelType);
            _filter.Changed += async (_, _) => await this.RunAsync(LoadCarsAsync);
            lblGreeting.Text = "Merhaba, " + AppText.DisplayName(session.UserName) + Environment.NewLine + "Müşteri hesabı";

            Load += async (_, _) => await this.RunAsync(RefreshAsync);
            btnProfile.Click += async (_, _) => await OpenProfileAsync();
            btnSignOut.Click += (_, _) => SignOut();
        }

        public bool SignedOut { get; private set; }

        /// <summary>The car cards shown now, for tests.</summary>
        internal IReadOnlyList<Car> ShownCars { get; private set; } = [];

        /// <summary>The "Kirala" buttons of the shown cards, for tests.</summary>
        internal IReadOnlyList<Button> RentButtons { get; private set; } = [];

        internal CarFilterBoxes Filter => _filter;

        /// <summary>Loads the filter values and the car list.</summary>
        internal async Task RefreshAsync()
        {
            _filter.Fill(await _cars.GetFilterOptionsAsync(_session));
            await LoadCarsAsync();
        }

        /// <summary>Loads the cars of the current filter; only the latest request is shown.</summary>
        internal async Task LoadCarsAsync()
        {
            var request = _loads.Next();
            grdCars.IsLoading = true;
            try
            {
                var cars = await _cars.GetCatalogAsync(_session, _filter.Current);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShowCars(cars);
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    grdCars.IsLoading = false;
                }
            }
        }

        private void ShowCars(IReadOnlyList<Car> cars)
        {
            ShownCars = cars;
            var buttons = new List<Button>(cars.Count);
            var cards = new List<Control>(cars.Count);
            foreach (var car in cars)
            {
                var carId = car.Id;
                var (card, rent) = CarCards.Build(car, () => OpenRentalAsync(carId));
                cards.Add(FormLayout.ScaledFor(card, grdCars));
                buttons.Add(rent);
            }

            grdCars.Replace(cards);
            RentButtons = buttons;
            lblCount.Text = cars.Count == 0 ? string.Empty : string.Create(CultureInfo.CurrentCulture, $"{cars.Count} araç");
        }

        private async Task OpenRentalAsync(int carId)
        {
            string? done = null;
            using (var form = _forms.Create<RentalForm>(_session, carId))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    done = form.DoneMessage;
                }
            }

            if (done is not null)
            {
                await this.RunAsync(LoadCarsAsync);
                ShowStatus(StatusKind.Success, done);
            }
        }

        private async Task OpenProfileAsync()
        {
            using (var form = _forms.Create<ProfileForm>(_session))
            {
                form.ShowDialog(this);
            }

            // A cancellation in the profile frees days of a car.
            await this.RunAsync(RefreshAsync);
        }

        private void SignOut()
        {
            if (Prompts.Confirm("Çıkış yapmak istediğinizden emin misiniz?"))
            {
                SignedOut = true;
                Close();
            }
        }

        private void BuildLayout()
        {
            Name = nameof(CarListForm);
            lblGreeting.Font = Theme.Fonts.Body;
            lblGreeting.ForeColor = Theme.Current.TextSecondary;
            SetHeaderAside(lblGreeting);

            lblCount.Name = nameof(lblCount);
            lblCount.Margin = new Padding(Spacing.L, 0, 0, Spacing.M);
            var filters = FormLayout.FieldRow(
                "filterRow",
                (FormLayout.Field("&Marka", cmbBrand), 0),
                (FormLayout.Field("&Vites", cmbTransmission), 0),
                (FormLayout.Field("&Yakıt", cmbFuelType), 0),
                (FormLayout.Spacer("filterSpacer"), 100),
                (lblCount, 0));
            lblCount.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            var filterCard = FormLayout.Card("filterCard", null, filters, fill: false);
            filterCard.Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, 0);
            grdCars.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("carsPage", (filterCard, false), (grdCars, true)));

            CancelButton = btnSignOut;
            SetFooterNote("Gün sayısını ve tutarı bir sonraki adımda görürsünüz.");
            SetFooterButtons(btnProfile, btnSignOut);
            FinishLayout();
        }
    }
}
