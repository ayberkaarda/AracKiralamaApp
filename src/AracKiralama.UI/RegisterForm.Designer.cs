namespace AracKiralama.UI
{
    partial class RegisterForm
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
            txtUserName = new TextBox();
            txtPassword = new TextBox();
            txtEmail = new TextBox();
            txtPhone = new TextBox();
            lblUserName = new Label();
            lblPassword = new Label();
            lblEmail = new Label();
            lblPhone = new Label();
            btnRegister = new Button();
            SuspendLayout();
            //
            // lblUserName
            //
            lblUserName.AutoSize = true;
            lblUserName.Location = new Point(80, 90);
            lblUserName.Name = "lblUserName";
            lblUserName.TabIndex = 0;
            lblUserName.Text = "Kullanıcı Adı:";
            //
            // txtUserName
            //
            txtUserName.Location = new Point(177, 87);
            txtUserName.Name = "txtUserName";
            txtUserName.Size = new Size(160, 22);
            txtUserName.TabIndex = 1;
            //
            // lblPassword
            //
            lblPassword.AutoSize = true;
            lblPassword.Location = new Point(117, 136);
            lblPassword.Name = "lblPassword";
            lblPassword.TabIndex = 2;
            lblPassword.Text = "Şifre:";
            //
            // txtPassword
            //
            txtPassword.Location = new Point(177, 133);
            txtPassword.Name = "txtPassword";
            txtPassword.Size = new Size(160, 22);
            txtPassword.TabIndex = 3;
            txtPassword.UseSystemPasswordChar = true;
            //
            // lblEmail
            //
            lblEmail.AutoSize = true;
            lblEmail.Location = new Point(100, 191);
            lblEmail.Name = "lblEmail";
            lblEmail.TabIndex = 4;
            lblEmail.Text = "E-Posta";
            //
            // txtEmail
            //
            txtEmail.Location = new Point(177, 188);
            txtEmail.Name = "txtEmail";
            txtEmail.Size = new Size(160, 22);
            txtEmail.TabIndex = 5;
            //
            // lblPhone
            //
            lblPhone.AutoSize = true;
            lblPhone.Location = new Point(101, 244);
            lblPhone.Name = "lblPhone";
            lblPhone.TabIndex = 6;
            lblPhone.Text = "Telefon";
            //
            // txtPhone
            //
            txtPhone.Location = new Point(177, 241);
            txtPhone.Name = "txtPhone";
            txtPhone.Size = new Size(160, 22);
            txtPhone.TabIndex = 7;
            //
            // btnRegister
            //
            btnRegister.Location = new Point(177, 287);
            btnRegister.Name = "btnRegister";
            btnRegister.Size = new Size(110, 28);
            btnRegister.TabIndex = 8;
            btnRegister.Text = "Kayıt Ol";
            btnRegister.UseVisualStyleBackColor = true;
            btnRegister.Click += btnRegister_Click;
            //
            // RegisterForm
            //
            AutoScaleDimensions = new SizeF(8F, 16F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(416, 367);
            Controls.Add(btnRegister);
            Controls.Add(txtPhone);
            Controls.Add(lblPhone);
            Controls.Add(txtEmail);
            Controls.Add(lblEmail);
            Controls.Add(txtPassword);
            Controls.Add(lblPassword);
            Controls.Add(txtUserName);
            Controls.Add(lblUserName);
            Font = new Font("Microsoft Sans Serif", 9.75F);
            Name = "RegisterForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Kayıt Ol";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TextBox txtUserName;
        private TextBox txtPassword;
        private TextBox txtEmail;
        private TextBox txtPhone;
        private Label lblUserName;
        private Label lblPassword;
        private Label lblEmail;
        private Label lblPhone;
        private Button btnRegister;
    }
}
