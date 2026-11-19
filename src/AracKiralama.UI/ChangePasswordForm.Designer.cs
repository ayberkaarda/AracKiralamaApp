namespace AracKiralama.UI
{
    partial class ChangePasswordForm
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
            lblCurrent = new Label();
            lblNew = new Label();
            lblRepeat = new Label();
            txtCurrentPassword = new TextBox();
            txtNewPassword = new TextBox();
            txtNewPasswordRepeat = new TextBox();
            btnChange = new Button();
            SuspendLayout();
            //
            // lblCurrent
            //
            lblCurrent.AutoSize = true;
            lblCurrent.Location = new Point(33, 80);
            lblCurrent.Name = "lblCurrent";
            lblCurrent.TabIndex = 0;
            lblCurrent.Text = "Mevcut Şifre:";
            //
            // txtCurrentPassword
            //
            txtCurrentPassword.Location = new Point(145, 76);
            txtCurrentPassword.Name = "txtCurrentPassword";
            txtCurrentPassword.Size = new Size(160, 22);
            txtCurrentPassword.TabIndex = 1;
            txtCurrentPassword.UseSystemPasswordChar = true;
            //
            // lblNew
            //
            lblNew.AutoSize = true;
            lblNew.Location = new Point(53, 130);
            lblNew.Name = "lblNew";
            lblNew.TabIndex = 2;
            lblNew.Text = "Yeni Şifre:";
            //
            // txtNewPassword
            //
            txtNewPassword.Location = new Point(145, 127);
            txtNewPassword.Name = "txtNewPassword";
            txtNewPassword.Size = new Size(160, 22);
            txtNewPassword.TabIndex = 3;
            txtNewPassword.UseSystemPasswordChar = true;
            //
            // lblRepeat
            //
            lblRepeat.AutoSize = true;
            lblRepeat.Location = new Point(8, 175);
            lblRepeat.Name = "lblRepeat";
            lblRepeat.TabIndex = 4;
            lblRepeat.Text = "Yeni Şifre Tekrar:";
            //
            // txtNewPasswordRepeat
            //
            txtNewPasswordRepeat.Location = new Point(145, 171);
            txtNewPasswordRepeat.Name = "txtNewPasswordRepeat";
            txtNewPasswordRepeat.Size = new Size(160, 22);
            txtNewPasswordRepeat.TabIndex = 5;
            txtNewPasswordRepeat.UseSystemPasswordChar = true;
            //
            // btnChange
            //
            btnChange.Location = new Point(60, 219);
            btnChange.Name = "btnChange";
            btnChange.Size = new Size(230, 47);
            btnChange.TabIndex = 6;
            btnChange.Text = "Şifre Değiştir";
            btnChange.UseVisualStyleBackColor = true;
            btnChange.Click += btnChange_Click;
            //
            // ChangePasswordForm
            //
            AutoScaleDimensions = new SizeF(8F, 16F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(349, 320);
            Controls.Add(btnChange);
            Controls.Add(txtNewPasswordRepeat);
            Controls.Add(lblRepeat);
            Controls.Add(txtNewPassword);
            Controls.Add(lblNew);
            Controls.Add(txtCurrentPassword);
            Controls.Add(lblCurrent);
            Font = new Font("Microsoft Sans Serif", 9.75F);
            Name = "ChangePasswordForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Şifre Değiştir";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label lblCurrent;
        private Label lblNew;
        private Label lblRepeat;
        private TextBox txtCurrentPassword;
        private TextBox txtNewPassword;
        private TextBox txtNewPasswordRepeat;
        private Button btnChange;
    }
}
