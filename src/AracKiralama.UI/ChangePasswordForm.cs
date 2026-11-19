using System.Globalization;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.UI.Infrastructure;
using AracKiralama.UI.Theming;

namespace AracKiralama.UI
{
    /// <summary>
    /// Change of the signed-in account's own password. Closes with <see cref="DialogResult.OK"/> once it changed;
    /// the window that opened it then shows <see cref="ChangedMessage"/>.
    /// </summary>
    public sealed class ChangePasswordForm : AppDialog
    {
        internal const string ChangedMessage =
            "Şifre başarıyla değiştirildi. Güvenliğiniz için hesabınızın başka bilgisayarlarda açık olan oturumları kapatıldı; bu pencere açık kalır.";

        private readonly IAuthService _auth;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();

        private readonly TextBox txtCurrentPassword = new() { Name = nameof(txtCurrentPassword), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtNewPassword = new() { Name = nameof(txtNewPassword), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtNewPasswordRepeat = new() { Name = nameof(txtNewPasswordRepeat), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnChange = FormLayout.Button(nameof(btnChange), "Şifreyi Değiştir", ButtonKind.Primary, Glyphs.Lock);
        private readonly ThemedButton btnCancel = FormLayout.Button(nameof(btnCancel), "İptal", ButtonKind.Secondary);

        public ChangePasswordForm(IAuthService auth, UserSession session)
            : base("Şifre değiştir", "Mevcut şifrenizle yeni bir şifre belirleyin.", Glyphs.Lock, new Size(520, 460), new Size(440, 420), modal: true)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(session);

            _auth = auth;
            _session = session;
            BuildLayout();
            _change.HoldClosingOf(this);
            btnChange.Click += async (_, _) => await ChangeAndCloseAsync();
        }

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
                        if (changed)
                        {
                            txtNewPassword.Clear();
                            txtNewPasswordRepeat.Clear();
                        }
                        else
                        {
                            _change.FocusAfterUnlock(txtCurrentPassword);
                        }
                    }
                },
                btnChange,
                btnCancel);
            return changed;
        }

        private async Task ChangeAndCloseAsync()
        {
            if (await ChangeAsync())
            {
                DialogResult = DialogResult.OK;
            }
        }

        private void BuildLayout()
        {
            Name = nameof(ChangePasswordForm);
            var current = FormLayout.Field("&Mevcut şifre", FormLayout.Input(txtCurrentPassword));
            var next = FormLayout.Field(
                "&Yeni şifre",
                FormLayout.Input(txtNewPassword),
                string.Create(CultureInfo.CurrentCulture, $"En az {PasswordPolicy.MinLength}, en fazla {PasswordPolicy.MaxLength} karakter."));
            var repeat = FormLayout.Field("Yeni şifre &tekrar", FormLayout.Input(txtNewPasswordRepeat));
            current.Margin = next.Margin = repeat.Margin = new Padding(0, 0, 0, Spacing.M);
            var card = FormLayout.Card("passwordCard", null, FormLayout.Stack("passwordForm", current, next, repeat), fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(card);

            AcceptButton = btnChange;
            CancelButton = btnCancel;
            btnCancel.DialogResult = DialogResult.Cancel;
            SetFooterNote("Diğer bilgisayarlardaki oturumlarınız kapanır.");
            SetFooterButtons(btnChange, btnCancel);
            FinishLayout();
        }
    }
}
