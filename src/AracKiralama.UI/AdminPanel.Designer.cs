namespace AracKiralama.UI
{
    partial class AdminPanel
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
            components = new System.ComponentModel.Container();
            btnRefresh = new Button();
            btnUsers = new Button();
            btnRentals = new Button();
            btnAddCar = new Button();
            btnSignOut = new Button();
            btnUpdateCar = new Button();
            btnDeleteCar = new Button();
            dataGridView1 = new DataGridView();
            txtId = new TextBox();
            txtBrand = new TextBox();
            txtModel = new TextBox();
            cmbYear = new ComboBox();
            cmbTransmission = new ComboBox();
            cmbFuelType = new ComboBox();
            txtMileage = new TextBox();
            txtColor = new TextBox();
            txtPlate = new TextBox();
            txtDailyPrice = new TextBox();
            txtStatus = new TextBox();
            chkListed = new CheckBox();
            txtImageUrl = new TextBox();
            picCar = new PictureBox();
            errorProvider1 = new ErrorProvider(components);
            ((System.ComponentModel.ISupportInitialize)dataGridView1).BeginInit();
            ((System.ComponentModel.ISupportInitialize)picCar).BeginInit();
            ((System.ComponentModel.ISupportInitialize)errorProvider1).BeginInit();
            SuspendLayout();

            SetButton(btnRefresh, "btnRefresh", "Araç Durumu", new Point(12, 12));
            btnRefresh.Click += btnRefresh_Click;
            SetButton(btnUsers, "btnUsers", "Kullanıcı Paneli", new Point(228, 12));
            btnUsers.Click += btnUsers_Click;
            SetButton(btnRentals, "btnRentals", "Kiralama Paneli", new Point(444, 12));
            btnRentals.Click += btnRentals_Click;
            SetButton(btnAddCar, "btnAddCar", "Araç Ekle", new Point(741, 12));
            btnAddCar.Click += btnAddCar_Click;
            SetButton(btnSignOut, "btnSignOut", "Panelden Çıkış Yap", new Point(1052, 12));
            btnSignOut.Click += btnSignOut_Click;
            SetButton(btnUpdateCar, "btnUpdateCar", "Aracı Güncelle", new Point(755, 520));
            btnUpdateCar.Click += btnUpdateCar_Click;
            SetButton(btnDeleteCar, "btnDeleteCar", "Aracı Sil", new Point(995, 520));
            btnDeleteCar.Click += btnDeleteCar_Click;
            //
            // dataGridView1
            //
            dataGridView1.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridView1.Location = new Point(12, 70);
            dataGridView1.Name = "dataGridView1";
            dataGridView1.Size = new Size(668, 502);
            dataGridView1.TabIndex = 7;
            dataGridView1.CellClick += dataGridView1_CellClick;
            dataGridView1.SelectionChanged += dataGridView1_SelectionChanged;

            AddField("Araç ID :", txtId, "txtId", 80);
            txtId.ReadOnly = true;
            AddField("Marka :", txtBrand, "txtBrand", 112);
            AddField("Model :", txtModel, "txtModel", 144);
            AddField("Yıl :", cmbYear, "cmbYear", 176);
            AddField("Vites Tipi :", cmbTransmission, "cmbTransmission", 208);
            AddField("Yakıt Tipi :", cmbFuelType, "cmbFuelType", 240);
            AddField("Km :", txtMileage, "txtMileage", 272);
            AddField("Renk :", txtColor, "txtColor", 304);
            AddField("Plaka :", txtPlate, "txtPlate", 336);
            AddField("Günlük Ücret :", txtDailyPrice, "txtDailyPrice", 368);
            AddField("Durum :", txtStatus, "txtStatus", 400);
            txtStatus.ReadOnly = true;
            //
            // chkListed
            //
            chkListed.AutoSize = true;
            chkListed.Checked = true;
            chkListed.Location = new Point(805, 430);
            chkListed.Name = "chkListed";
            chkListed.TabIndex = 30;
            chkListed.Text = "Kiralamaya açık";
            chkListed.UseVisualStyleBackColor = true;
            AddField("Resim Linki :", txtImageUrl, "txtImageUrl", 460);
            //
            // picCar
            //
            picCar.Location = new Point(990, 180);
            picCar.Name = "picCar";
            picCar.Size = new Size(284, 184);
            picCar.SizeMode = PictureBoxSizeMode.Zoom;
            picCar.TabStop = false;
            //
            // errorProvider1
            //
            errorProvider1.ContainerControl = this;
            //
            // AdminPanel
            //
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1290, 590);
            Controls.AddRange([btnRefresh, btnUsers, btnRentals, btnAddCar, btnSignOut, btnUpdateCar, btnDeleteCar, dataGridView1, chkListed, picCar]);
            Name = "AdminPanel";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Yönetim Paneli";
            ((System.ComponentModel.ISupportInitialize)dataGridView1).EndInit();
            ((System.ComponentModel.ISupportInitialize)picCar).EndInit();
            ((System.ComponentModel.ISupportInitialize)errorProvider1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        private static void SetButton(Button button, string name, string text, Point location)
        {
            button.Location = location;
            button.Name = name;
            button.Size = new Size(210, 52);
            button.Text = text;
            button.UseVisualStyleBackColor = true;
        }

        private void AddField(string caption, Control field, string name, int top)
        {
            var label = new Label
            {
                AutoSize = false,
                Location = new Point(690, top + 2),
                Size = new Size(108, 18),
                TextAlign = ContentAlignment.MiddleRight,
                Text = caption,
                Font = new Font("Microsoft Sans Serif", 10F),
            };
            field.Location = new Point(805, top);
            field.Name = name;
            field.Size = new Size(160, 20);
            Controls.Add(label);
            Controls.Add(field);
        }

        #endregion

        private Button btnRefresh;
        private Button btnUsers;
        private Button btnRentals;
        private Button btnAddCar;
        private Button btnSignOut;
        private Button btnUpdateCar;
        private Button btnDeleteCar;
        private DataGridView dataGridView1;
        private TextBox txtId;
        private TextBox txtBrand;
        private TextBox txtModel;
        private ComboBox cmbYear;
        private ComboBox cmbTransmission;
        private ComboBox cmbFuelType;
        private TextBox txtMileage;
        private TextBox txtColor;
        private TextBox txtPlate;
        private TextBox txtDailyPrice;
        private TextBox txtStatus;
        private CheckBox chkListed;
        private TextBox txtImageUrl;
        private PictureBox picCar;
        private ErrorProvider errorProvider1;
    }
}
