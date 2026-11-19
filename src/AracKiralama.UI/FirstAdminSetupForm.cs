using System.Globalization;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// First-run setup: opens by itself on an empty database, where the person installing the application
    /// creates the first admin account with a password of their own; there is no default password. A
    /// database with accounts but no admin offers it from the sign-in with <see cref="RequireSetupKey"/>
    /// set: then a setup key issued for that database proves the founder's authority. Closes with
    /// <see cref="DialogResult.OK"/> and <see cref="Session"/> set on success.
    /// </summary>
    public sealed class FirstAdminSetupForm : AppDialog
    {
        internal const string EmptyDatabaseText =
            "Henüz bir yönetici hesabı yok. İlk yönetici hesabını kendi belirleyeceğiniz bir şifreyle oluşturun; varsayılan bir hesap ya da şifre yoktur.";

        private readonly ISetupService _setup;
        private readonly ExclusiveOperation _create = new();
        private bool _requireSetupKey = true;

        private readonly Label lblDescription = FormLayout.Text(nameof(lblDescription));
        private readonly TextBox txtKey = new() { Name = nameof(txtKey), MaxLength = 64, PlaceholderText = "32 onaltılık karakter" };
        private readonly TextBox txtUserName = new() { Name = nameof(txtUserName), MaxLength = InputRules.UserNameMaxLength };
        private readonly TextBox txtPassword = new() { Name = nameof(txtPassword), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtPasswordRepeat = new() { Name = nameof(txtPasswordRepeat), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnCreate = FormLayout.Button(nameof(btnCreate), "Hesabı Oluştur", ButtonKind.Primary, Glyphs.Admin);
        private readonly ThemedButton btnCancel = FormLayout.Button(nameof(btnCancel), "İptal", ButtonKind.Secondary);
        private TableLayoutPanel _keyField = null!;

        public FirstAdminSetupForm(ISetupService setup)
            : base("İlk kurulum", "İlk yönetici hesabını oluşturun.", Glyphs.Admin, new Size(600, 600), new Size(500, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(setup);

            _setup = setup;
            BuildLayout();
            _create.HoldClosingOf(this);
            RequireSetupKey = false;
            btnCreate.Click += async (_, _) => await CreateAndCloseAsync();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public UserSession? Session { get; private set; }

        /// <summary>
        /// Whether the setup key is asked for, because the database has accounts but no admin. The key field
        /// starts with the key of the ARAC_SETUP_KEY environment variable, if it has the form of one; the
        /// database decides whether it is a valid key. A malformed value fills in nothing and is reported as
        /// a configuration warning in the window.
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
                lblDescription.Text = value ? SetupKeyHint : EmptyDatabaseText;
                FormLayout.SetShown(_keyField, value);
                var prefill = value ? _setup.GetEnvironmentKey() : new SetupKeyPrefill(null, false);
                txtKey.Text = prefill.Key ?? string.Empty;
                EnvironmentKeyWarning = prefill.Invalid ? EnvironmentKeyInvalid : null;
                if (EnvironmentKeyWarning is { } warning)
                {
                    ShowStatus(StatusKind.Warning, warning);
                }
                else
                {
                    ClearStatus();
                }

                ActiveControl = value ? txtKey : txtUserName;
            }
        }

        /// <summary>The configuration warning shown in the window, or <see langword="null"/>.</summary>
        internal string? EnvironmentKeyWarning { get; private set; }

        /// <summary>Shown when ARAC_SETUP_KEY is set to something that is not a setup key; the value itself is never shown.</summary>
        internal static readonly string EnvironmentKeyInvalid =
            SetupKeyPolicy.EnvironmentVariable + " ortam değişkeni geçerli bir kurulum anahtarı biçiminde değil (32 onaltılık karakter); "
            + "alan boş bırakıldı. Anahtarı elle girin veya ortam değişkenini düzeltin.";

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

        private async Task CreateAndCloseAsync()
        {
            var session = await CreateAsync();
            if (session is not null)
            {
                Session = session;
                DialogResult = DialogResult.OK;
            }
        }

        private void BuildLayout()
        {
            Name = nameof(FirstAdminSetupForm);
            lblDescription.Text = EmptyDatabaseText;
            var note = FormLayout.Note("setupNote", Glyphs.Info, lblDescription);

            _keyField = FormLayout.Field("Kurulum &anahtarı", FormLayout.Input(txtKey), "Tireler ve boşluklar yok sayılır.");
            var name = FormLayout.Field("&Kullanıcı adı", FormLayout.Input(txtUserName));
            var password = FormLayout.Field(
                "Ş&ifre",
                FormLayout.Input(txtPassword),
                string.Create(CultureInfo.CurrentCulture, $"En az {PasswordPolicy.MinLength}, en fazla {PasswordPolicy.MaxLength} karakter."));
            var repeat = FormLayout.Field("Şifre &tekrar", FormLayout.Input(txtPasswordRepeat));
            foreach (var field in new[] { _keyField, name, password, repeat })
            {
                field.Margin = new Padding(0, 0, 0, Spacing.M);
            }

            var card = FormLayout.Card("adminCard", "Yönetici hesabı", FormLayout.Stack("adminForm", _keyField, name, password, repeat), fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Stack("setupStack", note, card));

            AcceptButton = btnCreate;
            CancelButton = btnCancel;
            btnCancel.DialogResult = DialogResult.Cancel;
            SetFooterNote("Hesap oluşunca yeni şifrenizle giriş yaparsınız.");
            SetFooterButtons(btnCreate, btnCancel);
            FinishLayout();
        }
    }
}
