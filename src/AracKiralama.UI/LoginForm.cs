using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>A main screen that the account can leave to sign in again.</summary>
    internal interface ISignOutPanel
    {
        /// <summary>Whether the panel was closed with its sign-out button.</summary>
        bool SignedOut { get; }
    }

    /// <summary>Sign-in; also the entry point to the sign-up form and, when no admin exists, the keyed setup.</summary>
    public partial class LoginForm : Form
    {
        private readonly IAuthService _auth;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _signIn = new();

        public LoginForm(IAuthService auth, IFormFactory forms)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(forms);

            _auth = auth;
            _forms = forms;

            InitializeComponent();
            Icon = AppAssets.Icon;
            picHero.Image = AppAssets.Hero;
            Text = AppText.Title;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            txtUserName.MaxLength = InputRules.StoredUserNameMaxLength;
            txtPassword.MaxLength = PasswordPolicy.MaxLength;
        }

        /// <summary>
        /// Whether the database has accounts but no admin, so the sign-in offers the first-run setup. That
        /// setup asks for a setup key issued by the database owner; having no admin alone grants nothing.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool OfferAdminSetup
        {
            get => btnSetup.Tag is true;
            set
            {
                btnSetup.Tag = value;
                btnSetup.Visible = value;
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

        /// <summary>
        /// Opens the screen of the signed-in role: admins get the management panel, users the car list.
        /// Signing out there returns to this form; closing the panel ends the application, as before.
        /// </summary>
        private void OpenPanel(UserSession session)
        {
            Hide();
            bool signedOut;
            using (Form panel = session.IsAdmin ? _forms.Create<AdminPanel>(session) : _forms.Create<CarListForm>(session))
            {
                panel.ShowDialog();
                signedOut = panel is ISignOutPanel { SignedOut: true };
            }

            if (!signedOut)
            {
                Close();
                return;
            }

            txtPassword.Text = string.Empty;
            Show();
            txtPassword.Focus();
        }

        private async void btnSignIn_Click(object? sender, EventArgs e)
        {
            var session = await SignInAsync();
            if (session is not null)
            {
                OpenPanel(session);
            }
        }

        private void btnRegister_Click(object? sender, EventArgs e)
        {
            using var register = _forms.Create<RegisterForm>();
            register.StartPosition = FormStartPosition.CenterParent;
            register.ShowDialog(this);
        }

        private void btnSetup_Click(object? sender, EventArgs e)
        {
            using var setup = _forms.Create<FirstAdminSetupForm>();
            setup.RequireSetupKey = true;
            setup.StartPosition = FormStartPosition.CenterParent;
            if (setup.ShowDialog(this) == DialogResult.OK)
            {
                // The new admin signs in here with the password just chosen.
                OfferAdminSetup = false;
                txtUserName.Text = setup.Session?.UserName ?? string.Empty;
                txtPassword.Focus();
            }
        }
    }
}
