using AracKiralama.Business;
using AracKiralama.Business.Services;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>The customer's own active rentals with their cancellation or return and contract, and the password change.</summary>
    public sealed class ProfileForm : AppDialog
    {
        private readonly IRentalService _rentals;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _end = new();
        private readonly RequestSequence _loads = new();

        private readonly Label lblGreeting = new() { Name = nameof(lblGreeting), AutoSize = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleRight };
        private readonly CardGrid grdRentals = new()
        {
            Name = nameof(grdRentals),
            EmptyText = "Aktif kiralamanız yok. Araçlar ekranından bir araç kiralayabilirsiniz.",
            LoadingText = "Kiralamalarınız yükleniyor…",
            MinCardWidth = RentalCards.MinWidth,
        };
        private readonly ThemedButton btnChangePassword = FormLayout.Button(nameof(btnChangePassword), "Şifre Değiştir", ButtonKind.Secondary, Glyphs.Lock);
        private readonly ThemedButton btnClose = FormLayout.Button(nameof(btnClose), "Kapat", ButtonKind.Secondary);

        public ProfileForm(IRentalService rentals, IContractService contracts, IFormFactory forms, UserSession session)
            : base("Kiralamalarım", "Aktif kiralamalarınız: başlamamış olanı iptal edin, başlamış olanı iade edin, sözleşmeyi kaydedin.", Glyphs.List, new Size(900, 600), new Size(640, 480), modal: true)
        {
            ArgumentNullException.ThrowIfNull(rentals);
            ArgumentNullException.ThrowIfNull(contracts);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _rentals = rentals;
            _forms = forms;
            _session = session;
            Contracts = new ContractExport(contracts, session);
            BuildLayout();
            _end.HoldClosingOf(this);
            Contracts.Operation.HoldClosingOf(this);
            lblGreeting.Text = AppText.DisplayName(session.UserName) + Environment.NewLine + "Müşteri hesabı";

            Load += async (_, _) => await this.RunAsync(LoadRentalsAsync);
            btnChangePassword.Click += (_, _) => ChangePassword();
            btnClose.Click += (_, _) => Close();
        }

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

        /// <summary>Loads the account's active rentals; only the latest request is shown.</summary>
        internal async Task LoadRentalsAsync()
        {
            var request = _loads.Next();
            grdRentals.IsLoading = true;
            try
            {
                var rentals = await _rentals.GetMyActiveRentalsAsync(_session);
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
                var (card, end, contract) = RentalCards.Build(current, _rentals.IsUnderway(current), showRenter: false, () => EndAsync(current), () => SaveContractAsync(current));
                cards.Add(FormLayout.ScaledFor(card, grdRentals));
                buttons.Add(end);
                contractButtons.Add(contract);
            }

            grdRentals.Replace(cards);
            EndButtons = buttons;
            ContractButtons = contractButtons;
        }

        private void ChangePassword()
        {
            using var form = _forms.Create<ChangePasswordForm>(_session);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Prompts.Inform(ChangePasswordForm.ChangedMessage);
            }
        }

        private void BuildLayout()
        {
            Name = nameof(ProfileForm);
            lblGreeting.Font = Theme.Fonts.Body;
            lblGreeting.ForeColor = Theme.Current.TextSecondary;
            SetHeaderAside(lblGreeting);
            Content.Controls.Add(FormLayout.Page("rentalsPage", (grdRentals, true)));

            CancelButton = btnClose;
            SetFooterNote("İptal alış gününden önce, iade alış gününden itibaren yapılır.");
            SetFooterButtons(btnChangePassword, btnClose);
            FinishLayout();
        }
    }
}
