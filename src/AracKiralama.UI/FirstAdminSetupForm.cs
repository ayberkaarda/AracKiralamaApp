using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>
    /// First-run setup: opens by itself on an empty database, where the person installing the application
    /// creates the first admin account with a password of their own; there is no default password. A
    /// database with accounts but no admin offers it from the sign-in with <see cref="RequireSetupKey"/>
    /// set: then a setup key issued for that database proves the founder's authority. Closes with
    /// <see cref="DialogResult.OK"/> and <see cref="Session"/> set on success.
    /// </summary>
    public partial class FirstAdminSetupForm : Form
    {
        /// <summary>Height of the key row and the longer explanation, removed while no key is asked for.</summary>
        private const int KeySectionHeight = 64;

        private readonly ISetupService _setup;
        private readonly ExclusiveOperation _create = new();
        private readonly string _emptyDatabaseText;
        private bool _requireSetupKey = true;

        public FirstAdminSetupForm(ISetupService setup)
        {
            ArgumentNullException.ThrowIfNull(setup);

            _setup = setup;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            Text = AppText.Title + " - " + Text;
            _emptyDatabaseText = lblDescription.Text;
            txtKey.MaxLength = 64;
            txtUserName.MaxLength = InputRules.UserNameMaxLength;
            txtPassword.MaxLength = PasswordPolicy.MaxLength;
            txtPasswordRepeat.MaxLength = PasswordPolicy.MaxLength;
            AcceptButton = btnCreate;
            CancelButton = btnCancel;
            RequireSetupKey = false;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public UserSession? Session { get; private set; }

        /// <summary>
        /// Whether the setup key is asked for, because the database has accounts but no admin. The key field
        /// starts with the key of the ARAC_SETUP_KEY environment variable, if it has the form of one; the
        /// database decides whether it is a valid key. A malformed value fills in nothing and is reported as
        /// a configuration error when the form opens.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool RequireSetupKey
        {
            get => _requireSetupKey;
            set
            {
                if (value == _requireSetupKey)
                {
                    return;
                }

                _requireSetupKey = value;
                var shift = value ? KeySectionHeight : -KeySectionHeight;
                SuspendLayout();
                lblDescription.Height += shift;
                lblDescription.Text = value ? SetupKeyHint : _emptyDatabaseText;
                lblKey.Visible = value;
                txtKey.Visible = value;
                var prefill = value ? _setup.GetEnvironmentKey() : new SetupKeyPrefill(null, false);
                txtKey.Text = prefill.Key ?? string.Empty;
                EnvironmentKeyWarning = prefill.Invalid ? EnvironmentKeyInvalid : null;
                foreach (Control control in new Control[] { lblUserName, txtUserName, lblPassword, txtPassword, lblRepeat, txtPasswordRepeat, btnCreate, btnCancel })
                {
                    control.Top += shift;
                }

                ClientSize = new Size(ClientSize.Width, ClientSize.Height + shift);
                ActiveControl = value ? txtKey : txtUserName;
                ResumeLayout();
            }
        }

        /// <summary>The configuration warning shown when the form opens, or <see langword="null"/>.</summary>
        internal string? EnvironmentKeyWarning { get; private set; }

        /// <summary>Shown when ARAC_SETUP_KEY is set to something that is not a setup key; the value itself is never shown.</summary>
        internal static readonly string EnvironmentKeyInvalid =
            SetupKeyPolicy.EnvironmentVariable + " ortam değişkeni geçerli bir kurulum anahtarı biçiminde değil (32 onaltılık karakter); "
            + "alan boş bırakıldı. Anahtarı elle girin veya ortam değişkenini düzeltin.";

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>How the founder gets a setup key for this database; no key is issued or shown here.</summary>
        internal static readonly string SetupKeyHint =
            "Bu veritabanında hesaplar var ama yönetici yok. İlk yönetici hesabı yalnızca bu veritabanı için üretilmiş bir kurulum anahtarıyla oluşturulabilir. "
            + "Anahtarı uygulama değil, veritabanı sahibi docs\\issue-setup-key.sql betiğiyle üretir; "
            + "anahtar 24 saat geçerlidir ve bir kez kullanılır.";

        /// <summary>Creates the first admin; ignored while a previous attempt is still running.</summary>
        internal async Task<UserSession?> CreateAsync()
        {
            UserSession? session = null;
            await this.RunExclusiveAsync(
                _create,
                async () =>
                {
                    try
                    {
                        var key = RequireSetupKey ? txtKey.Text : null;
                        session = await _setup.CreateFirstAdminAsync(key, txtUserName.Text, txtPassword.Text, txtPasswordRepeat.Text);
                    }
                    finally
                    {
                        txtPassword.Clear();
                        txtPasswordRepeat.Clear();
                        if (session is null)
                        {
                            _create.FocusAfterUnlock(txtPassword);
                        }
                        else
                        {
                            txtKey.Clear();
                        }
                    }
                },
                btnCreate,
                btnCancel);
            return session;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (EnvironmentKeyWarning is { } warning)
            {
                Prompts.Warn(warning);
            }
        }

        private async void btnCreate_Click(object? sender, EventArgs e)
        {
            var session = await CreateAsync();
            if (session is not null)
            {
                Prompts.Inform("Yönetici hesabı oluşturuldu. Lütfen yeni şifrenizle giriş yapın.");
                Session = session;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
