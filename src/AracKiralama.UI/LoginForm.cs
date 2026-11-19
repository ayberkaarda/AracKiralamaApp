using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>A main screen that the account can leave to sign in again.</summary>
    internal interface ISignOutPanel
    {
        /// <summary>Whether the panel was closed with its sign-out button or because its session ended.</summary>
        bool SignedOut { get; }

        /// <summary>Why the session ended, when the sign-in screen should say so; <see langword="null"/> otherwise.</summary>
        string? SignOutNotice => null;
    }

    /// <summary>Sign-in; also the entry point to the sign-up form and, when no admin exists, the keyed setup.</summary>
    public sealed class LoginForm : AppDialog
    {
        internal const string RegisteredMessage = "Hesabınız oluşturuldu. Şimdi kullanıcı adınız ve şifrenizle giriş yapabilirsiniz.";

        internal const string AdminCreatedMessage = "Yönetici hesabı oluşturuldu. Lütfen yeni şifrenizle giriş yapın.";

        private readonly IAuthService _auth;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _signIn = new();
        private bool _offerAdminSetup;

        private readonly TextBox txtUserName = new() { Name = nameof(txtUserName), MaxLength = InputRules.StoredUserNameMaxLength };
        private readonly TextBox txtPassword = new() { Name = nameof(txtPassword), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnSignIn = FormLayout.Button(nameof(btnSignIn), "Giriş Yap", ButtonKind.Primary);
        private readonly ThemedButton btnRegister = FormLayout.Button(nameof(btnRegister), "Hesap Oluştur", ButtonKind.Secondary, Glyphs.AddPerson);
        private readonly ThemedButton btnSetup = FormLayout.Button(nameof(btnSetup), "İlk yönetici kurulumu (kurulum anahtarı)", ButtonKind.Subtle, Glyphs.Key);
        private readonly ThemedButton btnExit = FormLayout.Button(nameof(btnExit), "Çıkış", ButtonKind.Secondary);

        public LoginForm(IAuthService auth, IFormFactory forms)
            : base("Giriş", string.Empty, string.Empty, new Size(860, 540), new Size(760, 500), header: false)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(forms);

            _auth = auth;
            _forms = forms;
            BuildLayout();
            OfferAdminSetup = false;

            btnSignIn.Click += async (_, _) => await SignInAndOpenAsync();
            btnRegister.Click += (_, _) => OpenRegistration();
            btnSetup.Click += (_, _) => OpenSetup();
            btnExit.Click += (_, _) => Close();
        }

        /// <summary>
        /// Whether the database has accounts but no admin, so the sign-in offers the first-run setup. That
        /// setup asks for a setup key issued by the database owner; having no admin alone grants nothing.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool OfferAdminSetup
        {
            get => _offerAdminSetup;
            set
            {
                _offerAdminSetup = value;
                FormLayout.SetShown(btnSetup, value);
            }
        }

        /// <summary>A message from the step before the sign-in (a new account, the first admin, an ended session).</summary>
        internal void ShowNotice(string? notice, StatusKind kind = StatusKind.Info)
        {
            if (!string.IsNullOrWhiteSpace(notice))
            {
                ShowStatus(kind, notice);
            }
        }

        /// <summary>
        /// Signs the account in; ignored while a previous attempt is still running. Returns the session, or
        /// <see langword="null"/> when the attempt failed (the reason has been shown) or was ignored.
        /// </summary>
        internal async Task<UserSession?> SignInAsync()
        {
            UserSession? session = null;
            await this.RunExclusiveAsync(
                _signIn,
                async () =>
                {
                    try
                    {
                        session = await _auth.SignInAsync(txtUserName.Text, txtPassword.Text);
                    }
                    finally
                    {
                        txtPassword.Text = string.Empty;
                        if (session is null)
                        {
                            // Retyping the password is the next step after a refused attempt.
                            _signIn.FocusAfterUnlock(txtPassword);
                        }
                    }
                },
                btnSignIn,
                btnRegister,
                btnSetup);
            return session;
        }

        private async Task SignInAndOpenAsync()
        {
            var session = await SignInAsync();
            if (session is not null)
            {
                OpenPanel(session);
            }
        }

        /// <summary>
        /// Opens the screen of the signed-in role: admins get the management panel, users the car list.
        /// Signing out there returns to this form; closing the panel ends the application, as before.
        /// </summary>
        private void OpenPanel(UserSession session)
        {
            Hide();
            ISignOutPanel? ended;
            using (Form panel = session.IsAdmin ? _forms.Create<AdminPanel>(session) : _forms.Create<CarListForm>(session))
            {
                panel.ShowDialog();
                ended = panel is ISignOutPanel { SignedOut: true } signOut ? signOut : null;
            }

            if (ended is null)
            {
                Close();
                return;
            }

            ClearStatus();
            ShowNotice(ended.SignOutNotice);
            txtPassword.Text = string.Empty;
            Show();
            txtPassword.Focus();
        }

        private void OpenRegistration()
        {
            if (_signIn.IsRunning)
            {
                return;
            }

            using var register = _forms.Create<RegisterForm>();
            if (register.ShowDialog(this) == DialogResult.OK)
            {
                ShowNotice(RegisteredMessage, StatusKind.Success);
                txtUserName.Text = register.RegisteredUserName ?? string.Empty;
                txtPassword.Focus();
            }
        }

        private void OpenSetup()
        {
            if (_signIn.IsRunning)
            {
                return;
            }

            using var setup = _forms.Create<FirstAdminSetupForm>();
            setup.RequireSetupKey = true;
            if (setup.ShowDialog(this) == DialogResult.OK)
            {
                // The new admin signs in here with the password just chosen.
                OfferAdminSetup = false;
                ShowNotice(AdminCreatedMessage, StatusKind.Success);
                txtUserName.Text = setup.Session?.UserName ?? string.Empty;
                txtPassword.Focus();
            }
        }

        private void BuildLayout()
        {
            Name = nameof(LoginForm);
            SetSidePanel(
                new BrandPanel(
                    "Aracınızı seçin, günlerinizi ayırın, yola çıkın.",
                    (Glyphs.Filter, "Kiralamaya açık araçları marka, vites ve yakıta göre süzün."),
                    (Glyphs.Calendar, "Müsaitlik takviminde boş günleri görün, kiralamanızı tek adımda yapın."),
                    (Glyphs.Document, "Kiralama sözleşmenizi yazdırılabilir sayfa olarak kaydedin.")),
                Sizes.BrandPanelWidth);

            var title = FormLayout.SectionTitle("Giriş yap");
            title.Name = "formTitle";
            title.Font = Theme.Fonts.Title;
            var intro = FormLayout.Hint("Kullanıcı adınız ve şifrenizle giriş yapın. Yöneticiler de bu ekranı kullanır.");
            intro.Name = "formIntro";
            intro.Margin = new Padding(0, 0, 0, Spacing.XL);

            var name = FormLayout.Field("&Kullanıcı adı", FormLayout.Input(txtUserName));
            var password = FormLayout.Field("Ş&ifre", FormLayout.Input(txtPassword));
            name.Margin = password.Margin = new Padding(0, 0, 0, Spacing.M);
            btnSignIn.AutoSize = false;
            btnSignIn.Dock = DockStyle.Top;
            btnSignIn.Margin = new Padding(0, Spacing.S, 0, Spacing.XL);

            var newHere = FormLayout.Hint("Hesabınız yok mu? Birkaç bilgiyle hemen oluşturun.");
            newHere.Name = "registerHint";
            newHere.Margin = new Padding(0, 0, 0, Spacing.S);
            btnRegister.Margin = new Padding(0, 0, 0, Spacing.L);
            btnSetup.Margin = Padding.Empty;

            var stack = FormLayout.Stack("signInForm", title, intro, name, password, btnSignIn, newHere, btnRegister, btnSetup);
            stack.Padding = new Padding(Spacing.S, Spacing.S, Spacing.S, 0);
            btnRegister.Anchor = btnSetup.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            Content.Controls.Add(stack);

            AcceptButton = btnSignIn;
            CancelButton = btnExit;
            SetFooterNote("Şifreniz yalnızca güvenli özet olarak saklanır.");
            SetFooterButtons(btnExit);
            FinishLayout();
        }
    }
}
