using System.Globalization;
using AracKiralama.Business.Security;
using AracKiralama.Business.Services;
using AracKiralama.Business.Validation;
using AracKiralama.Domain.Entities;
using AracKiralama.UI.Infrastructure;

namespace AracKiralama.UI
{
    /// <summary>
    /// The admin's account management. Passwords are never shown: the password field sets a new one and
    /// may stay empty when an account is changed; e-mail and phone may stay empty, as accounts of the
    /// original application may have neither. The list masks both; the edit fields show them in full.
    /// </summary>
    public partial class UserManagementPanel : Form
    {
        private readonly IAccountService _accounts;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _write = new();
        private readonly RequestSequence _loads = new();

        public UserManagementPanel(IAccountService accounts, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            ArgumentNullException.ThrowIfNull(session);

            _accounts = accounts;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;

            cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRole.ReplaceItems(RoleChoice.All);
            cmbRole.SelectedIndex = -1;
            txtUserName.MaxLength = InputRules.StoredUserNameMaxLength;
            txtPassword.MaxLength = PasswordPolicy.MaxLength;
            txtPassword.UseSystemPasswordChar = true;
            txtEmail.MaxLength = InputRules.EmailMaxLength;
            txtPhone.MaxLength = 12;
            txtPhone.FormatAsPhone();
            dataGridView1.UseReadOnlyList();
        }

        /// <summary>Confirmations and information; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        internal int? SelectedUserId { get; private set; }

        internal IReadOnlyList<UserSummary> ShownUsers { get; private set; } = [];

        internal async Task LoadUsersAsync()
        {
            var request = _loads.Next();
            var users = await _accounts.GetAllAsync(_session);
            if (!_loads.IsLatest(request) || IsDisposed)
            {
                return;
            }

            ShownUsers = users;
            dataGridView1.DataSource = users.Select(u => new UserRow(u)).ToList();
            dataGridView1.ClearSelection();
        }

        internal void SelectUser(UserSummary user)
        {
            ArgumentNullException.ThrowIfNull(user);

            SelectedUserId = user.Id;
            txtId.Text = user.Id.ToString(CultureInfo.InvariantCulture);
            txtUserName.Text = user.UserName;
            txtPassword.Clear();
            txtEmail.Text = user.Email ?? string.Empty;
            txtPhone.Text = user.Phone ?? string.Empty;
            cmbRole.SelectedItem = RoleChoice.All.First(r => r.Role == user.Role);
        }

        internal Task<bool> AddAsync() =>
            WriteAsync(async () =>
            {
                await _accounts.AddAsync(_session, ReadInput());
                Prompts.Inform("Kullanıcı başarıyla eklendi.");
                ClearForm();
                await LoadUsersAsync();
            });

        internal Task<bool> UpdateAsync()
        {
            if (SelectedUserId is not { } id)
            {
                Prompts.Warn("Lütfen güncellemek için bir kullanıcı seçin.");
                return Task.FromResult(false);
            }

            return WriteAsync(async () =>
            {
                await _accounts.UpdateAsync(_session, id, ReadInput());
                Prompts.Inform("Kullanıcı başarıyla güncellendi.");
                ClearForm();
                await LoadUsersAsync();
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
                Prompts.Inform("Kullanıcı başarıyla silindi.");
                ClearForm();
                await LoadUsersAsync();
            });
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            await this.RunAsync(LoadUsersAsync);
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
                btnDeleteUser);

        private AccountInput ReadInput() => new(
            txtUserName.Text,
            txtPassword.Text,
            txtEmail.Text,
            txtPhone.Text,
            (cmbRole.SelectedItem as RoleChoice)?.Role ?? (UserRole)(-1));

        private void ClearForm()
        {
            SelectedUserId = null;
            dataGridView1.ClearSelection();
            txtId.Clear();
            txtUserName.Clear();
            txtPassword.Clear();
            txtEmail.Clear();
            txtPhone.Clear();
            cmbRole.SelectedIndex = -1;
        }

        private void dataGridView1_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && dataGridView1.Rows[e.RowIndex].DataBoundItem is UserRow row)
            {
                SelectUser(row.User);
            }
        }

        private void btnClose_Click(object? sender, EventArgs e) => Close();

        private async void btnUpdateUser_Click(object? sender, EventArgs e) => await UpdateAsync();

        private async void btnDeleteUser_Click(object? sender, EventArgs e) => await DeleteAsync();

        private async void btnAddUser_Click(object? sender, EventArgs e) => await AddAsync();
    }
}
