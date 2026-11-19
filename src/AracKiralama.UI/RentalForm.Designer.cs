namespace AracKiralama.UI
{
    partial class RentalForm
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
            picCar = new PictureBox();
            btnBack = new Button();
            lblBrandCaption = new Label();
            lblBrand = new Label();
            lblModelCaption = new Label();
            lblModel = new Label();
            lblYearCaption = new Label();
            lblYear = new Label();
            lblTransmissionCaption = new Label();
            lblTransmission = new Label();
            lblFuelTypeCaption = new Label();
            lblFuelType = new Label();
            lblColorCaption = new Label();
            lblColor = new Label();
            lblDailyPriceCaption = new Label();
            lblDailyPrice = new Label();
            lblStartCaption = new Label();
            dtpStart = new DateTimePicker();
            lblDaysCaption = new Label();
            btnDecrease = new Button();
            txtDays = new TextBox();
            btnIncrease = new Button();
            lblTotal = new Label();
            btnConfirm = new Button();
            lblBooked = new Label();
            ((System.ComponentModel.ISupportInitialize)picCar).BeginInit();
            SuspendLayout();
            //
            // btnBack
            //
            btnBack.Location = new Point(16, 15);
            btnBack.Name = "btnBack";
            btnBack.Size = new Size(97, 68);
            btnBack.TabIndex = 0;
            btnBack.Text = "Geri Dön";
            btnBack.UseVisualStyleBackColor = true;
            btnBack.Click += btnBack_Click;
            //
            // picCar
            //
            picCar.Location = new Point(16, 106);
            picCar.Name = "picCar";
            picCar.Size = new Size(359, 219);
            picCar.SizeMode = PictureBoxSizeMode.Zoom;
            picCar.TabIndex = 1;
            picCar.TabStop = false;
            //
            // captions and values
            //
            SetCaption(lblBrandCaption, "lblBrandCaption", "Marka:", 112);
            SetValue(lblBrand, "lblBrand", 112);
            SetCaption(lblModelCaption, "lblModelCaption", "Model:", 140);
            SetValue(lblModel, "lblModel", 140);
            SetCaption(lblYearCaption, "lblYearCaption", "Yıl:", 170);
            SetValue(lblYear, "lblYear", 170);
            SetCaption(lblTransmissionCaption, "lblTransmissionCaption", "Vites Tipi:", 203);
            SetValue(lblTransmission, "lblTransmission", 203);
            SetCaption(lblFuelTypeCaption, "lblFuelTypeCaption", "Yakıt Tipi:", 231);
            SetValue(lblFuelType, "lblFuelType", 231);
            SetCaption(lblColorCaption, "lblColorCaption", "Renk:", 260);
            SetValue(lblColor, "lblColor", 260);
            SetCaption(lblDailyPriceCaption, "lblDailyPriceCaption", "Günlük Ücret:", 297);
            SetValue(lblDailyPrice, "lblDailyPrice", 297);
            lblDailyPrice.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Bold);
            //
            // lblStartCaption
            //
            lblStartCaption.AutoSize = true;
            lblStartCaption.Location = new Point(676, 60);
            lblStartCaption.Name = "lblStartCaption";
            lblStartCaption.Text = "Alış Tarihi";
            //
            // dtpStart
            //
            dtpStart.Format = DateTimePickerFormat.Short;
            dtpStart.Location = new Point(676, 82);
            dtpStart.Name = "dtpStart";
            dtpStart.Size = new Size(224, 22);
            dtpStart.TabIndex = 2;
            dtpStart.ValueChanged += dtpStart_ValueChanged;
            //
            // lblDaysCaption
            //
            lblDaysCaption.AutoSize = true;
            lblDaysCaption.Location = new Point(744, 140);
            lblDaysCaption.Name = "lblDaysCaption";
            lblDaysCaption.Text = "Gün Sayısı";
            //
            // btnDecrease
            //
            btnDecrease.Location = new Point(676, 169);
            btnDecrease.Name = "btnDecrease";
            btnDecrease.Size = new Size(37, 28);
            btnDecrease.TabIndex = 3;
            btnDecrease.Text = "-";
            btnDecrease.UseVisualStyleBackColor = true;
            btnDecrease.Click += btnDecrease_Click;
            //
            // txtDays
            //
            txtDays.Location = new Point(721, 171);
            txtDays.Name = "txtDays";
            txtDays.Size = new Size(132, 22);
            txtDays.TabIndex = 4;
            txtDays.TextAlign = HorizontalAlignment.Center;
            txtDays.KeyDown += txtDays_KeyDown;
            txtDays.Leave += txtDays_Leave;
            //
            // btnIncrease
            //
            btnIncrease.Location = new Point(863, 169);
            btnIncrease.Name = "btnIncrease";
            btnIncrease.Size = new Size(37, 28);
            btnIncrease.TabIndex = 5;
            btnIncrease.Text = "+";
            btnIncrease.UseVisualStyleBackColor = true;
            btnIncrease.Click += btnIncrease_Click;
            //
            // lblTotal
            //
            lblTotal.AutoSize = true;
            lblTotal.Font = new Font("Microsoft Sans Serif", 12F, FontStyle.Bold);
            lblTotal.Location = new Point(676, 214);
            lblTotal.Name = "lblTotal";
            lblTotal.Text = string.Empty;
            //
            // btnConfirm
            //
            btnConfirm.Location = new Point(709, 252);
            btnConfirm.Name = "btnConfirm";
            btnConfirm.Size = new Size(160, 32);
            btnConfirm.TabIndex = 6;
            btnConfirm.Text = "Ödemeye Geç";
            btnConfirm.UseVisualStyleBackColor = true;
            btnConfirm.Click += btnConfirm_Click;
            //
            // lblBooked
            //
            lblBooked.Location = new Point(676, 295);
            lblBooked.Name = "lblBooked";
            lblBooked.Size = new Size(290, 90);
            lblBooked.Text = string.Empty;
            //
            // RentalForm
            //
            AutoScaleDimensions = new SizeF(8F, 16F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(977, 398);
            Controls.AddRange(
            [
                btnBack, picCar,
                lblBrandCaption, lblBrand, lblModelCaption, lblModel, lblYearCaption, lblYear,
                lblTransmissionCaption, lblTransmission, lblFuelTypeCaption, lblFuelType, lblColorCaption, lblColor,
                lblDailyPriceCaption, lblDailyPrice,
                lblStartCaption, dtpStart, lblDaysCaption, btnDecrease, txtDays, btnIncrease, lblTotal, btnConfirm, lblBooked,
            ]);
            Font = new Font("Microsoft Sans Serif", 9.75F);
            Name = "RentalForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Kiralama Ekranı";
            ((System.ComponentModel.ISupportInitialize)picCar).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private static void SetCaption(Label label, string name, string text, int top)
        {
            label.AutoSize = true;
            label.Location = new Point(389, top);
            label.Name = name;
            label.Text = text;
        }

        private static void SetValue(Label label, string name, int top)
        {
            label.AutoSize = true;
            label.Location = new Point(490, top);
            label.Name = name;
            label.Text = string.Empty;
        }

        #endregion

        private PictureBox picCar;
        private Button btnBack;
        private Label lblBrandCaption;
        private Label lblBrand;
        private Label lblModelCaption;
        private Label lblModel;
        private Label lblYearCaption;
        private Label lblYear;
        private Label lblTransmissionCaption;
        private Label lblTransmission;
        private Label lblFuelTypeCaption;
        private Label lblFuelType;
        private Label lblColorCaption;
        private Label lblColor;
        private Label lblDailyPriceCaption;
        private Label lblDailyPrice;
        private Label lblStartCaption;
        private DateTimePicker dtpStart;
        private Label lblDaysCaption;
        private Button btnDecrease;
        private TextBox txtDays;
        private Button btnIncrease;
        private Label lblTotal;
        private Button btnConfirm;
        private Label lblBooked;
    }
}
