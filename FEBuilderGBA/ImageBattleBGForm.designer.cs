namespace FEBuilderGBA
{
    partial class ImageBattleBGForm
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
            this.label3 = new System.Windows.Forms.Label();
            this.ImportButton = new System.Windows.Forms.Button();
            this.panel2 = new System.Windows.Forms.Panel();
            this.label5 = new System.Windows.Forms.Label();
            this.X_REF = new FEBuilderGBA.ListBoxEx();
            this.Comment = new FEBuilderGBA.TextBoxEx();
            this.label4 = new System.Windows.Forms.Label();
            this.D0 = new System.Windows.Forms.NumericUpDown();
            this.D4 = new System.Windows.Forms.NumericUpDown();
            this.D8 = new System.Windows.Forms.NumericUpDown();
            this.label8 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.X_BG_PIC = new FEBuilderGBA.InterpolatedPictureBox();
            this.AddressPanel = new System.Windows.Forms.Panel();
            this.BlockSize = new FEBuilderGBA.TextBoxEx();
            this.SelectAddress = new FEBuilderGBA.TextBoxEx();
            this.label22 = new System.Windows.Forms.Label();
            this.WriteButton = new System.Windows.Forms.Button();
            this.Address = new System.Windows.Forms.NumericUpDown();
            this.label23 = new System.Windows.Forms.Label();
            this.ExportButton = new System.Windows.Forms.Button();
            this.panel3 = new System.Windows.Forms.Panel();
            this.GraphicsToolButton = new System.Windows.Forms.Button();
            this.DecreaseColorTSAToolButton = new System.Windows.Forms.Button();
            this.ReloadListButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.ReadCount = new System.Windows.Forms.NumericUpDown();
            this.ReadStartAddress = new System.Windows.Forms.NumericUpDown();
            this.panel6 = new System.Windows.Forms.Panel();
            this.AddressListExpandsButton_255 = new System.Windows.Forms.Button();
            this.LabelFilter = new System.Windows.Forms.Label();
            this.AddressList = new FEBuilderGBA.ListBoxEx();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.D0)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.D4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.D8)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_BG_PIC)).BeginInit();
            this.AddressPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).BeginInit();
            this.panel3.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).BeginInit();
            this.panel6.SuspendLayout();
            this.SuspendLayout();
            // 
            // label3
            // 
            this.label3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label3.Location = new System.Drawing.Point(150, -1);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(46, 21);
            this.label3.TabIndex = 52;
            this.label3.Text = "Size:";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ImportButton
            // 
            this.ImportButton.Location = new System.Drawing.Point(5, 9);
            this.ImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ImportButton.Name = "ImportButton";
            this.ImportButton.Size = new System.Drawing.Size(107, 20);
            this.ImportButton.TabIndex = 59;
            this.ImportButton.Text = "画像読込";
            this.ImportButton.UseVisualStyleBackColor = true;
            this.ImportButton.Click += new System.EventHandler(this.ImportButton_Click);
            // 
            // panel2
            // 
            this.panel2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel2.Controls.Add(this.SelectSourceButton);
            this.panel2.Controls.Add(this.OpenSourceButton);
            this.panel2.Controls.Add(this.label5);
            this.panel2.Controls.Add(this.X_REF);
            this.panel2.Controls.Add(this.Comment);
            this.panel2.Controls.Add(this.label4);
            this.panel2.Controls.Add(this.D0);
            this.panel2.Controls.Add(this.D4);
            this.panel2.Controls.Add(this.D8);
            this.panel2.Controls.Add(this.label8);
            this.panel2.Controls.Add(this.label6);
            this.panel2.Controls.Add(this.label7);
            this.panel2.Controls.Add(this.X_BG_PIC);
            this.panel2.Location = new System.Drawing.Point(182, 51);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(690, 314);
            this.panel2.TabIndex = 1;
            // 
            // label5
            // 
            this.label5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label5.Location = new System.Drawing.Point(517, 5);
            this.label5.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(172, 18);
            this.label5.TabIndex = 202;
            this.label5.Text = "参照箇所";
            this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // X_REF
            // 
            this.X_REF.FormattingEnabled = true;
            this.X_REF.IntegralHeight = false;
            this.X_REF.Location = new System.Drawing.Point(517, 22);
            this.X_REF.Name = "X_REF";
            this.X_REF.Size = new System.Drawing.Size(172, 289);
            this.X_REF.TabIndex = 201;
            this.X_REF.KeyDown += new System.Windows.Forms.KeyEventHandler(this.X_REF_KeyDown);
            this.X_REF.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.X_REF_MouseDoubleClick);
            // 
            // Comment
            // 
            this.Comment.ErrorMessage = "";
            this.Comment.Location = new System.Drawing.Point(109, 101);
            this.Comment.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Comment.Name = "Comment";
            this.Comment.Placeholder = "";
            this.Comment.Size = new System.Drawing.Size(149, 20);
            this.Comment.TabIndex = 200;
            // 
            // label4
            // 
            this.label4.AccessibleDescription = "@COMMENT";
            this.label4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label4.Location = new System.Drawing.Point(5, 99);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(99, 21);
            this.label4.TabIndex = 199;
            this.label4.Text = "コメント";
            this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // D0
            // 
            this.D0.Hexadecimal = true;
            this.D0.Location = new System.Drawing.Point(107, 7);
            this.D0.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.D0.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.D0.Name = "D0";
            this.D0.Size = new System.Drawing.Size(83, 20);
            this.D0.TabIndex = 0;
            // 
            // D4
            // 
            this.D4.Hexadecimal = true;
            this.D4.Location = new System.Drawing.Point(107, 27);
            this.D4.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.D4.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.D4.Name = "D4";
            this.D4.Size = new System.Drawing.Size(83, 20);
            this.D4.TabIndex = 1;
            // 
            // D8
            // 
            this.D8.Hexadecimal = true;
            this.D8.Location = new System.Drawing.Point(107, 47);
            this.D8.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.D8.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.D8.Name = "D8";
            this.D8.Size = new System.Drawing.Size(83, 20);
            this.D8.TabIndex = 2;
            // 
            // label8
            // 
            this.label8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label8.Location = new System.Drawing.Point(5, 47);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(99, 21);
            this.label8.TabIndex = 70;
            this.label8.Text = "パレット";
            this.label8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label6
            // 
            this.label6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label6.Location = new System.Drawing.Point(5, 27);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(99, 21);
            this.label6.TabIndex = 69;
            this.label6.Text = "TSA";
            this.label6.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label7
            // 
            this.label7.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label7.Location = new System.Drawing.Point(5, 7);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(99, 21);
            this.label7.TabIndex = 68;
            this.label7.Text = "画像";
            this.label7.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_BG_PIC
            // 
            this.X_BG_PIC.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_BG_PIC.Location = new System.Drawing.Point(260, 5);
            this.X_BG_PIC.Name = "X_BG_PIC";
            this.X_BG_PIC.Size = new System.Drawing.Size(256, 192);
            this.X_BG_PIC.TabIndex = 67;
            this.X_BG_PIC.TabStop = false;
            // 
            // AddressPanel
            // 
            this.AddressPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.AddressPanel.Controls.Add(this.BlockSize);
            this.AddressPanel.Controls.Add(this.label3);
            this.AddressPanel.Controls.Add(this.SelectAddress);
            this.AddressPanel.Controls.Add(this.label22);
            this.AddressPanel.Controls.Add(this.WriteButton);
            this.AddressPanel.Controls.Add(this.Address);
            this.AddressPanel.Controls.Add(this.label23);
            this.AddressPanel.Location = new System.Drawing.Point(182, 31);
            this.AddressPanel.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.AddressPanel.Name = "AddressPanel";
            this.AddressPanel.Size = new System.Drawing.Size(690, 21);
            this.AddressPanel.TabIndex = 70;
            // 
            // BlockSize
            // 
            this.BlockSize.ErrorMessage = "";
            this.BlockSize.Location = new System.Drawing.Point(199, -1);
            this.BlockSize.Name = "BlockSize";
            this.BlockSize.Placeholder = "";
            this.BlockSize.ReadOnly = true;
            this.BlockSize.Size = new System.Drawing.Size(55, 20);
            this.BlockSize.TabIndex = 52;
            // 
            // SelectAddress
            // 
            this.SelectAddress.ErrorMessage = "";
            this.SelectAddress.Location = new System.Drawing.Point(340, 0);
            this.SelectAddress.Name = "SelectAddress";
            this.SelectAddress.Placeholder = "";
            this.SelectAddress.ReadOnly = true;
            this.SelectAddress.Size = new System.Drawing.Size(89, 20);
            this.SelectAddress.TabIndex = 40;
            // 
            // label22
            // 
            this.label22.AccessibleDescription = "@SELECTION_ADDRESS";
            this.label22.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label22.Location = new System.Drawing.Point(261, -1);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(79, 21);
            this.label22.TabIndex = 39;
            this.label22.Text = "選択アドレス:";
            this.label22.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WriteButton
            // 
            this.WriteButton.Location = new System.Drawing.Point(540, -1);
            this.WriteButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(147, 20);
            this.WriteButton.TabIndex = 9;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            // 
            // Address
            // 
            this.Address.Hexadecimal = true;
            this.Address.Location = new System.Drawing.Point(59, 3);
            this.Address.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.Address.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.Address.Name = "Address";
            this.Address.Size = new System.Drawing.Size(83, 20);
            this.Address.TabIndex = 0;
            // 
            // label23
            // 
            this.label23.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label23.Location = new System.Drawing.Point(-1, 0);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(56, 22);
            this.label23.TabIndex = 1;
            this.label23.Text = "アドレス";
            this.label23.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ExportButton
            // 
            this.ExportButton.Location = new System.Drawing.Point(127, 9);
            this.ExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ExportButton.Name = "ExportButton";
            this.ExportButton.Size = new System.Drawing.Size(107, 20);
            this.ExportButton.TabIndex = 60;
            this.ExportButton.Text = "画像取出し";
            this.ExportButton.UseVisualStyleBackColor = true;
            this.ExportButton.Click += new System.EventHandler(this.ExportButton_Click);
            // 
            // panel3
            // 
            this.panel3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel3.Controls.Add(this.GraphicsToolButton);
            this.panel3.Controls.Add(this.DecreaseColorTSAToolButton);
            this.panel3.Controls.Add(this.ExportButton);
            this.panel3.Controls.Add(this.ImportButton);
            this.panel3.Location = new System.Drawing.Point(182, 371);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(690, 29);
            this.panel3.TabIndex = 73;
            // 
            // GraphicsToolButton
            // 
            this.GraphicsToolButton.Location = new System.Drawing.Point(517, 6);
            this.GraphicsToolButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.GraphicsToolButton.Name = "GraphicsToolButton";
            this.GraphicsToolButton.Size = new System.Drawing.Size(141, 20);
            this.GraphicsToolButton.TabIndex = 203;
            this.GraphicsToolButton.Text = "グラフィックツール";
            this.GraphicsToolButton.UseVisualStyleBackColor = true;
            this.GraphicsToolButton.Click += new System.EventHandler(this.GraphicsToolButton_Click);
            // 
            // DecreaseColorTSAToolButton
            // 
            this.DecreaseColorTSAToolButton.Location = new System.Drawing.Point(370, 6);
            this.DecreaseColorTSAToolButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.DecreaseColorTSAToolButton.Name = "DecreaseColorTSAToolButton";
            this.DecreaseColorTSAToolButton.Size = new System.Drawing.Size(137, 20);
            this.DecreaseColorTSAToolButton.TabIndex = 71;
            this.DecreaseColorTSAToolButton.Text = "減色ツール";
            this.DecreaseColorTSAToolButton.UseVisualStyleBackColor = true;
            this.DecreaseColorTSAToolButton.Click += new System.EventHandler(this.DecreaseColorTSAToolButton_Click);
            // 
            // ReloadListButton
            // 
            this.ReloadListButton.Location = new System.Drawing.Point(321, -1);
            this.ReloadListButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReloadListButton.Name = "ReloadListButton";
            this.ReloadListButton.Size = new System.Drawing.Size(71, 21);
            this.ReloadListButton.TabIndex = 25;
            this.ReloadListButton.Text = "再取得";
            this.ReloadListButton.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label1.Location = new System.Drawing.Point(-1, -1);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(75, 22);
            this.label1.TabIndex = 23;
            this.label1.Text = "先頭アドレス";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label2
            // 
            this.label2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label2.Location = new System.Drawing.Point(172, -1);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(56, 22);
            this.label2.TabIndex = 24;
            this.label2.Text = "読込数";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panel1
            // 
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.ReloadListButton);
            this.panel1.Controls.Add(this.label1);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Controls.Add(this.ReadCount);
            this.panel1.Controls.Add(this.ReadStartAddress);
            this.panel1.Location = new System.Drawing.Point(9, 12);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(862, 21);
            this.panel1.TabIndex = 71;
            // 
            // ReadCount
            // 
            this.ReadCount.Location = new System.Drawing.Point(232, 1);
            this.ReadCount.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReadCount.Name = "ReadCount";
            this.ReadCount.Size = new System.Drawing.Size(51, 20);
            this.ReadCount.TabIndex = 28;
            // 
            // ReadStartAddress
            // 
            this.ReadStartAddress.Hexadecimal = true;
            this.ReadStartAddress.Location = new System.Drawing.Point(82, 1);
            this.ReadStartAddress.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReadStartAddress.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.ReadStartAddress.Name = "ReadStartAddress";
            this.ReadStartAddress.Size = new System.Drawing.Size(83, 20);
            this.ReadStartAddress.TabIndex = 0;
            // 
            // panel6
            // 
            this.panel6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel6.Controls.Add(this.AddressListExpandsButton_255);
            this.panel6.Controls.Add(this.LabelFilter);
            this.panel6.Controls.Add(this.AddressList);
            this.panel6.Location = new System.Drawing.Point(9, 31);
            this.panel6.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.panel6.Name = "panel6";
            this.panel6.Size = new System.Drawing.Size(169, 369);
            this.panel6.TabIndex = 71;
            // 
            // AddressListExpandsButton_255
            // 
            this.AddressListExpandsButton_255.Location = new System.Drawing.Point(2, 347);
            this.AddressListExpandsButton_255.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.AddressListExpandsButton_255.Name = "AddressListExpandsButton_255";
            this.AddressListExpandsButton_255.Size = new System.Drawing.Size(160, 20);
            this.AddressListExpandsButton_255.TabIndex = 114;
            this.AddressListExpandsButton_255.Text = "リストの拡張";
            this.AddressListExpandsButton_255.UseVisualStyleBackColor = true;
            // 
            // LabelFilter
            // 
            this.LabelFilter.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.LabelFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.LabelFilter.Location = new System.Drawing.Point(0, -1);
            this.LabelFilter.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.LabelFilter.Name = "LabelFilter";
            this.LabelFilter.Size = new System.Drawing.Size(168, 18);
            this.LabelFilter.TabIndex = 55;
            this.LabelFilter.Text = "名前";
            this.LabelFilter.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AddressList
            // 
            this.AddressList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.AddressList.FormattingEnabled = true;
            this.AddressList.IntegralHeight = false;
            this.AddressList.Location = new System.Drawing.Point(0, 16);
            this.AddressList.Name = "AddressList";
            this.AddressList.Size = new System.Drawing.Size(169, 328);
            this.AddressList.TabIndex = 0;
            this.AddressList.SelectedIndexChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(260, 203);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(115, 22);
            this.OpenSourceButton.TabIndex = 203;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(384, 203);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(122, 21);
            this.SelectSourceButton.TabIndex = 204;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // ImageBattleBGForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(837, 403);
            this.Controls.Add(this.panel6);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.AddressPanel);
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.panel1);
            this.Name = "ImageBattleBGForm";
            this.Text = "戦闘背景";
            this.Load += new System.EventHandler(this.ImageBattleBGForm_Load);
            this.panel2.ResumeLayout(false);
            this.panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.D0)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.D4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.D8)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_BG_PIC)).EndInit();
            this.AddressPanel.ResumeLayout(false);
            this.AddressPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).EndInit();
            this.panel3.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).EndInit();
            this.panel6.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button ImportButton;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.NumericUpDown D0;
        private System.Windows.Forms.NumericUpDown D4;
        private System.Windows.Forms.NumericUpDown D8;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private FEBuilderGBA.TextBoxEx BlockSize;
        private System.Windows.Forms.Panel AddressPanel;
        private FEBuilderGBA.TextBoxEx SelectAddress;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.Button WriteButton;
        private System.Windows.Forms.NumericUpDown Address;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.Button ExportButton;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.Button ReloadListButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.NumericUpDown ReadCount;
        private System.Windows.Forms.NumericUpDown ReadStartAddress;
        private System.Windows.Forms.Panel panel6;
        private System.Windows.Forms.Label LabelFilter;
        private ListBoxEx AddressList;
        private System.Windows.Forms.Button AddressListExpandsButton_255;
        private InterpolatedPictureBox X_BG_PIC;
        private System.Windows.Forms.Button DecreaseColorTSAToolButton;
        private TextBoxEx Comment;
        private System.Windows.Forms.Label label4;
        private ListBoxEx X_REF;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Button GraphicsToolButton;
        private System.Windows.Forms.Button SelectSourceButton;
        private System.Windows.Forms.Button OpenSourceButton;
    }
}