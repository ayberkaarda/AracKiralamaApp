namespace AracKiralama.UI
{
    partial class FirstAdminSetupForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblBaslik = new System.Windows.Forms.Label();
            this.lblDescription = new System.Windows.Forms.Label();
            this.lblKey = new System.Windows.Forms.Label();
            this.txtKey = new System.Windows.Forms.TextBox();
            this.lblUserName = new System.Windows.Forms.Label();
            this.txtUserName = new System.Windows.Forms.TextBox();
            this.lblPassword = new System.Windows.Forms.Label();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.lblRepeat = new System.Windows.Forms.Label();
            this.txtPasswordRepeat = new System.Windows.Forms.TextBox();
            this.btnCreate = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // lblBaslik
            //
            this.lblBaslik.AutoSize = true;
            this.lblBaslik.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.lblBaslik.Location = new System.Drawing.Point(12, 12);
            this.lblBaslik.Name = "lblBaslik";
            this.lblBaslik.Size = new System.Drawing.Size(100, 21);
            this.lblBaslik.TabIndex = 0;
            this.lblBaslik.Text = "İlk Kurulum";
            //
            // lblDescription
            //
            this.lblDescription.Location = new System.Drawing.Point(13, 42);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(392, 84);
            this.lblDescription.TabIndex = 1;
            this.lblDescription.Text = "Henüz bir yönetici hesabı yok. İlk yönetici hesabını kendi belirleyeceğiniz bir şifreyle oluşturun.";
            //
            // lblKey
            //
            this.lblKey.AutoSize = true;
            this.lblKey.Location = new System.Drawing.Point(13, 135);
            this.lblKey.Name = "lblKey";
            this.lblKey.Size = new System.Drawing.Size(99, 15);
            this.lblKey.TabIndex = 2;
            this.lblKey.Text = "Kurulum Anahtarı";
            //
            // txtKey
            //
            this.txtKey.Location = new System.Drawing.Point(140, 132);
            this.txtKey.Name = "txtKey";
            this.txtKey.Size = new System.Drawing.Size(265, 23);
            this.txtKey.TabIndex = 3;
            this.txtKey.UseSystemPasswordChar = true;
            //
            // lblUserName
            //
            this.lblUserName.AutoSize = true;
            this.lblUserName.Location = new System.Drawing.Point(13, 167);
            this.lblUserName.Name = "lblUserName";
            this.lblUserName.Size = new System.Drawing.Size(80, 15);
            this.lblUserName.TabIndex = 4;
            this.lblUserName.Text = "Kullanıcı Adı";
            //
            // txtUserName
            //
            this.txtUserName.Location = new System.Drawing.Point(140, 164);
            this.txtUserName.Name = "txtUserName";
            this.txtUserName.Size = new System.Drawing.Size(265, 23);
            this.txtUserName.TabIndex = 5;
            //
            // lblPassword
            //
            this.lblPassword.AutoSize = true;
            this.lblPassword.Location = new System.Drawing.Point(13, 199);
            this.lblPassword.Name = "lblPassword";
            this.lblPassword.Size = new System.Drawing.Size(32, 15);
            this.lblPassword.TabIndex = 6;
            this.lblPassword.Text = "Şifre";
            //
            // txtPassword
            //
            this.txtPassword.Location = new System.Drawing.Point(140, 196);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Size = new System.Drawing.Size(265, 23);
            this.txtPassword.TabIndex = 7;
            this.txtPassword.UseSystemPasswordChar = true;
            //
            // lblRepeat
            //
            this.lblRepeat.AutoSize = true;
            this.lblRepeat.Location = new System.Drawing.Point(13, 231);
            this.lblRepeat.Name = "lblRepeat";
            this.lblRepeat.Size = new System.Drawing.Size(76, 15);
            this.lblRepeat.TabIndex = 8;
            this.lblRepeat.Text = "Şifre (Tekrar)";
            //
            // txtPasswordRepeat
            //
            this.txtPasswordRepeat.Location = new System.Drawing.Point(140, 228);
            this.txtPasswordRepeat.Name = "txtPasswordRepeat";
            this.txtPasswordRepeat.Size = new System.Drawing.Size(265, 23);
            this.txtPasswordRepeat.TabIndex = 9;
            this.txtPasswordRepeat.UseSystemPasswordChar = true;
            //
            // btnCreate
            //
            this.btnCreate.Location = new System.Drawing.Point(185, 268);
            this.btnCreate.Name = "btnCreate";
            this.btnCreate.Size = new System.Drawing.Size(120, 30);
            this.btnCreate.TabIndex = 10;
            this.btnCreate.Text = "Hesabı Oluştur";
            this.btnCreate.UseVisualStyleBackColor = true;
            this.btnCreate.Click += new System.EventHandler(this.btnCreate_Click);
            //
            // btnCancel
            //
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(311, 268);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(94, 30);
            this.btnCancel.TabIndex = 11;
            this.btnCancel.Text = "İptal";
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // FirstAdminSetupForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(420, 314);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnCreate);
            this.Controls.Add(this.txtPasswordRepeat);
            this.Controls.Add(this.lblRepeat);
            this.Controls.Add(this.txtPassword);
            this.Controls.Add(this.lblPassword);
            this.Controls.Add(this.txtUserName);
            this.Controls.Add(this.lblUserName);
            this.Controls.Add(this.txtKey);
            this.Controls.Add(this.lblKey);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.lblBaslik);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FirstAdminSetupForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "İlk Kurulum";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblBaslik;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.Label lblKey;
        private System.Windows.Forms.TextBox txtKey;
        private System.Windows.Forms.Label lblUserName;
        private System.Windows.Forms.TextBox txtUserName;
        private System.Windows.Forms.Label lblPassword;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.Label lblRepeat;
        private System.Windows.Forms.TextBox txtPasswordRepeat;
        private System.Windows.Forms.Button btnCreate;
        private System.Windows.Forms.Button btnCancel;
    }
}
