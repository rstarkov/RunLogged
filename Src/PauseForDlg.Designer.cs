namespace RunLogged
{
    partial class PauseForDlg
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
            this.lblPauseFor = new System.Windows.Forms.Label();
            this.cmbPauseFor = new System.Windows.Forms.ComboBox();
            this.numPauseFor = new System.Windows.Forms.NumericUpDown();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize) (this.numPauseFor)).BeginInit();
            this.SuspendLayout();
            // 
            // lblPauseFor
            // 
            this.lblPauseFor.AutoSize = true;
            this.lblPauseFor.Location = new System.Drawing.Point(12, 19);
            this.lblPauseFor.Name = "lblPauseFor";
            this.lblPauseFor.Size = new System.Drawing.Size(55, 13);
            this.lblPauseFor.TabIndex = 0;
            this.lblPauseFor.Text = "&Pause for:";
            // 
            // cmbPauseFor
            // 
            this.cmbPauseFor.Anchor = ((System.Windows.Forms.AnchorStyles) (((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbPauseFor.FormattingEnabled = true;
            this.cmbPauseFor.Location = new System.Drawing.Point(175, 16);
            this.cmbPauseFor.Name = "cmbPauseFor";
            this.cmbPauseFor.Size = new System.Drawing.Size(253, 21);
            this.cmbPauseFor.TabIndex = 2;
            this.cmbPauseFor.SelectedIndexChanged += new System.EventHandler(this.comboBoxChanged);
            // 
            // numPauseFor
            // 
            this.numPauseFor.Location = new System.Drawing.Point(73, 17);
            this.numPauseFor.Maximum = new decimal(new int[] {
            -1,
            -1,
            -1,
            0});
            this.numPauseFor.Name = "numPauseFor";
            this.numPauseFor.Size = new System.Drawing.Size(96, 20);
            this.numPauseFor.TabIndex = 1;
            this.numPauseFor.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // btnOK
            // 
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOK.Location = new System.Drawing.Point(272, 64);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(75, 23);
            this.btnOK.TabIndex = 3;
            this.btnOK.Text = "&OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.ok);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(353, 64);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 4;
            this.btnCancel.Text = "&Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // PauseForDlg
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(440, 99);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.numPauseFor);
            this.Controls.Add(this.cmbPauseFor);
            this.Controls.Add(this.lblPauseFor);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "PauseForDlg";
            this.Text = "PauseForDlg";
            ((System.ComponentModel.ISupportInitialize) (this.numPauseFor)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblPauseFor;
        private System.Windows.Forms.ComboBox cmbPauseFor;
        private System.Windows.Forms.NumericUpDown numPauseFor;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}