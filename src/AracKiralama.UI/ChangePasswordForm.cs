using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>Change of the signed-in account's own password.</summary>
    public partial class ChangePasswordForm : Form
    {
        private readonly IAuthService _auth;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();

        public ChangePasswordForm(IAuthService auth, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(session);

            _auth = auth;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;
            foreach (var box in new[] { txtCurrentPassword, txtNewPassword, txtNewPasswordRepeat })
            {
                box.MaxLength = PasswordPolicy.MaxLength;
            }

            AcceptButton = btnChange;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Changes the password; ignored while a previous attempt is still running. Returns whether it changed.</summary>
        internal async Task<bool> ChangeAsync()
        {
            var changed = false;
            await this.RunExclusiveAsync(
                _change,
                async () =>
                {
                    try
                    {
                        await _auth.ChangePasswordAsync(_session, txtCurrentPassword.Text, txtNewPassword.Text, txtNewPasswordRepeat.Text);
                        changed = true;
                    }
                    finally
                    {
                        txtCurrentPassword.Clear();
                        if (!changed)
                        {
                            _change.FocusAfterUnlock(txtCurrentPassword);
                        }
                    }
                },
                btnChange);
            return changed;
        }

        private async void btnChange_Click(object? sender, EventArgs e)
        {
            if (await ChangeAsync())
            {
                txtNewPassword.Clear();
                txtNewPasswordRepeat.Clear();
                Prompts.Inform("Şifre başarıyla değiştirildi.");
                Close();
            }
        }
    }
}
