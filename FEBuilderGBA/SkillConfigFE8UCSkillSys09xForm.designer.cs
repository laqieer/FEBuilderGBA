namespace FEBuilderGBA
{
    partial class SkillConfigCSkillSystem09xForm
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
            this.panel3 = new System.Windows.Forms.Panel();
            this.ReloadListButton = new System.Windows.Forms.Button();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.ReadCount = new System.Windows.Forms.NumericUpDown();
            this.ReadStartAddress = new System.Windows.Forms.NumericUpDown();
            this.panel5 = new System.Windows.Forms.Panel();
            this.BlockSize = new FEBuilderGBA.TextBoxEx();
            this.label3 = new System.Windows.Forms.Label();
            this.SelectAddress = new FEBuilderGBA.TextBoxEx();
            this.label22 = new System.Windows.Forms.Label();
            this.WriteButton = new System.Windows.Forms.Button();
            this.Address = new System.Windows.Forms.NumericUpDown();
            this.label23 = new System.Windows.Forms.Label();
            this.panel6 = new System.Windows.Forms.Panel();
            this.LabelFilter = new System.Windows.Forms.Label();
            this.AddressList = new FEBuilderGBA.ListBoxEx();
            this.panel1 = new System.Windows.Forms.Panel();
            this.IconAddr = new System.Windows.Forms.NumericUpDown();
            this.AnimationExportButton = new System.Windows.Forms.Button();
            this.AnimationInportButton = new System.Windows.Forms.Button();
            this.AnimationPanel = new System.Windows.Forms.Panel();
            this.BinInfo = new FEBuilderGBA.TextBoxEx();
            this.X_N_JumpEditor = new System.Windows.Forms.Button();
            this.ShowZoomComboBox = new System.Windows.Forms.ComboBox();
            this.label25 = new System.Windows.Forms.Label();
            this.label26 = new System.Windows.Forms.Label();
            this.label24 = new System.Windows.Forms.Label();
            this.ShowFrameUpDown = new System.Windows.Forms.NumericUpDown();
            this.AnimationPictureBox = new FEBuilderGBA.InterpolatedPictureBox();
            this.ANIMATION = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.ImportButton = new System.Windows.Forms.Button();
            this.ExportButton = new System.Windows.Forms.Button();
            this.L_6_TEXT = new FEBuilderGBA.TextBoxEx();
            this.W6 = new System.Windows.Forms.NumericUpDown();
            this.J_6_TEXT = new System.Windows.Forms.Label();
            this.SKILLICON = new FEBuilderGBA.InterpolatedPictureBox();
            this.panel10 = new System.Windows.Forms.Label();
            this.J_4_TEXT = new System.Windows.Forms.Label();
            this.W4 = new System.Windows.Forms.NumericUpDown();
            this.L_4_TEXT = new FEBuilderGBA.TextBoxEx();
            this.panel3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).BeginInit();
            this.panel5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).BeginInit();
            this.panel6.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.IconAddr)).BeginInit();
            this.AnimationPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.AnimationPictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ANIMATION)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.W6)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.SKILLICON)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.W4)).BeginInit();
            this.SuspendLayout();
            // 
            // panel3
            // 
            this.panel3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel3.Controls.Add(this.ReloadListButton);
            this.panel3.Controls.Add(this.label8);
            this.panel3.Controls.Add(this.label9);
            this.panel3.Controls.Add(this.ReadCount);
            this.panel3.Controls.Add(this.ReadStartAddress);
            this.panel3.Location = new System.Drawing.Point(7, 5);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(771, 23);
            this.panel3.TabIndex = 95;
            // 
            // ReloadListButton
            // 
            this.ReloadListButton.Location = new System.Drawing.Point(320, 1);
            this.ReloadListButton.Margin = new System.Windows.Forms.Padding(1);
            this.ReloadListButton.Name = "ReloadListButton";
            this.ReloadListButton.Size = new System.Drawing.Size(75, 20);
            this.ReloadListButton.TabIndex = 3;
            this.ReloadListButton.Text = "再取得";
            this.ReloadListButton.UseVisualStyleBackColor = true;
            // 
            // label8
            // 
            this.label8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label8.Location = new System.Drawing.Point(-1, -1);
            this.label8.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(77, 25);
            this.label8.TabIndex = 23;
            this.label8.Text = "先頭アドレス";
            this.label8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label9
            // 
            this.label9.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label9.Location = new System.Drawing.Point(166, -1);
            this.label9.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(58, 23);
            this.label9.TabIndex = 24;
            this.label9.Text = "読込数";
            this.label9.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ReadCount
            // 
            this.ReadCount.Location = new System.Drawing.Point(227, 2);
            this.ReadCount.Margin = new System.Windows.Forms.Padding(1);
            this.ReadCount.Maximum = new decimal(new int[] {
            256,
            0,
            0,
            0});
            this.ReadCount.Name = "ReadCount";
            this.ReadCount.Size = new System.Drawing.Size(52, 21);
            this.ReadCount.TabIndex = 1;
            // 
            // ReadStartAddress
            // 
            this.ReadStartAddress.Hexadecimal = true;
            this.ReadStartAddress.Location = new System.Drawing.Point(76, 3);
            this.ReadStartAddress.Margin = new System.Windows.Forms.Padding(1);
            this.ReadStartAddress.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.ReadStartAddress.Name = "ReadStartAddress";
            this.ReadStartAddress.Size = new System.Drawing.Size(87, 21);
            this.ReadStartAddress.TabIndex = 0;
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
            this.panel5.Location = new System.Drawing.Point(174, 27);
            this.panel5.Margin = new System.Windows.Forms.Padding(1);
            this.panel5.Name = "panel5";
            this.panel5.Size = new System.Drawing.Size(604, 23);
            this.panel5.TabIndex = 94;
            // 
            // BlockSize
            // 
            this.BlockSize.ErrorMessage = "";
            this.BlockSize.Location = new System.Drawing.Point(209, 3);
            this.BlockSize.Name = "BlockSize";
            this.BlockSize.Placeholder = "";
            this.BlockSize.ReadOnly = true;
            this.BlockSize.Size = new System.Drawing.Size(56, 21);
            this.BlockSize.TabIndex = 58;
            // 
            // label3
            // 
            this.label3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label3.Location = new System.Drawing.Point(154, 0);
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
            this.SelectAddress.Size = new System.Drawing.Size(101, 21);
            this.SelectAddress.TabIndex = 57;
            // 
            // label22
            // 
            this.label22.AccessibleDescription = "@SELECTION_ADDRESS";
            this.label22.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label22.Location = new System.Drawing.Point(272, 1);
            this.label22.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(82, 21);
            this.label22.TabIndex = 56;
            this.label22.Text = "選択アドレス:";
            this.label22.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WriteButton
            // 
            this.WriteButton.Location = new System.Drawing.Point(477, 0);
            this.WriteButton.Margin = new System.Windows.Forms.Padding(1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(112, 20);
            this.WriteButton.TabIndex = 55;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            this.WriteButton.Click += new System.EventHandler(this.WriteButton_Click);
            // 
            // Address
            // 
            this.Address.Hexadecimal = true;
            this.Address.Location = new System.Drawing.Point(60, 3);
            this.Address.Margin = new System.Windows.Forms.Padding(1);
            this.Address.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.Address.Name = "Address";
            this.Address.Size = new System.Drawing.Size(87, 21);
            this.Address.TabIndex = 0;
            // 
            // label23
            // 
            this.label23.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label23.Location = new System.Drawing.Point(-1, 0);
            this.label23.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(59, 23);
            this.label23.TabIndex = 53;
            this.label23.Text = "アドレス";
            this.label23.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panel6
            // 
            this.panel6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel6.Controls.Add(this.LabelFilter);
            this.panel6.Controls.Add(this.AddressList);
            this.panel6.Location = new System.Drawing.Point(7, 29);
            this.panel6.Margin = new System.Windows.Forms.Padding(1);
            this.panel6.Name = "panel6";
            this.panel6.Size = new System.Drawing.Size(164, 535);
            this.panel6.TabIndex = 96;
            // 
            // LabelFilter
            // 
            this.LabelFilter.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.LabelFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.LabelFilter.Location = new System.Drawing.Point(-1, -2);
            this.LabelFilter.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.LabelFilter.Name = "LabelFilter";
            this.LabelFilter.Size = new System.Drawing.Size(165, 18);
            this.LabelFilter.TabIndex = 55;
            this.LabelFilter.Text = "名前";
            this.LabelFilter.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AddressList
            // 
            this.AddressList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.AddressList.FormattingEnabled = true;
            this.AddressList.IntegralHeight = false;
            this.AddressList.ItemHeight = 12;
            this.AddressList.Location = new System.Drawing.Point(-1, 15);
            this.AddressList.Name = "AddressList";
            this.AddressList.Size = new System.Drawing.Size(165, 519);
            this.AddressList.TabIndex = 0;
            this.AddressList.SelectedIndexChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // panel1
            // 
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.L_4_TEXT);
            this.panel1.Controls.Add(this.W4);
            this.panel1.Controls.Add(this.J_4_TEXT);
            this.panel1.Controls.Add(this.IconAddr);
            this.panel1.Controls.Add(this.AnimationExportButton);
            this.panel1.Controls.Add(this.AnimationInportButton);
            this.panel1.Controls.Add(this.AnimationPanel);
            this.panel1.Controls.Add(this.ANIMATION);
            this.panel1.Controls.Add(this.label1);
            this.panel1.Controls.Add(this.ImportButton);
            this.panel1.Controls.Add(this.ExportButton);
            this.panel1.Controls.Add(this.L_6_TEXT);
            this.panel1.Controls.Add(this.W6);
            this.panel1.Controls.Add(this.J_6_TEXT);
            this.panel1.Controls.Add(this.SKILLICON);
            this.panel1.Controls.Add(this.panel10);
            this.panel1.Location = new System.Drawing.Point(174, 49);
            this.panel1.Margin = new System.Windows.Forms.Padding(2);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(603, 509);
            this.panel1.TabIndex = 97;
            // 
            // IconAddr
            // 
            this.IconAddr.Hexadecimal = true;
            this.IconAddr.Location = new System.Drawing.Point(156, 29);
            this.IconAddr.Margin = new System.Windows.Forms.Padding(1);
            this.IconAddr.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.IconAddr.Name = "IconAddr";
            this.IconAddr.ReadOnly = true;
            this.IconAddr.Size = new System.Drawing.Size(111, 21);
            this.IconAddr.TabIndex = 154;
            // 
            // AnimationExportButton
            // 
            this.AnimationExportButton.Location = new System.Drawing.Point(407, 197);
            this.AnimationExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.AnimationExportButton.Name = "AnimationExportButton";
            this.AnimationExportButton.Size = new System.Drawing.Size(153, 20);
            this.AnimationExportButton.TabIndex = 122;
            this.AnimationExportButton.Text = "アニメーション取出";
            this.AnimationExportButton.UseVisualStyleBackColor = true;
            this.AnimationExportButton.Click += new System.EventHandler(this.AnimationExportButton_Click);
            // 
            // AnimationInportButton
            // 
            this.AnimationInportButton.Location = new System.Drawing.Point(229, 197);
            this.AnimationInportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.AnimationInportButton.Name = "AnimationInportButton";
            this.AnimationInportButton.Size = new System.Drawing.Size(153, 20);
            this.AnimationInportButton.TabIndex = 121;
            this.AnimationInportButton.Text = "アニメーション読込";
            this.AnimationInportButton.UseVisualStyleBackColor = true;
            this.AnimationInportButton.Click += new System.EventHandler(this.AnimationImportButton_Click);
            // 
            // AnimationPanel
            // 
            this.AnimationPanel.Controls.Add(this.BinInfo);
            this.AnimationPanel.Controls.Add(this.X_N_JumpEditor);
            this.AnimationPanel.Controls.Add(this.ShowZoomComboBox);
            this.AnimationPanel.Controls.Add(this.label25);
            this.AnimationPanel.Controls.Add(this.label26);
            this.AnimationPanel.Controls.Add(this.label24);
            this.AnimationPanel.Controls.Add(this.ShowFrameUpDown);
            this.AnimationPanel.Controls.Add(this.AnimationPictureBox);
            this.AnimationPanel.Location = new System.Drawing.Point(12, 228);
            this.AnimationPanel.Margin = new System.Windows.Forms.Padding(2);
            this.AnimationPanel.Name = "AnimationPanel";
            this.AnimationPanel.Size = new System.Drawing.Size(576, 273);
            this.AnimationPanel.TabIndex = 120;
            this.AnimationPanel.Visible = false;
            // 
            // BinInfo
            // 
            this.BinInfo.ErrorMessage = "";
            this.BinInfo.Location = new System.Drawing.Point(216, 245);
            this.BinInfo.Margin = new System.Windows.Forms.Padding(2);
            this.BinInfo.Name = "BinInfo";
            this.BinInfo.Placeholder = "";
            this.BinInfo.ReadOnly = true;
            this.BinInfo.Size = new System.Drawing.Size(359, 21);
            this.BinInfo.TabIndex = 197;
            // 
            // X_N_JumpEditor
            // 
            this.X_N_JumpEditor.Location = new System.Drawing.Point(113, 81);
            this.X_N_JumpEditor.Margin = new System.Windows.Forms.Padding(2);
            this.X_N_JumpEditor.Name = "X_N_JumpEditor";
            this.X_N_JumpEditor.Size = new System.Drawing.Size(89, 20);
            this.X_N_JumpEditor.TabIndex = 188;
            this.X_N_JumpEditor.Text = "エディタ";
            this.X_N_JumpEditor.UseVisualStyleBackColor = true;
            this.X_N_JumpEditor.Click += new System.EventHandler(this.X_N_JumpEditor_Click);
            // 
            // ShowZoomComboBox
            // 
            this.ShowZoomComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ShowZoomComboBox.FormattingEnabled = true;
            this.ShowZoomComboBox.Items.AddRange(new object[] {
            "拡大して描画",
            "拡大しないで描画"});
            this.ShowZoomComboBox.Location = new System.Drawing.Point(85, 23);
            this.ShowZoomComboBox.Name = "ShowZoomComboBox";
            this.ShowZoomComboBox.Size = new System.Drawing.Size(118, 20);
            this.ShowZoomComboBox.TabIndex = 187;
            this.ShowZoomComboBox.SelectedIndexChanged += new System.EventHandler(this.ShowZoomComboBox_SelectedIndexChanged);
            // 
            // label25
            // 
            this.label25.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label25.Location = new System.Drawing.Point(3, 23);
            this.label25.Name = "label25";
            this.label25.Size = new System.Drawing.Size(78, 22);
            this.label25.TabIndex = 186;
            this.label25.Text = "拡大";
            this.label25.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label26
            // 
            this.label26.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label26.Location = new System.Drawing.Point(3, 43);
            this.label26.Name = "label26";
            this.label26.Size = new System.Drawing.Size(78, 21);
            this.label26.TabIndex = 185;
            this.label26.Text = "フレーム";
            this.label26.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label24
            // 
            this.label24.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label24.Location = new System.Drawing.Point(3, 1);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(200, 21);
            this.label24.TabIndex = 184;
            this.label24.Text = "表示例";
            this.label24.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ShowFrameUpDown
            // 
            this.ShowFrameUpDown.Hexadecimal = true;
            this.ShowFrameUpDown.Location = new System.Drawing.Point(85, 45);
            this.ShowFrameUpDown.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ShowFrameUpDown.Maximum = new decimal(new int[] {
            255,
            0,
            0,
            0});
            this.ShowFrameUpDown.Name = "ShowFrameUpDown";
            this.ShowFrameUpDown.Size = new System.Drawing.Size(44, 21);
            this.ShowFrameUpDown.TabIndex = 183;
            this.ShowFrameUpDown.ValueChanged += new System.EventHandler(this.ShowFrameUpDown_ValueChanged);
            // 
            // AnimationPictureBox
            // 
            this.AnimationPictureBox.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.AnimationPictureBox.Location = new System.Drawing.Point(217, 1);
            this.AnimationPictureBox.Margin = new System.Windows.Forms.Padding(1);
            this.AnimationPictureBox.Name = "AnimationPictureBox";
            this.AnimationPictureBox.Size = new System.Drawing.Size(357, 240);
            this.AnimationPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.AnimationPictureBox.TabIndex = 114;
            this.AnimationPictureBox.TabStop = false;
            // 
            // ANIMATION
            // 
            this.ANIMATION.Hexadecimal = true;
            this.ANIMATION.Location = new System.Drawing.Point(94, 200);
            this.ANIMATION.Margin = new System.Windows.Forms.Padding(1);
            this.ANIMATION.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.ANIMATION.Name = "ANIMATION";
            this.ANIMATION.Size = new System.Drawing.Size(111, 21);
            this.ANIMATION.TabIndex = 119;
            // 
            // label1
            // 
            this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label1.Location = new System.Drawing.Point(1, 197);
            this.label1.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(91, 21);
            this.label1.TabIndex = 118;
            this.label1.Text = "アニメーション";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ImportButton
            // 
            this.ImportButton.Location = new System.Drawing.Point(156, 4);
            this.ImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ImportButton.Name = "ImportButton";
            this.ImportButton.Size = new System.Drawing.Size(107, 20);
            this.ImportButton.TabIndex = 117;
            this.ImportButton.Text = "画像読込";
            this.ImportButton.UseVisualStyleBackColor = true;
            this.ImportButton.Click += new System.EventHandler(this.ImportButton_Click);
            // 
            // ExportButton
            // 
            this.ExportButton.Location = new System.Drawing.Point(283, 4);
            this.ExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ExportButton.Name = "ExportButton";
            this.ExportButton.Size = new System.Drawing.Size(107, 20);
            this.ExportButton.TabIndex = 116;
            this.ExportButton.Text = "画像取出";
            this.ExportButton.UseVisualStyleBackColor = true;
            this.ExportButton.Click += new System.EventHandler(this.ExportButton_Click);
            // 
            // L_6_TEXT
            // 
            this.L_6_TEXT.ErrorMessage = "";
            this.L_6_TEXT.Location = new System.Drawing.Point(149, 91);
            this.L_6_TEXT.Margin = new System.Windows.Forms.Padding(1);
            this.L_6_TEXT.Multiline = true;
            this.L_6_TEXT.Name = "L_6_TEXT";
            this.L_6_TEXT.Placeholder = "";
            this.L_6_TEXT.ReadOnly = true;
            this.L_6_TEXT.Size = new System.Drawing.Size(233, 73);
            this.L_6_TEXT.TabIndex = 115;
            // 
            // W6
            // 
            this.W6.Hexadecimal = true;
            this.W6.Location = new System.Drawing.Point(94, 92);
            this.W6.Margin = new System.Windows.Forms.Padding(1);
            this.W6.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.W6.Name = "W6";
            this.W6.Size = new System.Drawing.Size(53, 21);
            this.W6.TabIndex = 111;
            // 
            // J_6_TEXT
            // 
            this.J_6_TEXT.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_6_TEXT.Location = new System.Drawing.Point(1, 92);
            this.J_6_TEXT.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.J_6_TEXT.Name = "J_6_TEXT";
            this.J_6_TEXT.Size = new System.Drawing.Size(91, 21);
            this.J_6_TEXT.TabIndex = 114;
            this.J_6_TEXT.Text = "詳細";
            this.J_6_TEXT.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // SKILLICON
            // 
            this.SKILLICON.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.SKILLICON.Location = new System.Drawing.Point(94, 4);
            this.SKILLICON.Margin = new System.Windows.Forms.Padding(1);
            this.SKILLICON.Name = "SKILLICON";
            this.SKILLICON.Size = new System.Drawing.Size(43, 43);
            this.SKILLICON.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.SKILLICON.TabIndex = 113;
            this.SKILLICON.TabStop = false;
            // 
            // panel10
            // 
            this.panel10.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel10.Location = new System.Drawing.Point(1, 4);
            this.panel10.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.panel10.Name = "panel10";
            this.panel10.Size = new System.Drawing.Size(91, 21);
            this.panel10.TabIndex = 112;
            this.panel10.Text = "アイコン";
            this.panel10.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // J_4_TEXT
            // 
            this.J_4_TEXT.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_4_TEXT.Location = new System.Drawing.Point(1, 56);
            this.J_4_TEXT.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.J_4_TEXT.Name = "J_4_TEXT";
            this.J_4_TEXT.Size = new System.Drawing.Size(91, 21);
            this.J_4_TEXT.TabIndex = 155;
            this.J_4_TEXT.Text = "スキル名";
            this.J_4_TEXT.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // W4
            // 
            this.W4.Hexadecimal = true;
            this.W4.Location = new System.Drawing.Point(94, 56);
            this.W4.Margin = new System.Windows.Forms.Padding(1);
            this.W4.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.W4.Name = "W4";
            this.W4.Size = new System.Drawing.Size(53, 21);
            this.W4.TabIndex = 156;
            // 
            // L_4_TEXT
            // 
            this.L_4_TEXT.ErrorMessage = "";
            this.L_4_TEXT.Location = new System.Drawing.Point(149, 56);
            this.L_4_TEXT.Margin = new System.Windows.Forms.Padding(1);
            this.L_4_TEXT.Multiline = true;
            this.L_4_TEXT.Name = "L_4_TEXT";
            this.L_4_TEXT.Placeholder = "";
            this.L_4_TEXT.ReadOnly = true;
            this.L_4_TEXT.Size = new System.Drawing.Size(233, 21);
            this.L_4_TEXT.TabIndex = 157;
            // 
            // SkillConfigCSkillSystem09xForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(785, 573);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.panel5);
            this.Controls.Add(this.panel6);
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "SkillConfigCSkillSystem09xForm";
            this.Text = "スキル拡張設定";
            this.Load += new System.EventHandler(this.SkillConfigCSkillSystem09xForm_Load);
            this.panel3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).EndInit();
            this.panel5.ResumeLayout(false);
            this.panel5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).EndInit();
            this.panel6.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.IconAddr)).EndInit();
            this.AnimationPanel.ResumeLayout(false);
            this.AnimationPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.AnimationPictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ANIMATION)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.W6)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.SKILLICON)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.W4)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.Button ReloadListButton;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.NumericUpDown ReadCount;
        private System.Windows.Forms.NumericUpDown ReadStartAddress;
        private System.Windows.Forms.Panel panel5;
        private FEBuilderGBA.TextBoxEx BlockSize;
        private System.Windows.Forms.Label label3;
        private FEBuilderGBA.TextBoxEx SelectAddress;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.Button WriteButton;
        private System.Windows.Forms.NumericUpDown Address;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.Panel panel6;
        private System.Windows.Forms.Label LabelFilter;
        private ListBoxEx AddressList;
        private System.Windows.Forms.Panel panel1;
        private FEBuilderGBA.TextBoxEx L_6_TEXT;
        private System.Windows.Forms.NumericUpDown W6;
        private System.Windows.Forms.Label J_6_TEXT;
        private InterpolatedPictureBox SKILLICON;
        private System.Windows.Forms.Label panel10;
        private System.Windows.Forms.Button ImportButton;
        private System.Windows.Forms.Button ExportButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown ANIMATION;
        private System.Windows.Forms.Button AnimationExportButton;
        private System.Windows.Forms.Button AnimationInportButton;
        private System.Windows.Forms.Panel AnimationPanel;
        private InterpolatedPictureBox AnimationPictureBox;
        private System.Windows.Forms.ComboBox ShowZoomComboBox;
        private System.Windows.Forms.Label label25;
        private System.Windows.Forms.Label label26;
        private System.Windows.Forms.Label label24;
        private System.Windows.Forms.NumericUpDown ShowFrameUpDown;
        private System.Windows.Forms.Button X_N_JumpEditor;
        private System.Windows.Forms.NumericUpDown IconAddr;
        private TextBoxEx BinInfo;
        private System.Windows.Forms.Label J_4_TEXT;
        private System.Windows.Forms.NumericUpDown W4;
        private TextBoxEx L_4_TEXT;
    }
}