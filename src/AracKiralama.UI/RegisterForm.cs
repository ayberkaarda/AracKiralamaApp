using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>Sign-up of a new customer account.</summary>
    public partial class RegisterForm : Form
    {
        private readonly IAuthService _auth;
        private readonly ExclusiveOperation _register = new();

        public RegisterForm(IAuthService auth)
        {
            ArgumentNullException.ThrowIfNull(auth);

            _auth = auth;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            txtUserName.MaxLength = InputRules.UserNameMaxLength;
            txtPassword.MaxLength = PasswordPolicy.MaxLength;
            txtEmail.MaxLength = InputRules.EmailMaxLength;
            txtPhone.MaxLength = 12;
            txtPhone.FormatAsPhone();
            AcceptButton = btnRegister;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Registers the account; ignored while a previous attempt is still running. Returns whether it was stored.</summary>
        internal async Task<bool> RegisterAsync()
        {
            MarkEmptyFields();
            var registered = false;
            await this.RunExclusiveAsync(
                _register,
                async () =>
                {
                    try
                    {
                        await _auth.RegisterAsync(txtUserName.Text, txtPassword.Text, txtEmail.Text, txtPhone.Text);
                        registered = true;
                    }
                    finally
                    {
                        if (!registered)
                        {
                            _register.FocusAfterUnlock(txtUserName);
                        }
                    }
                },
                btnRegister);
            return registered;
        }

        /// <summary>Marks the empty fields as the original form did; the service decides what is accepted.</summary>
        private void MarkEmptyFields()
        {
            if (SystemInformation.HighContrast)
            {
                return;
            }

            foreach (var box in new[] { txtUserName, txtPassword, txtEmail, txtPhone })
            {
                box.BackColor = string.IsNullOrWhiteSpace(box.Text) ? Color.LightPink : SystemColors.Window;
            }
        }

        private async void btnRegister_Click(object? sender, EventArgs e)
        {
            if (await RegisterAsync())
            {
                Prompts.Inform("Kayıt başarılı!");
                Close();
            }
        }
    }
}
