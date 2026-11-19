using System.Drawing.Drawing2D;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>The customer's car list: filter by brand, gearbox and fuel, book a car, open the profile.</summary>
    public partial class CarListForm : Form, ISignOutPanel
    {
        private const int CardWidth = 550;
        private const int CardHeight = 180;
        private const int CardSpacing = 30;
        private const int RowSpacing = 20;
        private const int CardsPerRow = 2;

        private readonly ICarService _cars;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly RequestSequence _loads = new();
        private readonly CarFilterBoxes _filter;
        private readonly Panel _scrollPanel;

        public CarListForm(ICarService cars, IFormFactory forms, UserSession session)
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
            BackgroundImage = AppAssets.Background;
            BackgroundImageLayout = ImageLayout.Stretch;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            var header = BuildHeader();
            var (filterPanel, filter) = BuildFilterPanel(header.Bottom + 10);
            _filter = filter;
            _filter.Changed += async (_, _) => await this.RunAsync(LoadCarsAsync);

            _scrollPanel = new Panel
            {
                BackColor = Color.Transparent,
                Location = new Point(0, filterPanel.Bottom + 10),
                AutoScroll = true,
            };
            _scrollPanel.Size = new Size(ClientSize.Width, ClientSize.Height - _scrollPanel.Top);
            Controls.Add(_scrollPanel);
        }

        public bool SignedOut { get; private set; }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The car cards shown now, for tests.</summary>
        internal IReadOnlyList<Car> ShownCars { get; private set; } = [];

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
            var cars = await _cars.GetCatalogAsync(_session, _filter.Current);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShowCars(cars);
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(RefreshAsync);
        }

        private Panel BuildHeader()
        {
            var header = new Panel { Size = new Size(ClientSize.Width, 50), BackColor = Color.SteelBlue };
            Controls.Add(header);

            header.Controls.Add(new Label
            {
                Text = "Merhaba, " + AppText.DisplayName(_session.UserName),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(10, 12),
                Size = new Size(500, 30),
            });

            var profile = new Button
            {
                Name = "btnProfile",
                Text = "Profil",
                Size = new Size(100, 30),
                Location = new Point(header.Width - 230, 10),
                BackColor = Color.LightSeaGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            profile.Click += async (_, _) =>
            {
                using (var form = _forms.Create<ProfileForm>(_session))
                {
                    form.ShowDialog(this);
                }

                // A cancellation in the profile frees days of a car.
                await this.RunAsync(RefreshAsync);
            };
            header.Controls.Add(profile);

            var signOut = new Button
            {
                Name = "btnSignOut",
                Text = "Çıkış Yap",
                Size = new Size(100, 30),
                Location = new Point(header.Width - 110, 10),
                BackColor = Color.IndianRed,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            signOut.Click += (_, _) =>
            {
                if (Prompts.Confirm("Çıkış yapmak istediğinizden emin misiniz?"))
                {
                    SignedOut = true;
                    Close();
                }
            };
            header.Controls.Add(signOut);
            return header;
        }

        private (Panel Panel, CarFilterBoxes Filter) BuildFilterPanel(int top)
        {
            var panel = new Panel
            {
                Size = new Size(ClientSize.Width, 60),
                Location = new Point(0, top),
                BackColor = Color.WhiteSmoke,
            };
            Controls.Add(panel);

            static Label Caption(string text, int x) => new()
            {
                Text = text,
                Location = new Point(x, 0),
                AutoSize = true,
                Font = new Font("Segoe UI", 10),
            };

            var brand = new ComboBox { Name = "cmbBrand", Location = new Point(20, 22), Width = 150 };
            var transmission = new ComboBox { Name = "cmbTransmission", Location = new Point(190, 22), Width = 150 };
            var fuel = new ComboBox { Name = "cmbFuelType", Location = new Point(360, 22), Width = 150 };
            panel.Controls.AddRange([Caption("Marka", 20), Caption("Vites Tipi", 190), Caption("Yakıt Tipi", 360), brand, transmission, fuel]);
            return (panel, new CarFilterBoxes(brand, transmission, fuel));
        }

        private void ShowCars(IReadOnlyList<Car> cars)
        {
            ShownCars = cars;
            _scrollPanel.SuspendLayout();
            try
            {
                foreach (Control old in _scrollPanel.Controls.Cast<Control>().ToList())
                {
                    old.Dispose();
                }

                var rowWidth = (CardsPerRow * (CardWidth + CardSpacing)) - CardSpacing;
                var left = (ClientSize.Width - rowWidth) / 2;
                for (var i = 0; i < cars.Count; i++)
                {
                    var card = BuildCard(cars[i]);
                    card.Location = new Point(
                        left + (i % CardsPerRow * (CardWidth + CardSpacing)),
                        10 + (i / CardsPerRow * (CardHeight + RowSpacing)) + _scrollPanel.AutoScrollPosition.Y);
                    _scrollPanel.Controls.Add(card);
                }
            }
            finally
            {
                _scrollPanel.ResumeLayout();
            }
        }

        private Panel BuildCard(Car car)
        {
            var card = new Panel
            {
                Size = new Size(CardWidth, CardHeight),
                BackColor = Color.White,
                Padding = new Padding(10),
            };
            card.Region = Rounded(card.Size, 20);

            var picture = new PictureBox
            {
                Size = new Size(180, 140),
                Location = new Point(10, 20),
                SizeMode = PictureBoxSizeMode.Zoom,
            };
            picture.ShowCarPicture(car.ImageUrl);
            card.Controls.Add(picture);

            card.Controls.Add(new Label
            {
                Size = new Size(230, 150),
                Location = new Point(200, 25),
                Text = $"{car.Brand} {car.Model}\n{car.Year} - {car.Transmission} - {car.FuelType}\nGünlük Ücret: {DisplayFormat.Money(car.DailyPrice)}",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.Black,
            });

            var rent = new Button
            {
                Text = "Kirala",
                Size = new Size(110, 40),
                Location = new Point(430, 120),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.Green,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            var carId = car.Id;
            rent.Click += async (_, _) =>
            {
                bool rented;
                using (var form = _forms.Create<RentalForm>(_session, carId))
                {
                    rented = form.ShowDialog(this) == DialogResult.OK;
                }

                if (rented)
                {
                    await this.RunAsync(LoadCarsAsync);
                }
            };
            card.Controls.Add(rent);
            return card;
        }

        private static Region Rounded(Size size, int radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(0, 0, radius, radius, 180, 90);
            path.AddArc(size.Width - radius, 0, radius, radius, 270, 90);
            path.AddArc(size.Width - radius, size.Height - radius, radius, radius, 0, 90);
            path.AddArc(0, size.Height - radius, radius, radius, 90, 90);
            return new Region(path);
        }
    }
}
