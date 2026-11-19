namespace AracKiralama.UI
{
    partial class CarListForm
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
            SuspendLayout();
            //
            // CarListForm
            //
            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1200, 726);
            Name = "CarListForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Araç Kirala";
            ResumeLayout(false);
        }

        #endregion
    }
}
