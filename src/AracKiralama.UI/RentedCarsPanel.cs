using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>The admin's list of every active rental, filtered by brand, gearbox and fuel, with the cancellation or return of each.</summary>
    public partial class RentedCarsPanel : Form
    {
        private readonly IRentalService _rentals;
        private readonly ICarService _cars;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _end = new();
        private readonly RequestSequence _loads = new();
        private readonly CarFilterBoxes _filter;
        private readonly Panel _listPanel;
        private CarFilter _applied = CarFilter.None;

        public RentedCarsPanel(IRentalService rentals, ICarService cars, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(cars);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _cars = cars;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            _listPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            Controls.Add(_listPanel);
            _filter = BuildFilterPanel();
            // The filling list docks last, below the filter bar.
            Controls.SetChildIndex(_listPanel, 0);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        internal IReadOnlyList<RentalDetails> ShownRentals { get; private set; } = [];

        internal IReadOnlyList<Button> EndButtons { get; private set; } = [];

        internal CarFilterBoxes Filter => _filter;

        internal async Task RefreshAsync()
        {
            _filter.Fill(await _cars.GetFilterOptionsAsync(_session));
            await LoadRentalsAsync();
        }

        /// <summary>Loads the rentals of the applied filter; only the latest request is shown.</summary>
        internal async Task LoadRentalsAsync()
        {
            var request = _loads.Next();
            var rentals = await _rentals.GetActiveRentalsAsync(_session, _applied);
            if (_loads.IsLatest(request) && !IsDisposed)
            {
                ShowRentals(rentals);
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
                    Prompts.Inform(ending == RentalEnding.Cancelled ? "Kiralama başarıyla iptal edildi." : "Araç iade alındı.");
                    await LoadRentalsAsync();
                },
                [.. EndButtons]);
            return ended;
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        private CarFilterBoxes BuildFilterPanel()
        {
            var panel = new Panel
            {
                Size = new Size(Width, 60),
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(245, 245, 245),
                BorderStyle = BorderStyle.FixedSingle,
            };
            Controls.Add(panel);

            var brand = new ComboBox { Name = "cmbBrand", Location = new Point(60, 15), Width = 120 };
            var transmission = new ComboBox { Name = "cmbTransmission", Location = new Point(270, 15), Width = 120 };
            var fuel = new ComboBox { Name = "cmbFuelType", Location = new Point(490, 15), Width = 120 };
            var apply = new Button
            {
                Name = "btnApplyFilter",
                Text = "Filtrele",
                Location = new Point(640, 12),
                Size = new Size(100, 30),
                BackColor = Color.SteelBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            apply.Click += async (_, _) => await this.RunAsync(ApplyFilterAsync);

            panel.Controls.AddRange(
            [
                new Label { Text = "Marka:", Location = new Point(10, 20), AutoSize = true },
                brand,
                new Label { Text = "Vites Tipi:", Location = new Point(200, 20), AutoSize = true },
                transmission,
                new Label { Text = "Yakıt Tipi:", Location = new Point(415, 20), AutoSize = true },
                fuel,
                apply,
            ]);
            return new CarFilterBoxes(brand, transmission, fuel);
        }

        private void ShowRentals(IReadOnlyList<RentalDetails> rentals)
        {
            ShownRentals = rentals;
            var buttons = new List<Button>();
            _listPanel.SuspendLayout();
            try
            {
                foreach (Control old in _listPanel.Controls.Cast<Control>().ToList())
                {
                    old.Dispose();
                }

                var top = 20 + _listPanel.AutoScrollPosition.Y;
                for (var i = 0; i < rentals.Count; i++)
                {
                    var current = rentals[i];
                    var (card, button) = RentalCards.Build(current, _rentals.IsUnderway(current), showRenter: true, () => EndAsync(current));
                    card.Location = new Point(i % 2 == 0 ? 20 : 650, top + (i / 2 * (RentalCards.Height + 20)));
                    _listPanel.Controls.Add(card);
                    buttons.Add(button);
                }
            }
            finally
            {
                _listPanel.ResumeLayout();
            }

            EndButtons = buttons;
        }
    }
}
