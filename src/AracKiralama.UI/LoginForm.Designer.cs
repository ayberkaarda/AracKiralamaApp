namespace AracKiralama.UI
{
    partial class LoginForm
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
            lblUserName = new Label();
            lblPassword = new Label();
            txtUserName = new TextBox();
            txtPassword = new TextBox();
            btnSignIn = new Button();
            btnRegister = new Button();
            btnSetup = new Button();
            picHero = new PictureBox();
            lblOr = new Label();
            ((System.ComponentModel.ISupportInitialize)picHero).BeginInit();
            SuspendLayout();
            //
            // lblUserName
            //
            lblUserName.AutoSize = true;
            lblUserName.Location = new Point(400, 185);
            lblUserName.Name = "lblUserName";
            lblUserName.TabIndex = 0;
            lblUserName.Text = "Kullanıcı Adı :";
            //
            // lblPassword
            //
            lblPassword.AutoSize = true;
            lblPassword.Location = new Point(440, 225);
            lblPassword.Name = "lblPassword";
            lblPassword.TabIndex = 2;
            lblPassword.Text = "Şifre:";
            //
            // txtUserName
            //
            txtUserName.Location = new Point(486, 182);
            txtUserName.Name = "txtUserName";
            txtUserName.Size = new Size(140, 20);
            txtUserName.TabIndex = 1;
            //
            // txtPassword
            //
            txtPassword.Location = new Point(486, 222);
            txtPassword.Name = "txtPassword";
            txtPassword.Size = new Size(140, 20);
            txtPassword.TabIndex = 3;
            txtPassword.UseSystemPasswordChar = true;
            //
            // btnSignIn
            //
            btnSignIn.Location = new Point(467, 260);
            btnSignIn.Name = "btnSignIn";
            btnSignIn.Size = new Size(140, 26);
            btnSignIn.TabIndex = 4;
            btnSignIn.Text = "Giriş Yap";
            btnSignIn.UseVisualStyleBackColor = true;
            btnSignIn.Click += btnSignIn_Click;
            //
            // lblOr
            //
            lblOr.AutoSize = true;
            lblOr.Location = new Point(517, 296);
            lblOr.Name = "lblOr";
            lblOr.TabIndex = 5;
            lblOr.Text = "YA DA";
            //
            // btnRegister
            //
            btnRegister.Location = new Point(467, 319);
            btnRegister.Name = "btnRegister";
            btnRegister.Size = new Size(140, 26);
            btnRegister.TabIndex = 6;
            btnRegister.Text = "Hesap Oluştur";
            btnRegister.UseVisualStyleBackColor = true;
            btnRegister.Click += btnRegister_Click;
            //
            // btnSetup
            //
            btnSetup.Location = new Point(437, 360);
            btnSetup.Name = "btnSetup";
            btnSetup.Size = new Size(200, 26);
            btnSetup.TabIndex = 7;
            btnSetup.Text = "İlk yönetici kurulumu";
            btnSetup.UseVisualStyleBackColor = true;
            btnSetup.Visible = false;
            btnSetup.Click += btnSetup_Click;
            //
            // picHero
            //
            picHero.Location = new Point(0, 0);
            picHero.Name = "picHero";
            picHero.Size = new Size(345, 518);
            picHero.SizeMode = PictureBoxSizeMode.StretchImage;
            picHero.TabIndex = 8;
            picHero.TabStop = false;
            //
            // LoginForm
            //
            AcceptButton = btnSignIn;
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.Control;
            ClientSize = new Size(697, 512);
            Controls.Add(lblOr);
            Controls.Add(picHero);
            Controls.Add(btnSetup);
            Controls.Add(btnRegister);
            Controls.Add(btnSignIn);
            Controls.Add(txtPassword);
            Controls.Add(txtUserName);
            Controls.Add(lblPassword);
            Controls.Add(lblUserName);
            Name = "LoginForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Araç Kiralama";
            ((System.ComponentModel.ISupportInitialize)picHero).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblUserName;
        private Label lblPassword;
        private TextBox txtUserName;
        private TextBox txtPassword;
        private Button btnSignIn;
        private Button btnRegister;
        private Button btnSetup;
        private PictureBox picHero;
        private Label lblOr;
    }
}
