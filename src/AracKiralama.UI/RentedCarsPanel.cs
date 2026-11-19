using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>The admin's list of every active rental, filtered by brand, gearbox and fuel, with the cancellation or return of each.</summary>
    public sealed class RentedCarsPanel : AppDialog
    {
        private readonly IRentalService _rentals;
        private readonly ICarService _cars;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _end = new();
        private readonly RequestSequence _loads = new();
        private readonly CarFilterBoxes _filter;
        private CarFilter _applied = CarFilter.None;

        private readonly ThemedComboBox cmbBrand = FormLayout.Picker(nameof(cmbBrand), 170);
        private readonly ThemedComboBox cmbTransmission = FormLayout.Picker(nameof(cmbTransmission), 150);
        private readonly ThemedComboBox cmbFuelType = FormLayout.Picker(nameof(cmbFuelType), 150);
        private readonly ThemedButton btnApplyFilter = FormLayout.Button(nameof(btnApplyFilter), "Filtrele", ButtonKind.Secondary, Glyphs.Filter);
        private readonly Label lblCount = FormLayout.Hint(string.Empty);
        private readonly CardGrid grdRentals = new()
        {
            Name = nameof(grdRentals),
            EmptyText = "Bu seçimlere uyan aktif kiralama yok.",
            LoadingText = "Kiralamalar yükleniyor…",
            MinCardWidth = RentalCards.MinWidth,
        };
        private readonly ThemedButton btnClose = FormLayout.Button(nameof(btnClose), "Kapat", ButtonKind.Secondary);

        public RentedCarsPanel(IRentalService rentals, ICarService cars, IContractService contracts, UserSession session)
            : base("Kiralamalar", "Tüm aktif kiralamalar; iptal edin, iade alın ya da sözleşmeyi kaydedin.", Glyphs.List, new Size(1000, 660), new Size(720, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(cars);
            ArgumentNullException.ThrowIfNull(contracts);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _cars = cars;
            _session = session;
            Contracts = new ContractExport(contracts, session);
            BuildLayout();
            _filter = new CarFilterBoxes(cmbBrand, cmbTransmission, cmbFuelType);
            _end.HoldClosingOf(this);
            Contracts.Operation.HoldClosingOf(this);

            Load += async (_, _) => await this.RunAsync(RefreshAsync);
            btnApplyFilter.Click += async (_, _) => await this.RunAsync(ApplyFilterAsync);
            btnClose.Click += (_, _) => Close();
        }

        internal IReadOnlyList<RentalDetails> ShownRentals { get; private set; } = [];

        internal IReadOnlyList<Button> EndButtons { get; private set; } = [];

        /// <summary>The contract buttons shown now, for tests.</summary>
        internal IReadOnlyList<Button> ContractButtons { get; private set; } = [];

        /// <summary>Saves the rentals' contracts; its dialog and file writer are replaced in tests.</summary>
        internal ContractExport Contracts { get; }

        internal CarFilterBoxes Filter => _filter;

        /// <summary>Saves the contract of a rental; ignored while another save runs.</summary>
        internal Task<bool> SaveContractAsync(RentalDetails rental)
        {
            ArgumentNullException.ThrowIfNull(rental);
            return Contracts.SaveAsync(this, Prompts, rental.Id, ContractButtons);
        }

        internal async Task RefreshAsync()
        {
            _filter.Fill(await _cars.GetFilterOptionsAsync(_session));
            await LoadRentalsAsync();
        }

        /// <summary>Loads the rentals of the applied filter; only the latest request is shown.</summary>
        internal async Task LoadRentalsAsync()
        {
            var request = _loads.Next();
            grdRentals.IsLoading = true;
            try
            {
                var rentals = await _rentals.GetActiveRentalsAsync(_session, _applied);
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    ShowRentals(rentals);
                }
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    grdRentals.IsLoading = false;
                }
            }
        }

        /// <summary>Applies the chosen filter, as the original "Filtrele" button did.</summary>
        internal Task ApplyFilterAsync()
        {
            _applied = _filter.Current;
            return LoadRentalsAsync();
        }

        /// <summary>Cancels or returns one rental after a confirmation; ignored while another one is running.</summary>
        internal async Task<bool> EndAsync(RentalDetails rental)
        {
            ArgumentNullException.ThrowIfNull(rental);

            var ended = false;
            await this.RunExclusiveAsync(
                _end,
                async () =>
                {
                    if (!Prompts.Confirm(RentalCards.Question(rental, _rentals.IsUnderway(rental))))
                    {
                        return;
                    }

                    RentalEnding ending;
                    try
                    {
                        ending = await _rentals.EndAsync(_session, rental.Id);
                    }
                    catch (BusinessRuleException)
                    {
                        // Ended elsewhere meanwhile: the list shows the rentals as they are now.
                        await LoadRentalsAsync();
                        throw;
                    }

                    ended = true;
                    await LoadRentalsAsync();
                    Prompts.Inform(ending == RentalEnding.Cancelled ? "Kiralama başarıyla iptal edildi." : "Araç iade alındı.");
                },
                [.. EndButtons]);
            return ended;
        }

        private void ShowRentals(IReadOnlyList<RentalDetails> rentals)
        {
            ShownRentals = rentals;
            var cards = new List<Control>(rentals.Count);
            var buttons = new List<Button>(rentals.Count);
            var contractButtons = new List<Button>(rentals.Count);
            foreach (var rental in rentals)
            {
                var current = rental;
                var (card, end, contract) = RentalCards.Build(current, _rentals.IsUnderway(current), showRenter: true, () => EndAsync(current), () => SaveContractAsync(current));
                cards.Add(FormLayout.ScaledFor(card, grdRentals));
                buttons.Add(end);
                contractButtons.Add(contract);
            }

            grdRentals.Replace(cards);
            EndButtons = buttons;
            ContractButtons = contractButtons;
            lblCount.Text = rentals.Count == 0 ? string.Empty : string.Create(CultureInfo.CurrentCulture, $"{rentals.Count} kiralama");
        }

        private void BuildLayout()
        {
            Name = nameof(RentedCarsPanel);
            lblCount.Name = nameof(lblCount);
            lblCount.Margin = new Padding(Spacing.L, 0, 0, Spacing.M);
            var filters = FormLayout.FieldRow(
                "filterRow",
                (FormLayout.Field("&Marka", cmbBrand), 0),
                (FormLayout.Field("&Vites", cmbTransmission), 0),
                (FormLayout.Field("&Yakıt", cmbFuelType), 0),
                (FormLayout.FieldAction(btnApplyFilter), 0),
                (FormLayout.Spacer("filterSpacer"), 100),
                (lblCount, 0));
            lblCount.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            var filterCard = FormLayout.Card("filterCard", null, filters, fill: false);
            filterCard.Padding = new Padding(Spacing.L, Spacing.M, Spacing.L, 0);
            Content.Controls.Add(FormLayout.Page("rentalsPage", (filterCard, false), (grdRentals, true)));

            AcceptButton = btnApplyFilter;
            CancelButton = btnClose;
            SetFooterNote("İptal alış gününden önce, iade alış gününden itibaren yapılır; ücret değişmez.");
            SetFooterButtons(btnClose);
            FinishLayout();
        }
    }
}
