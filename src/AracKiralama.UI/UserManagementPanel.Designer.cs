namespace AracKiralama.UI
{
    partial class UserManagementPanel
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            btnAddUser = new Button();
            btnClose = new Button();
            btnUpdateUser = new Button();
            btnDeleteUser = new Button();
            dataGridView1 = new DataGridView();
            txtId = new TextBox();
            txtUserName = new TextBox();
            txtPassword = new TextBox();
            txtEmail = new TextBox();
            txtPhone = new TextBox();
            cmbRole = new ComboBox();
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            SuspendLayout();

            SetButton(btnAddUser, "btnAddUser", "Kullanıcı Ekle", new Point(599, 36));
            btnAddUser.Click += btnAddUser_Click;
            SetButton(btnClose, "btnClose", "Kullanıcı Yönetim Panelinden Çıkış Yap", new Point(975, 36));
            btnClose.Click += btnClose_Click;
            SetButton(btnUpdateUser, "btnUpdateUser", "Kullanıcı Bilgilerini Güncelle", new Point(925, 380));
            btnUpdateUser.Click += btnUpdateUser_Click;
            SetButton(btnDeleteUser, "btnDeleteUser", "Kullanıcıyı Sil", new Point(925, 452));
            btnDeleteUser.Click += btnDeleteUser_Click;
            //
            // dataGridView1
            //
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridView1.Location = new Point(16, 107);
            dataGridView1.Name = "dataGridView1";
            dataGridView1.Size = new Size(863, 409);
            dataGridView1.TabIndex = 4;
            dataGridView1.CellClick += dataGridView1_CellClick;

            AddField("Kullanıcı ID :", txtId, "txtId", 123);
            txtId.ReadOnly = true;
            AddField("Kullanıcı Adı :", txtUserName, "txtUserName", 166);
            AddField("Yeni Şifre :", txtPassword, "txtPassword", 210);
            AddField("E-Posta :", txtEmail, "txtEmail", 256);
            AddField("Telefon :", txtPhone, "txtPhone", 300);
            AddField("Rol :", cmbRole, "cmbRole", 336);
            //
            // UserManagementPanel
            //
            AutoScaleDimensions = new SizeF(8F, 16F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1271, 542);
            Controls.AddRange([btnAddUser, btnClose, btnUpdateUser, btnDeleteUser, dataGridView1]);
            Font = new Font("Microsoft Sans Serif", 9.75F);
            Name = "UserManagementPanel";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Kullanıcı Yönetim Paneli";
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private static void SetButton(Button button, string name, string text, Point location)
        {
            button.Location = location;
            button.Name = name;
            button.Size = new Size(280, 64);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
        }

        private void AddField(string caption, Control field, string name, int top)
        {
            var label = new Label
            {
                AutoSize = false,
                Location = new Point(890, top + 2),
                Size = new Size(130, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Text = caption,
            };
            field.Location = new Point(1029, top);
            field.Name = name;
            field.Size = new Size(194, 22);
            Controls.Add(label);
            Controls.Add(field);
        }

        #endregion

        private Button btnAddUser;
        private Button btnClose;
        private Button btnUpdateUser;
        private Button btnDeleteUser;
        private DataGridView dataGridView1;
        private TextBox txtId;
        private TextBox txtUserName;
        private TextBox txtPassword;
        private TextBox txtEmail;
        private TextBox txtPhone;
        private ComboBox cmbRole;
    }
}
