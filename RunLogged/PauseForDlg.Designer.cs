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
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.numPauseFor = new System.Windows.Forms.NumericUpDown();
            this.ctList = new RT.Util.Controls.ListBoxEx();
            this.btnAdd = new System.Windows.Forms.Button();
            this.btnRemove = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize) (this.numPauseFor)).BeginInit();
            this.SuspendLayout();
            // 
            // lblPauseFor
            // 
            this.lblPauseFor.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lblPauseFor.AutoSize = true;
            this.lblPauseFor.Location = new System.Drawing.Point(12, 360);
            this.lblPauseFor.Name = "lblPauseFor";
            this.lblPauseFor.Size = new System.Drawing.Size(59, 15);
            this.lblPauseFor.TabIndex = 1;
            this.lblPauseFor.Text = "&Pause for:";
            // 
            // cmbPauseFor
            // 
            this.cmbPauseFor.Anchor = ((System.Windows.Forms.AnchorStyles) (((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbPauseFor.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPauseFor.FormattingEnabled = true;
            this.cmbPauseFor.Location = new System.Drawing.Point(162, 357);
            this.cmbPauseFor.Name = "cmbPauseFor";
            this.cmbPauseFor.Size = new System.Drawing.Size(115, 23);
            this.cmbPauseFor.TabIndex = 3;
            this.cmbPauseFor.SelectedIndexChanged += new System.EventHandler(this.comboBoxChanged);
            // 
            // btnOK
            // 
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOK.Location = new System.Drawing.Point(283, 387);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(87, 25);
            this.btnOK.TabIndex = 6;
            this.btnOK.Text = "&OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.ok);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(376, 387);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(87, 25);
            this.btnCancel.TabIndex = 7;
            this.btnCancel.Text = "&Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // numPauseFor
            // 
            this.numPauseFor.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.numPauseFor.Location = new System.Drawing.Point(85, 357);
            this.numPauseFor.Maximum = new decimal(new int[] {
            -1,
            -1,
            -1,
            0});
            this.numPauseFor.Name = "numPauseFor";
            this.numPauseFor.Size = new System.Drawing.Size(71, 23);
            this.numPauseFor.TabIndex = 2;
            this.numPauseFor.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numPauseFor.Enter += new System.EventHandler(this.numPauseForEntered);
            // 
            // ctList
            // 
            this.ctList.Anchor = ((System.Windows.Forms.AnchorStyles) ((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.ctList.FormattingEnabled = true;
            this.ctList.IntegralHeight = false;
            this.ctList.ItemHeight = 15;
            this.ctList.Location = new System.Drawing.Point(12, 12);
            this.ctList.Name = "ctList";
            this.ctList.Size = new System.Drawing.Size(451, 332);
            this.ctList.TabIndex = 0;
            this.ctList.SelectedIndexChanged += new System.EventHandler(this.listChangeSelection);
            this.ctList.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.ctList_MouseDoubleClick);
            // 
            // btnAdd
            // 
            this.btnAdd.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAdd.Location = new System.Drawing.Point(283, 356);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(87, 25);
            this.btnAdd.TabIndex = 4;
            this.btnAdd.Text = "&Add";
            this.btnAdd.UseVisualStyleBackColor = true;
            this.btnAdd.Click += new System.EventHandler(this.add);
            // 
            // btnRemove
            // 
            this.btnRemove.Anchor = ((System.Windows.Forms.AnchorStyles) ((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRemove.Location = new System.Drawing.Point(376, 356);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(87, 25);
            this.btnRemove.TabIndex = 5;
            this.btnRemove.Text = "&Remove";
            this.btnRemove.UseVisualStyleBackColor = true;
            this.btnRemove.Click += new System.EventHandler(this.remove);
            // 
            // PauseForDlg
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(475, 424);
            this.Controls.Add(this.btnRemove);
            this.Controls.Add(this.btnAdd);
            this.Controls.Add(this.ctList);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.numPauseFor);
            this.Controls.Add(this.cmbPauseFor);
            this.Controls.Add(this.lblPauseFor);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte) (204)));
            this.Name = "PauseForDlg";
            this.Text = "Pause process for how long?";
            ((System.ComponentModel.ISupportInitialize) (this.numPauseFor)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblPauseFor;
        private System.Windows.Forms.ComboBox cmbPauseFor;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.NumericUpDown numPauseFor;
        private RT.Util.Controls.ListBoxEx ctList;
        private System.Windows.Forms.Button btnAdd;
        private System.Windows.Forms.Button btnRemove;
    }
}