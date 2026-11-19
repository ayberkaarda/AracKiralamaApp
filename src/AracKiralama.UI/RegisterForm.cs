using System.Globalization;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// Sign-up of a new customer account. Closes with <see cref="DialogResult.OK"/> once the account was stored;
    /// the sign-in screen then says so and fills in the user name.
    /// </summary>
    public sealed class RegisterForm : AppDialog
    {
        internal const string EmptyField = "Bu alan boş bırakılamaz.";

        private readonly IAuthService _auth;
        private readonly ExclusiveOperation _register = new();

        private readonly TextBox txtUserName = new() { Name = nameof(txtUserName), MaxLength = InputRules.UserNameMaxLength };
        private readonly TextBox txtPassword = new() { Name = nameof(txtPassword), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtEmail = new() { Name = nameof(txtEmail), MaxLength = InputRules.EmailMaxLength };
        private readonly TextBox txtPhone = new() { Name = nameof(txtPhone), MaxLength = 12, PlaceholderText = "555 123 4567" };
        private readonly ThemedButton btnRegister = FormLayout.Button(nameof(btnRegister), "Kayıt Ol", ButtonKind.Primary, Glyphs.AddPerson);
        private readonly ThemedButton btnCancel = FormLayout.Button(nameof(btnCancel), "İptal", ButtonKind.Secondary);
        private readonly List<TextField> _fields = [];

        public RegisterForm(IAuthService auth)
            : base("Hesap oluştur", "Kullanıcı adınızı, şifrenizi ve iletişim bilgilerinizi girin.", Glyphs.AddPerson, new Size(560, 520), new Size(460, 460), modal: true)
        {
            ArgumentNullException.ThrowIfNull(auth);

            _auth = auth;
            txtPhone.FormatAsPhone();
            BuildLayout();
            _register.HoldClosingOf(this);
            btnRegister.Click += async (_, _) => await RegisterAndCloseAsync();
        }

        /// <summary>The user name of the account just stored, for the sign-in screen.</summary>
        internal string? RegisteredUserName { get; private set; }

        /// <summary>Registers the account; ignored while a previous attempt is still running. Returns whether it was stored.</summary>
        internal async Task<bool> RegisterAsync()
        {
            var registered = false;
            await this.RunExclusiveAsync(
                _register,
                async () =>
                {
                    MarkEmptyFields();
                    try
                    {
                        await _auth.RegisterAsync(txtUserName.Text, txtPassword.Text, txtEmail.Text, txtPhone.Text);
                        registered = true;
                        RegisteredUserName = txtUserName.Text.Trim();
                    }
                    finally
                    {
                        if (!registered)
                        {
                            _register.FocusAfterUnlock(txtUserName);
                        }
                    }
                },
                btnRegister,
                btnCancel);
            return registered;
        }

        /// <summary>The fields left empty, marked as the original form marked them; the service decides what is accepted.</summary>
        internal IEnumerable<string> MarkedFields => _fields.Where(f => f.Error is not null).Select(f => f.Input.Name);

        private void MarkEmptyFields()
        {
            foreach (var field in _fields)
            {
                field.Error = string.IsNullOrWhiteSpace(field.Input.Text) ? EmptyField : null;
            }
        }

        private async Task RegisterAndCloseAsync()
        {
            if (await RegisterAsync())
            {
                DialogResult = DialogResult.OK;
            }
        }

        private TextField Track(TextBox box)
        {
            var field = FormLayout.Input(box);
            _fields.Add(field);
            box.TextChanged += (_, _) =>
            {
                if (field.Error is not null && !string.IsNullOrWhiteSpace(box.Text))
                {
                    field.Error = null;
                }
            };
            return field;
        }

        private void BuildLayout()
        {
            Name = nameof(RegisterForm);
            var name = FormLayout.Field("&Kullanıcı adı", Track(txtUserName));
            var password = FormLayout.Field(
                "Ş&ifre",
                Track(txtPassword),
                string.Create(CultureInfo.CurrentCulture, $"En az {PasswordPolicy.MinLength}, en fazla {PasswordPolicy.MaxLength} karakter."));
            var email = FormLayout.Field("&E-posta", Track(txtEmail));
            var phone = FormLayout.Field("&Telefon", Track(txtPhone), "On haneli numara.");
            name.Margin = password.Margin = new Padding(0, 0, 0, Spacing.M);
            email.Margin = phone.Margin = new Padding(0, 0, 0, Spacing.M);
            var contact = FormLayout.FieldRow("contactRow", (email, 55), (phone, 45));
            email.Margin = new Padding(0, 0, Spacing.L, Spacing.M);
            var card = FormLayout.Card("accountCard", null, FormLayout.Stack("accountForm", name, password, contact), fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(card);

            AcceptButton = btnRegister;
            CancelButton = btnCancel;
            btnCancel.DialogResult = DialogResult.Cancel;
            SetFooterNote("Yeni hesaplar müşteri rolüyle açılır.");
            SetFooterButtons(btnRegister, btnCancel);
            FinishLayout();
        }
    }
}
