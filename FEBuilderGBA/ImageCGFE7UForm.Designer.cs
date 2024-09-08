namespace FEBuilderGBA
{
    partial class ImageCGFE7UForm
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
            this.label8 = new System.Windows.Forms.Label();
            this.DragTargetPanel = new System.Windows.Forms.Panel();
            this.Comment = new FEBuilderGBA.TextBoxEx();
            this.label6 = new System.Windows.Forms.Label();
            this.DecreaseColorTSAToolButton = new System.Windows.Forms.Button();
            this.ImportButton = new System.Windows.Forms.Button();
            this.ExportButton = new System.Windows.Forms.Button();
            this.L_0_COMBO = new System.Windows.Forms.ComboBox();
            this.J_1 = new System.Windows.Forms.Label();
            this.B3 = new System.Windows.Forms.NumericUpDown();
            this.B2 = new System.Windows.Forms.NumericUpDown();
            this.B1 = new System.Windows.Forms.NumericUpDown();
            this.B0 = new System.Windows.Forms.NumericUpDown();
            this.J_0 = new System.Windows.Forms.Label();
            this.P4 = new System.Windows.Forms.NumericUpDown();
            this.P12 = new System.Windows.Forms.NumericUpDown();
            this.J_12 = new System.Windows.Forms.Label();
            this.P8 = new System.Windows.Forms.NumericUpDown();
            this.J_8 = new System.Windows.Forms.Label();
            this.X_PIC = new FEBuilderGBA.InterpolatedPictureBox();
            this.J_4 = new System.Windows.Forms.Label();
            this.ReloadListButton = new System.Windows.Forms.Button();
            this.WriteButton = new System.Windows.Forms.Button();
            this.panel6 = new System.Windows.Forms.Panel();
            this.AddressListExpandsButton = new System.Windows.Forms.Button();
            this.LabelFilter = new System.Windows.Forms.Label();
            this.AddressList = new FEBuilderGBA.ListBoxEx();
            this.BlockSize = new FEBuilderGBA.TextBoxEx();
            this.label3 = new System.Windows.Forms.Label();
            this.SelectAddress = new FEBuilderGBA.TextBoxEx();
            this.label22 = new System.Windows.Forms.Label();
            this.Address = new System.Windows.Forms.NumericUpDown();
            this.label9 = new System.Windows.Forms.Label();
            this.panel3 = new System.Windows.Forms.Panel();
            this.ReadCount = new System.Windows.Forms.NumericUpDown();
            this.ReadStartAddress = new System.Windows.Forms.NumericUpDown();
            this.label23 = new System.Windows.Forms.Label();
            this.panel5 = new System.Windows.Forms.Panel();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.DragTargetPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.B3)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.B2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.B1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.B0)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P12)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P8)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_PIC)).BeginInit();
            this.panel6.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).BeginInit();
            this.panel3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).BeginInit();
            this.panel5.SuspendLayout();
            this.SuspendLayout();
            // 
            // label8
            // 
            this.label8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label8.Location = new System.Drawing.Point(-1, -1);
            this.label8.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(77, 21);
            this.label8.TabIndex = 23;
            this.label8.Text = "先頭アドレス";
            this.label8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // DragTargetPanel
            // 
            this.DragTargetPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.DragTargetPanel.Controls.Add(this.SelectSourceButton);
            this.DragTargetPanel.Controls.Add(this.OpenSourceButton);
            this.DragTargetPanel.Controls.Add(this.Comment);
            this.DragTargetPanel.Controls.Add(this.label6);
            this.DragTargetPanel.Controls.Add(this.DecreaseColorTSAToolButton);
            this.DragTargetPanel.Controls.Add(this.ImportButton);
            this.DragTargetPanel.Controls.Add(this.ExportButton);
            this.DragTargetPanel.Controls.Add(this.L_0_COMBO);
            this.DragTargetPanel.Controls.Add(this.J_1);
            this.DragTargetPanel.Controls.Add(this.B3);
            this.DragTargetPanel.Controls.Add(this.B2);
            this.DragTargetPanel.Controls.Add(this.B1);
            this.DragTargetPanel.Controls.Add(this.B0);
            this.DragTargetPanel.Controls.Add(this.J_0);
            this.DragTargetPanel.Controls.Add(this.P4);
            this.DragTargetPanel.Controls.Add(this.P12);
            this.DragTargetPanel.Controls.Add(this.J_12);
            this.DragTargetPanel.Controls.Add(this.P8);
            this.DragTargetPanel.Controls.Add(this.J_8);
            this.DragTargetPanel.Controls.Add(this.X_PIC);
            this.DragTargetPanel.Controls.Add(this.J_4);
            this.DragTargetPanel.Location = new System.Drawing.Point(175, 49);
            this.DragTargetPanel.Name = "DragTargetPanel";
            this.DragTargetPanel.Size = new System.Drawing.Size(575, 247);
            this.DragTargetPanel.TabIndex = 88;
            // 
            // Comment
            // 
            this.Comment.ErrorMessage = "";
            this.Comment.Location = new System.Drawing.Point(85, 117);
            this.Comment.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Comment.Name = "Comment";
            this.Comment.Placeholder = "";
            this.Comment.Size = new System.Drawing.Size(159, 20);
            this.Comment.TabIndex = 200;
            // 
            // label6
            // 
            this.label6.AccessibleDescription = "@COMMENT";
            this.label6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label6.Location = new System.Drawing.Point(-1, 115);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(82, 21);
            this.label6.TabIndex = 199;
            this.label6.Text = "コメント";
            this.label6.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // DecreaseColorTSAToolButton
            // 
            this.DecreaseColorTSAToolButton.Location = new System.Drawing.Point(435, 223);
            this.DecreaseColorTSAToolButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.DecreaseColorTSAToolButton.Name = "DecreaseColorTSAToolButton";
            this.DecreaseColorTSAToolButton.Size = new System.Drawing.Size(137, 20);
            this.DecreaseColorTSAToolButton.TabIndex = 68;
            this.DecreaseColorTSAToolButton.Text = "減色ツール";
            this.DecreaseColorTSAToolButton.UseVisualStyleBackColor = true;
            this.DecreaseColorTSAToolButton.Click += new System.EventHandler(this.DecreaseColorTSAToolButton_Click);
            // 
            // ImportButton
            // 
            this.ImportButton.Location = new System.Drawing.Point(3, 223);
            this.ImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ImportButton.Name = "ImportButton";
            this.ImportButton.Size = new System.Drawing.Size(123, 20);
            this.ImportButton.TabIndex = 67;
            this.ImportButton.Text = "画像読込";
            this.ImportButton.UseVisualStyleBackColor = true;
            this.ImportButton.Click += new System.EventHandler(this.ImportButton_Click);
            // 
            // ExportButton
            // 
            this.ExportButton.Location = new System.Drawing.Point(131, 223);
            this.ExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ExportButton.Name = "ExportButton";
            this.ExportButton.Size = new System.Drawing.Size(124, 20);
            this.ExportButton.TabIndex = 66;
            this.ExportButton.Text = "画像取出";
            this.ExportButton.UseVisualStyleBackColor = true;
            this.ExportButton.Click += new System.EventHandler(this.ExportButton_Click);
            // 
            // L_0_COMBO
            // 
            this.L_0_COMBO.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.L_0_COMBO.FormattingEnabled = true;
            this.L_0_COMBO.Items.AddRange(new object[] {
            "00=単体",
            "01=10分割"});
            this.L_0_COMBO.Location = new System.Drawing.Point(149, 5);
            this.L_0_COMBO.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.L_0_COMBO.Name = "L_0_COMBO";
            this.L_0_COMBO.Size = new System.Drawing.Size(109, 21);
            this.L_0_COMBO.TabIndex = 39;
            // 
            // J_1
            // 
            this.J_1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_1.Location = new System.Drawing.Point(-1, 25);
            this.J_1.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.J_1.Name = "J_1";
            this.J_1.Size = new System.Drawing.Size(105, 21);
            this.J_1.TabIndex = 38;
            this.J_1.Text = "00";
            this.J_1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // B3
            // 
            this.B3.Hexadecimal = true;
            this.B3.Location = new System.Drawing.Point(194, 27);
            this.B3.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.B3.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.B3.Name = "B3";
            this.B3.Size = new System.Drawing.Size(40, 20);
            this.B3.TabIndex = 37;
            // 
            // B2
            // 
            this.B2.Hexadecimal = true;
            this.B2.Location = new System.Drawing.Point(150, 27);
            this.B2.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.B2.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.B2.Name = "B2";
            this.B2.Size = new System.Drawing.Size(40, 20);
            this.B2.TabIndex = 36;
            // 
            // B1
            // 
            this.B1.Hexadecimal = true;
            this.B1.Location = new System.Drawing.Point(106, 27);
            this.B1.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.B1.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.B1.Name = "B1";
            this.B1.Size = new System.Drawing.Size(40, 20);
            this.B1.TabIndex = 35;
            // 
            // B0
            // 
            this.B0.Hexadecimal = true;
            this.B0.Location = new System.Drawing.Point(106, 7);
            this.B0.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.B0.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.B0.Name = "B0";
            this.B0.Size = new System.Drawing.Size(40, 20);
            this.B0.TabIndex = 34;
            // 
            // J_0
            // 
            this.J_0.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_0.Location = new System.Drawing.Point(-1, 5);
            this.J_0.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.J_0.Name = "J_0";
            this.J_0.Size = new System.Drawing.Size(105, 21);
            this.J_0.TabIndex = 33;
            this.J_0.Text = "??";
            this.J_0.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // P4
            // 
            this.P4.Hexadecimal = true;
            this.P4.Location = new System.Drawing.Point(106, 51);
            this.P4.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.P4.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P4.Name = "P4";
            this.P4.Size = new System.Drawing.Size(87, 20);
            this.P4.TabIndex = 3;
            // 
            // P12
            // 
            this.P12.Hexadecimal = true;
            this.P12.Location = new System.Drawing.Point(106, 91);
            this.P12.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.P12.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P12.Name = "P12";
            this.P12.Size = new System.Drawing.Size(87, 20);
            this.P12.TabIndex = 2;
            // 
            // J_12
            // 
            this.J_12.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_12.Location = new System.Drawing.Point(-1, 88);
            this.J_12.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.J_12.Name = "J_12";
            this.J_12.Size = new System.Drawing.Size(105, 21);
            this.J_12.TabIndex = 32;
            this.J_12.Text = "パレット";
            this.J_12.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // P8
            // 
            this.P8.Hexadecimal = true;
            this.P8.Location = new System.Drawing.Point(106, 71);
            this.P8.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.P8.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P8.Name = "P8";
            this.P8.Size = new System.Drawing.Size(87, 20);
            this.P8.TabIndex = 1;
            // 
            // J_8
            // 
            this.J_8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_8.Location = new System.Drawing.Point(-1, 67);
            this.J_8.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.J_8.Name = "J_8";
            this.J_8.Size = new System.Drawing.Size(105, 21);
            this.J_8.TabIndex = 30;
            this.J_8.Text = "TSA";
            this.J_8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_PIC
            // 
            this.X_PIC.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_PIC.Location = new System.Drawing.Point(260, 1);
            this.X_PIC.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.X_PIC.Name = "X_PIC";
            this.X_PIC.Size = new System.Drawing.Size(313, 211);
            this.X_PIC.TabIndex = 29;
            this.X_PIC.TabStop = false;
            // 
            // J_4
            // 
            this.J_4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_4.Location = new System.Drawing.Point(-1, 47);
            this.J_4.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.J_4.Name = "J_4";
            this.J_4.Size = new System.Drawing.Size(105, 21);
            this.J_4.TabIndex = 24;
            this.J_4.Text = "10分割画像";
            this.J_4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ReloadListButton
            // 
            this.ReloadListButton.Location = new System.Drawing.Point(287, -1);
            this.ReloadListButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.ReloadListButton.Name = "ReloadListButton";
            this.ReloadListButton.Size = new System.Drawing.Size(75, 20);
            this.ReloadListButton.TabIndex = 25;
            this.ReloadListButton.Text = "再取得";
            this.ReloadListButton.UseVisualStyleBackColor = true;
            // 
            // WriteButton
            // 
            this.WriteButton.Location = new System.Drawing.Point(463, -1);
            this.WriteButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(111, 20);
            this.WriteButton.TabIndex = 55;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            // 
            // panel6
            // 
            this.panel6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel6.Controls.Add(this.AddressListExpandsButton);
            this.panel6.Controls.Add(this.LabelFilter);
            this.panel6.Controls.Add(this.AddressList);
            this.panel6.Location = new System.Drawing.Point(7, 25);
            this.panel6.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.panel6.Name = "panel6";
            this.panel6.Size = new System.Drawing.Size(164, 271);
            this.panel6.TabIndex = 89;
            // 
            // AddressListExpandsButton
            // 
            this.AddressListExpandsButton.Location = new System.Drawing.Point(1, 249);
            this.AddressListExpandsButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.AddressListExpandsButton.Name = "AddressListExpandsButton";
            this.AddressListExpandsButton.Size = new System.Drawing.Size(157, 20);
            this.AddressListExpandsButton.TabIndex = 114;
            this.AddressListExpandsButton.Text = "リストの拡張";
            this.AddressListExpandsButton.UseVisualStyleBackColor = true;
            // 
            // LabelFilter
            // 
            this.LabelFilter.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.LabelFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.LabelFilter.Location = new System.Drawing.Point(0, -2);
            this.LabelFilter.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.LabelFilter.Name = "LabelFilter";
            this.LabelFilter.Size = new System.Drawing.Size(163, 21);
            this.LabelFilter.TabIndex = 55;
            this.LabelFilter.Text = "名前";
            this.LabelFilter.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AddressList
            // 
            this.AddressList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.AddressList.FormattingEnabled = true;
            this.AddressList.IntegralHeight = false;
            this.AddressList.Location = new System.Drawing.Point(0, 17);
            this.AddressList.Name = "AddressList";
            this.AddressList.Size = new System.Drawing.Size(164, 232);
            this.AddressList.TabIndex = 0;
            this.AddressList.SelectedIndexChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // BlockSize
            // 
            this.BlockSize.ErrorMessage = "";
            this.BlockSize.Location = new System.Drawing.Point(212, 2);
            this.BlockSize.Name = "BlockSize";
            this.BlockSize.Placeholder = "";
            this.BlockSize.ReadOnly = true;
            this.BlockSize.Size = new System.Drawing.Size(56, 20);
            this.BlockSize.TabIndex = 58;
            // 
            // label3
            // 
            this.label3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label3.Location = new System.Drawing.Point(157, -1);
            this.label3.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(54, 21);
            this.label3.TabIndex = 59;
            this.label3.Text = "Size:";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // SelectAddress
            // 
            this.SelectAddress.ErrorMessage = "";
            this.SelectAddress.Location = new System.Drawing.Point(355, 1);
            this.SelectAddress.Name = "SelectAddress";
            this.SelectAddress.Placeholder = "";
            this.SelectAddress.ReadOnly = true;
            this.SelectAddress.Size = new System.Drawing.Size(93, 20);
            this.SelectAddress.TabIndex = 57;
            // 
            // label22
            // 
            this.label22.AccessibleDescription = "@SELECTION_ADDRESS";
            this.label22.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label22.Location = new System.Drawing.Point(273, -1);
            this.label22.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(82, 21);
            this.label22.TabIndex = 56;
            this.label22.Text = "選択アドレス:";
            this.label22.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Address
            // 
            this.Address.Hexadecimal = true;
            this.Address.Location = new System.Drawing.Point(65, 1);
            this.Address.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.Address.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.Address.Name = "Address";
            this.Address.Size = new System.Drawing.Size(87, 20);
            this.Address.TabIndex = 0;
            // 
            // label9
            // 
            this.label9.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label9.Location = new System.Drawing.Point(167, -1);
            this.label9.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(57, 23);
            this.label9.TabIndex = 24;
            this.label9.Text = "読込数";
            this.label9.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panel3
            // 
            this.panel3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel3.Controls.Add(this.ReloadListButton);
            this.panel3.Controls.Add(this.label8);
            this.panel3.Controls.Add(this.label9);
            this.panel3.Controls.Add(this.ReadCount);
            this.panel3.Controls.Add(this.ReadStartAddress);
            this.panel3.Location = new System.Drawing.Point(7, 3);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(743, 21);
            this.panel3.TabIndex = 87;
            // 
            // ReadCount
            // 
            this.ReadCount.Location = new System.Drawing.Point(231, 1);
            this.ReadCount.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.ReadCount.Maximum = new decimal(new int[] {
            256,
            0,
            0,
            0});
            this.ReadCount.Name = "ReadCount";
            this.ReadCount.Size = new System.Drawing.Size(52, 20);
            this.ReadCount.TabIndex = 1;
            // 
            // ReadStartAddress
            // 
            this.ReadStartAddress.Hexadecimal = true;
            this.ReadStartAddress.Location = new System.Drawing.Point(77, 1);
            this.ReadStartAddress.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.ReadStartAddress.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.ReadStartAddress.Name = "ReadStartAddress";
            this.ReadStartAddress.Size = new System.Drawing.Size(87, 20);
            this.ReadStartAddress.TabIndex = 0;
            // 
            // label23
            // 
            this.label23.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label23.Location = new System.Drawing.Point(-1, -1);
            this.label23.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(57, 21);
            this.label23.TabIndex = 53;
            this.label23.Text = "アドレス";
            this.label23.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panel5
            // 
            this.panel5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel5.Controls.Add(this.BlockSize);
            this.panel5.Controls.Add(this.label3);
            this.panel5.Controls.Add(this.SelectAddress);
            this.panel5.Controls.Add(this.label22);
            this.panel5.Controls.Add(this.WriteButton);
            this.panel5.Controls.Add(this.Address);
            this.panel5.Controls.Add(this.label23);
            this.panel5.Location = new System.Drawing.Point(175, 26);
            this.panel5.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.panel5.Name = "panel5";
            this.panel5.Size = new System.Drawing.Size(575, 20);
            this.panel5.TabIndex = 86;
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(3, 199);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(123, 20);
            this.OpenSourceButton.TabIndex = 201;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(131, 199);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(124, 20);
            this.SelectSourceButton.TabIndex = 202;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // ImageCGFE7UForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(758, 299);
            this.Controls.Add(this.DragTargetPanel);
            this.Controls.Add(this.panel6);
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.panel5);
            this.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.Name = "ImageCGFE7UForm";
            this.Text = "CG画像";
            this.Load += new System.EventHandler(this.ImageCGFE7UForm_Load);
            this.DragTargetPanel.ResumeLayout(false);
            this.DragTargetPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.B3)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.B2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.B1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.B0)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P12)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P8)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_PIC)).EndInit();
            this.panel6.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.Address)).EndInit();
            this.panel3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).EndInit();
            this.panel5.ResumeLayout(false);
            this.panel5.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Panel DragTargetPanel;
        private System.Windows.Forms.NumericUpDown P12;
        private System.Windows.Forms.Label J_12;
        private System.Windows.Forms.NumericUpDown P8;
        private System.Windows.Forms.Label J_8;
        private System.Windows.Forms.NumericUpDown P4;
        private System.Windows.Forms.Label J_4;
        private System.Windows.Forms.Button ReloadListButton;
        private System.Windows.Forms.Button WriteButton;
        private System.Windows.Forms.Panel panel6;
        private System.Windows.Forms.Button AddressListExpandsButton;
        private System.Windows.Forms.Label LabelFilter;
        private ListBoxEx AddressList;
        private FEBuilderGBA.TextBoxEx BlockSize;
        private System.Windows.Forms.Label label3;
        private FEBuilderGBA.TextBoxEx SelectAddress;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.NumericUpDown Address;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.NumericUpDown ReadCount;
        private System.Windows.Forms.NumericUpDown ReadStartAddress;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.Panel panel5;
        private System.Windows.Forms.Label J_0;
        private System.Windows.Forms.NumericUpDown B3;
        private System.Windows.Forms.NumericUpDown B2;
        private System.Windows.Forms.NumericUpDown B1;
        private System.Windows.Forms.NumericUpDown B0;
        private System.Windows.Forms.Label J_1;
        private System.Windows.Forms.ComboBox L_0_COMBO;
        private System.Windows.Forms.Button ImportButton;
        private System.Windows.Forms.Button ExportButton;
        private InterpolatedPictureBox X_PIC;
        private System.Windows.Forms.Button DecreaseColorTSAToolButton;
        private TextBoxEx Comment;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button SelectSourceButton;
        private System.Windows.Forms.Button OpenSourceButton;
    }
}