using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>The customer's profile: own active rentals with their cancellation or return, and the password change.</summary>
    public partial class ProfileForm : Form
    {
        private readonly IRentalService _rentals;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _end = new();
        private readonly RequestSequence _loads = new();
        private readonly Panel _scrollPanel;

        public ProfileForm(IRentalService rentals, IContractService contracts, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(contracts);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _forms = forms;
            _session = session;
            Contracts = new ContractExport(contracts, session);
            Prompts = new MessageBoxPrompts(this);
            _end.HoldClosingOf(this);
            Contracts.Operation.HoldClosingOf(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            BuildHeader();
            _scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.WhiteSmoke,
                Padding = new Padding(0, 10, 0, 0),
            };
            Controls.Add(_scrollPanel);
            Controls.SetChildIndex(_scrollPanel, 0);
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The rentals shown now, for tests.</summary>
        internal IReadOnlyList<RentalDetails> ShownRentals { get; private set; } = [];

        /// <summary>The end buttons shown now, for tests.</summary>
        internal IReadOnlyList<Button> EndButtons { get; private set; } = [];

        /// <summary>The contract buttons shown now, for tests.</summary>
        internal IReadOnlyList<Button> ContractButtons { get; private set; } = [];

        /// <summary>Saves the rentals' contracts; its dialog and file writer are replaced in tests.</summary>
        internal ContractExport Contracts { get; }

        /// <summary>Saves the contract of one of the account's rentals; ignored while another save runs.</summary>
        internal Task<bool> SaveContractAsync(RentalDetails rental)
        {
            ArgumentNullException.ThrowIfNull(rental);
            return Contracts.SaveAsync(this, Prompts, rental.Id, ContractButtons);
        }

        internal async Task LoadRentalsAsync()
        {
            var request = _loads.Next();
            var rentals = await _rentals.GetMyActiveRentalsAsync(_session);
            if (_loads.IsLatest(request) && !IsDisposed)
            {
                ShowRentals(rentals);
            }
        }

        /// <summary>
        /// Cancels (before the pick-up day) or returns one rental after a confirmation; ignored while another
        /// one is still running, and every end button stays disabled meanwhile. Returns whether it ended.
        /// </summary>
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
            await this.RunAsync(LoadRentalsAsync);
        }

        private void BuildHeader()
        {
            var header = new Panel
            {
                Size = new Size(ClientSize.Width, 80),
                Dock = DockStyle.Top,
                BackColor = Color.SteelBlue,
            };
            Controls.Add(header);

            header.Controls.Add(new Label
            {
                Text = "Hoş geldin, " + AppText.DisplayName(_session.UserName),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 25),
                AutoSize = true,
            });

            var changePassword = new Button
            {
                Name = "btnChangePassword",
                Text = "Şifre Değiştir",
                Size = new Size(120, 35),
                Location = new Point(header.Width - 140, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.White,
                ForeColor = Color.SteelBlue,
                FlatStyle = FlatStyle.Flat,
            };
            changePassword.Click += (_, _) =>
            {
                using var form = _forms.Create<ChangePasswordForm>(_session);
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    Prompts.Inform(ChangePasswordForm.ChangedMessage);
                }
            };
            header.Controls.Add(changePassword);
        }

        private void ShowRentals(IReadOnlyList<RentalDetails> rentals)
        {
            ShownRentals = rentals;
            var buttons = new List<Button>();
            var contractButtons = new List<Button>();
            _scrollPanel.SuspendLayout();
            try
            {
                foreach (Control old in _scrollPanel.Controls.Cast<Control>().ToList())
                {
                    old.Dispose();
                }

                var y = 20 + _scrollPanel.AutoScrollPosition.Y;
                foreach (var rental in rentals)
                {
                    var current = rental;
                    var (card, button, contract) = RentalCards.Build(current, _rentals.IsUnderway(current), showRenter: false, () => EndAsync(current), () => SaveContractAsync(current));
                    card.Location = new Point(20, y);
                    _scrollPanel.Controls.Add(card);
                    buttons.Add(button);
                    contractButtons.Add(contract);
                    y = card.Bottom + 20;
                }
            }
            finally
            {
                _scrollPanel.ResumeLayout();
            }

            EndButtons = buttons;
            ContractButtons = contractButtons;
        }
    }
}
