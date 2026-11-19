using System.Globalization;
using AracKiralama.Business;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// The admin's account management. Passwords are never shown: the password field sets a new one and
    /// may stay empty when an account is changed; e-mail and phone may stay empty, as accounts of the
    /// original application may have neither. The list masks both; the edit fields show them in full.
    /// </summary>
    public sealed class UserManagementPanel : AppDialog
    {
        internal const string OwnSessionEndedMessage =
            "Kendi rolünüzü veya şifrenizi değiştirdiniz. Güvenliğiniz için oturumunuz kapatıldı; lütfen yeniden giriş yapın.";

        private readonly IAccountService _accounts;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();
        private bool _binding;

        /// <summary>The phone field's text right after an account was picked, and the stored phone.</summary>
        private (string Shown, string? Stored)? _shownPhone;

        private readonly Label lblCount = FormLayout.Hint(string.Empty);
        private readonly ThemedListBox lstUsers = new() { Name = nameof(lstUsers), AccessibleName = "Hesaplar", EmptyText = "Henüz hesap yok." };
        private readonly Label lblSelection = FormLayout.Text(nameof(lblSelection));
        private readonly Label lblSelectionDetail = FormLayout.Hint(string.Empty);
        private readonly TextBox txtUserName = new() { Name = nameof(txtUserName), MaxLength = InputRules.StoredUserNameMaxLength };
        private readonly TextBox txtPassword = new() { Name = nameof(txtPassword), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtEmail = new() { Name = nameof(txtEmail), MaxLength = InputRules.EmailMaxLength };
        private readonly TextBox txtPhone = new() { Name = nameof(txtPhone), MaxLength = 12 };
        private readonly ThemedComboBox cmbRole = FormLayout.Picker(nameof(cmbRole), 140, "Rol seçin");
        private readonly ThemedButton btnNewUser = FormLayout.Button(nameof(btnNewUser), "Yeni Hesap", ButtonKind.Subtle, Glyphs.AddPerson);
        private readonly ThemedButton btnAddUser = FormLayout.Button(nameof(btnAddUser), "Kullanıcı Ekle", ButtonKind.Secondary, Glyphs.Add);
        private readonly ThemedButton btnUpdateUser = FormLayout.Button(nameof(btnUpdateUser), "Kullanıcıyı Güncelle", ButtonKind.Primary, Glyphs.Save);
        private readonly ThemedButton btnDeleteUser = FormLayout.Button(nameof(btnDeleteUser), "Kullanıcıyı Sil", ButtonKind.Danger, Glyphs.Delete);
        private readonly ThemedButton btnClose = FormLayout.Button(nameof(btnClose), "Kapat", ButtonKind.Secondary);

        public UserManagementPanel(IAccountService accounts, UserSession session)
            : base("Hesaplar", "Hesap ekleyin; rolünü, şifresini ve iletişim bilgilerini değiştirin ya da silin.", Glyphs.People, new Size(1080, 640), new Size(860, 540), modal: true)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            ArgumentNullException.ThrowIfNull(session);

            _accounts = accounts;
            _session = session;
            cmbRole.ReplaceItems(RoleChoice.All);
            txtPhone.FormatAsPhone();
            BuildLayout();
            ClearForm();

            Load += async (_, _) => await this.RunAsync(LoadUsersAsync);
            lstUsers.SelectedIndexChanged += (_, _) =>
            {
                if (!_binding && lstUsers.SelectedItem is AccountItem item)
                {
                    SelectUser(item.User);
                }
            };
            btnNewUser.Click += (_, _) => ClearForm();
            btnAddUser.Click += async (_, _) => await AddAsync();
            btnUpdateUser.Click += async (_, _) => await UpdateAsync();
            btnDeleteUser.Click += async (_, _) => await DeleteAsync();
            btnClose.Click += (_, _) => Close();
        }

        internal int? SelectedUserId => Selected?.Id;

        /// <summary>The account in the form as it was loaded, with its version, or <see langword="null"/> for a new one.</summary>
        internal UserSummary? Selected { get; private set; }

        internal IReadOnlyList<UserSummary> ShownUsers { get; private set; } = [];

        /// <summary>
        /// Whether the admin changed their own role or password here, which ended their session: the admin
        /// panel then signs out to the sign-in screen, which says why.
        /// </summary>
        internal bool SessionEnded { get; private set; }

        /// <summary>Loads the accounts; only the latest request is shown and binding them selects only the account in the form.</summary>
        internal async Task LoadUsersAsync()
        {
            var request = _loads.Next();
            lstUsers.IsLoading = true;
            try
            {
                var users = await _accounts.GetAllAsync(_session);
                if (!_loads.IsLatest(request) || IsDisposed)
                {
                    return;
                }

                ShownUsers = users;
                _binding = true;
                try
                {
                    lstUsers.ReplaceItems(users.Select(u => new AccountItem(u)));
                    lstUsers.ClearSelected();
                    var index = Selected is { } selected ? users.ToList().FindIndex(u => u.Id == selected.Id) : -1;
                    if (index >= 0)
                    {
                        lstUsers.SelectedIndex = index;
                    }
                }
                finally
                {
                    _binding = false;
                }

                lblCount.Text = string.Create(CultureInfo.CurrentCulture, $"{users.Count} hesap, {users.Count(u => u.Role == UserRole.Admin)} yönetici");
            }
            finally
            {
                if (_loads.IsLatest(request) && !IsDisposed)
                {
                    lstUsers.IsLoading = false;
                }
            }
        }

        internal void SelectUser(UserSummary user)
        {
            ArgumentNullException.ThrowIfNull(user);

            Selected = user;
            txtUserName.Text = user.UserName;
            txtPassword.Clear();
            txtEmail.Text = user.Email ?? string.Empty;
            txtPhone.Text = user.Phone ?? string.Empty;
            cmbRole.SelectedItem = RoleChoice.All.First(r => r.Role == user.Role);
            lblSelection.Text = user.UserName;
            lblSelectionDetail.Text = string.Create(CultureInfo.CurrentCulture, $"Kullanıcı no {user.Id} · {RoleChoice.Describe(user.Role)}");

            // The phone field keeps digits only, so a phone of the original data in another shape shows
            // changed; left as shown, the stored value is sent.
            _shownPhone = (txtPhone.Text, user.Phone);
        }

        internal Task<bool> AddAsync() =>
            WriteAsync(async () =>
            {
                await _accounts.AddAsync(_session, ReadInput());
                ClearForm();
                await LoadUsersAsync();
                Prompts.Inform("Kullanıcı başarıyla eklendi.");
            });

        internal Task<bool> UpdateAsync()
        {
            if (Selected is not { } loaded)
            {
                Prompts.Warn("Lütfen güncellemek için bir kullanıcı seçin.");
                return Task.FromResult(false);
            }

            return WriteAsync(async () =>
            {
                bool ownSessionEnded;
                try
                {
                    ownSessionEnded = await _accounts.UpdateAsync(_session, loaded.Id, loaded.Version, ReadInput());
                }
                catch (BusinessRuleException ex) when (ex.IsStale)
                {
                    // Changed or deleted meanwhile: the account is loaded again as it is now.
                    await LoadUsersAsync();
                    if (ShownUsers.FirstOrDefault(u => u.Id == loaded.Id) is { } current)
                    {
                        SelectUser(current);
                    }
                    else
                    {
                        ClearForm();
                    }

                    throw;
                }

                if (ownSessionEnded)
                {
                    // The admin's own role or password changed: this session has ended for good, so the
                    // panel signs out instead of answering every later click with a refusal. The sign-in
                    // screen says why.
                    SessionEnded = true;
                    ClearForm();
                    Close();
                    return;
                }

                ClearForm();
                await LoadUsersAsync();
                Prompts.Inform("Kullanıcı başarıyla güncellendi.");
            });
        }

        internal Task<bool> DeleteAsync()
        {
            if (SelectedUserId is not { } id)
            {
                Prompts.Warn("Lütfen silinecek bir kullanıcı seçin.");
                return Task.FromResult(false);
            }

            return WriteAsync(async () =>
            {
                if (!Prompts.Confirm("Bu kullanıcıyı silmek istediğinizden emin misiniz?"))
                {
                    return;
                }

                await _accounts.DeleteAsync(_session, id);
                ClearForm();
                await LoadUsersAsync();
                Prompts.Inform("Kullanıcı başarıyla silindi.");
            });
        }

        private Task<bool> WriteAsync(Func<Task> write) =>
            this.RunExclusiveAsync(
                _write,
                async () =>
                {
                    try
                    {
                        await write();
                    }
                    finally
                    {
                        txtPassword.Clear();
                    }
                },
                btnAddUser,
                btnUpdateUser,
                btnDeleteUser,
                btnNewUser);

        private AccountInput ReadInput() => new(
            txtUserName.Text,
            txtPassword.Text,
            txtEmail.Text,
            SelectedUserId is not null && _shownPhone is ({ } shown, { } stored) && txtPhone.Text == shown ? stored : txtPhone.Text,
            (cmbRole.SelectedItem as RoleChoice)?.Role ?? (UserRole)(-1));

        private void ClearForm()
        {
            Selected = null;
            _shownPhone = null;
            _binding = true;
            try
            {
                lstUsers.ClearSelected();
            }
            finally
            {
                _binding = false;
            }

            txtUserName.Clear();
            txtPassword.Clear();
            txtEmail.Clear();
            txtPhone.Clear();
            cmbRole.SelectedIndex = -1;
            lblSelection.Text = "Yeni hesap";
            lblSelectionDetail.Text = "Bilgileri girip \"Kullanıcı Ekle\" ile kaydedin.";
        }

        private void BuildLayout()
        {
            Name = nameof(UserManagementPanel);
            lblCount.Name = nameof(lblCount);
            btnNewUser.Margin = Padding.Empty;
            var toolbar = FormLayout.Bar("userToolbar", (lblCount, true), (btnNewUser, false));
            toolbar.Margin = new Padding(0, 0, 0, Spacing.S);
            lstUsers.Describe = item => item is AccountItem account ? account.Row() : new ListRow(item.ToString() ?? string.Empty);
            lstUsers.LoadingText = "Hesaplar yükleniyor…";
            var usersCard = FormLayout.Card("usersCard", "Hesaplar", FormLayout.Page("usersPage", (toolbar, false), (lstUsers, true)));

            var badge = new GlyphBadge(Glyphs.Person, Sizes.HeaderBadge, 14F) { Name = "selectionBadge", Margin = new Padding(0, 0, Spacing.M, 0) };
            lblSelection.Font = Theme.Fonts.Subtitle;
            lblSelectionDetail.Name = nameof(lblSelectionDetail);
            var selectionText = FormLayout.Stack("selectionText", lblSelection, lblSelectionDetail);
            var selection = FormLayout.Bar("selectionRow", (badge, false), (selectionText, true));
            selectionText.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            selection.Margin = new Padding(0, 0, 0, Spacing.M);
            var name = FormLayout.FieldRow(
                "nameRow",
                (FormLayout.Field("&Kullanıcı adı", FormLayout.Input(txtUserName)), 60),
                (FormLayout.Field("&Rol", cmbRole), 40));
            var password = FormLayout.Field(
                "&Yeni şifre",
                FormLayout.Input(txtPassword),
                string.Create(CultureInfo.CurrentCulture, $"Yeni hesapta zorunlu; değiştirirken boş bırakılırsa şifre aynı kalır. En az {PasswordPolicy.MinLength} karakter."));
            password.Margin = new Padding(0, 0, 0, Spacing.M);
            var contact = FormLayout.FieldRow(
                "contactRow",
                (FormLayout.Field("&E-posta", FormLayout.Input(txtEmail)), 60),
                (FormLayout.Field("&Telefon", FormLayout.Input(txtPhone)), 40));
            btnAddUser.Margin = btnUpdateUser.Margin = new Padding(0, 0, Spacing.S, 0);
            btnDeleteUser.Margin = Padding.Empty;
            var actions = FormLayout.Bar("userActions", (btnAddUser, false), (btnUpdateUser, false), (FormLayout.Spacer("userActionsSpacer"), true), (btnDeleteUser, false));
            actions.Margin = new Padding(0, Spacing.S, 0, 0);
            var accountCard = FormLayout.Card("accountCard", "Hesap bilgileri", FormLayout.Stack("accountForm", selection, name, password, contact, actions), fill: false);
            accountCard.Margin = Padding.Empty;

            Content.Controls.Add(FormLayout.Columns("layout", usersCard, 46, FormLayout.Page("formColumn", (accountCard, false))));

            CancelButton = btnClose;
            SetFooterNote("Listede e-posta ve telefon maskelenir; tam hali yalnızca seçilen hesabın alanlarında görünür.");
            SetFooterButtons(btnClose);
            FinishLayout();
        }
    }
}
