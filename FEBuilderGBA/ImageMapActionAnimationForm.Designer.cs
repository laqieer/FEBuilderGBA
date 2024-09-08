namespace FEBuilderGBA
{
    partial class ImageMapActionAnimationForm
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
            this.AddressListExpandsButton_255 = new System.Windows.Forms.Button();
            this.LabelFilter = new System.Windows.Forms.Label();
            this.AddressList = new FEBuilderGBA.ListBoxEx();
            this.panel1 = new System.Windows.Forms.Panel();
            this.W6 = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.W4 = new System.Windows.Forms.NumericUpDown();
            this.label2 = new System.Windows.Forms.Label();
            this.Comment = new FEBuilderGBA.TextBoxEx();
            this.label4 = new System.Windows.Forms.Label();
            this.AnimationExportButton = new System.Windows.Forms.Button();
            this.AnimationImportButton = new System.Windows.Forms.Button();
            this.AnimationPanel = new System.Windows.Forms.Panel();
            this.BinInfo = new FEBuilderGBA.TextBoxEx();
            this.ShowZoomComboBox = new System.Windows.Forms.ComboBox();
            this.label25 = new System.Windows.Forms.Label();
            this.label26 = new System.Windows.Forms.Label();
            this.label24 = new System.Windows.Forms.Label();
            this.ShowFrameUpDown = new System.Windows.Forms.NumericUpDown();
            this.AnimationPictureBox = new FEBuilderGBA.InterpolatedPictureBox();
            this.P0 = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.NOTIFY_KeepEmpty = new System.Windows.Forms.Label();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.panel3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).BeginInit();
            this.panel5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).BeginInit();
            this.panel6.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.W6)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.W4)).BeginInit();
            this.AnimationPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.AnimationPictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P0)).BeginInit();
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
            this.ReloadListButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
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
            this.ReadStartAddress.Location = new System.Drawing.Point(76, 3);
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
            this.panel5.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
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
            this.BlockSize.Size = new System.Drawing.Size(56, 20);
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
            this.SelectAddress.Size = new System.Drawing.Size(101, 20);
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
            this.WriteButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(112, 20);
            this.WriteButton.TabIndex = 55;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            // 
            // Address
            // 
            this.Address.Hexadecimal = true;
            this.Address.Location = new System.Drawing.Point(60, 3);
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
            this.panel6.Controls.Add(this.AddressListExpandsButton_255);
            this.panel6.Controls.Add(this.LabelFilter);
            this.panel6.Controls.Add(this.AddressList);
            this.panel6.Location = new System.Drawing.Point(7, 29);
            this.panel6.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.panel6.Name = "panel6";
            this.panel6.Size = new System.Drawing.Size(164, 535);
            this.panel6.TabIndex = 96;
            // 
            // AddressListExpandsButton_255
            // 
            this.AddressListExpandsButton_255.Location = new System.Drawing.Point(1, 512);
            this.AddressListExpandsButton_255.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.AddressListExpandsButton_255.Name = "AddressListExpandsButton_255";
            this.AddressListExpandsButton_255.Size = new System.Drawing.Size(159, 20);
            this.AddressListExpandsButton_255.TabIndex = 114;
            this.AddressListExpandsButton_255.Text = "リストの拡張";
            this.AddressListExpandsButton_255.UseVisualStyleBackColor = true;
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
            this.AddressList.Location = new System.Drawing.Point(-1, 15);
            this.AddressList.Name = "AddressList";
            this.AddressList.Size = new System.Drawing.Size(165, 496);
            this.AddressList.TabIndex = 0;
            this.AddressList.SelectedIndexChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // panel1
            // 
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.SelectSourceButton);
            this.panel1.Controls.Add(this.OpenSourceButton);
            this.panel1.Controls.Add(this.W6);
            this.panel1.Controls.Add(this.label5);
            this.panel1.Controls.Add(this.W4);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Controls.Add(this.Comment);
            this.panel1.Controls.Add(this.label4);
            this.panel1.Controls.Add(this.AnimationExportButton);
            this.panel1.Controls.Add(this.AnimationImportButton);
            this.panel1.Controls.Add(this.AnimationPanel);
            this.panel1.Controls.Add(this.P0);
            this.panel1.Controls.Add(this.label1);
            this.panel1.Controls.Add(this.NOTIFY_KeepEmpty);
            this.panel1.Location = new System.Drawing.Point(174, 49);
            this.panel1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(603, 509);
            this.panel1.TabIndex = 97;
            // 
            // W6
            // 
            this.W6.Hexadecimal = true;
            this.W6.Location = new System.Drawing.Point(91, 57);
            this.W6.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.W6.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.W6.Name = "W6";
            this.W6.Size = new System.Drawing.Size(65, 20);
            this.W6.TabIndex = 202;
            // 
            // label5
            // 
            this.label5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label5.Location = new System.Drawing.Point(-1, 54);
            this.label5.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(91, 21);
            this.label5.TabIndex = 201;
            this.label5.Text = "00";
            this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // W4
            // 
            this.W4.Hexadecimal = true;
            this.W4.Location = new System.Drawing.Point(91, 36);
            this.W4.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.W4.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.W4.Name = "W4";
            this.W4.Size = new System.Drawing.Size(65, 20);
            this.W4.TabIndex = 200;
            // 
            // label2
            // 
            this.label2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label2.Location = new System.Drawing.Point(-1, 33);
            this.label2.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(91, 21);
            this.label2.TabIndex = 199;
            this.label2.Text = "00";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Comment
            // 
            this.Comment.ErrorMessage = "";
            this.Comment.Location = new System.Drawing.Point(93, 87);
            this.Comment.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Comment.Name = "Comment";
            this.Comment.Placeholder = "";
            this.Comment.Size = new System.Drawing.Size(295, 20);
            this.Comment.TabIndex = 198;
            // 
            // label4
            // 
            this.label4.AccessibleDescription = "@COMMENT";
            this.label4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label4.Location = new System.Drawing.Point(-1, 85);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(91, 21);
            this.label4.TabIndex = 197;
            this.label4.Text = "コメント";
            this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // AnimationExportButton
            // 
            this.AnimationExportButton.Location = new System.Drawing.Point(405, 11);
            this.AnimationExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.AnimationExportButton.Name = "AnimationExportButton";
            this.AnimationExportButton.Size = new System.Drawing.Size(153, 20);
            this.AnimationExportButton.TabIndex = 122;
            this.AnimationExportButton.Text = "アニメーション取出";
            this.AnimationExportButton.UseVisualStyleBackColor = true;
            this.AnimationExportButton.Click += new System.EventHandler(this.AnimationExportButton_Click);
            // 
            // AnimationImportButton
            // 
            this.AnimationImportButton.Location = new System.Drawing.Point(227, 11);
            this.AnimationImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.AnimationImportButton.Name = "AnimationImportButton";
            this.AnimationImportButton.Size = new System.Drawing.Size(153, 20);
            this.AnimationImportButton.TabIndex = 121;
            this.AnimationImportButton.Text = "アニメーション読込";
            this.AnimationImportButton.UseVisualStyleBackColor = true;
            this.AnimationImportButton.Click += new System.EventHandler(this.AnimationImportButton_Click);
            // 
            // AnimationPanel
            // 
            this.AnimationPanel.Controls.Add(this.BinInfo);
            this.AnimationPanel.Controls.Add(this.ShowZoomComboBox);
            this.AnimationPanel.Controls.Add(this.label25);
            this.AnimationPanel.Controls.Add(this.label26);
            this.AnimationPanel.Controls.Add(this.label24);
            this.AnimationPanel.Controls.Add(this.ShowFrameUpDown);
            this.AnimationPanel.Controls.Add(this.AnimationPictureBox);
            this.AnimationPanel.Location = new System.Drawing.Point(2, 119);
            this.AnimationPanel.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.AnimationPanel.Name = "AnimationPanel";
            this.AnimationPanel.Size = new System.Drawing.Size(576, 283);
            this.AnimationPanel.TabIndex = 120;
            this.AnimationPanel.Visible = false;
            // 
            // BinInfo
            // 
            this.BinInfo.ErrorMessage = "";
            this.BinInfo.Location = new System.Drawing.Point(217, 245);
            this.BinInfo.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.BinInfo.Name = "BinInfo";
            this.BinInfo.Placeholder = "";
            this.BinInfo.ReadOnly = true;
            this.BinInfo.Size = new System.Drawing.Size(360, 20);
            this.BinInfo.TabIndex = 196;
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
            this.ShowZoomComboBox.Size = new System.Drawing.Size(118, 21);
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
            this.ShowFrameUpDown.Size = new System.Drawing.Size(44, 20);
            this.ShowFrameUpDown.TabIndex = 183;
            this.ShowFrameUpDown.ValueChanged += new System.EventHandler(this.ShowFrameUpDown_ValueChanged);
            // 
            // AnimationPictureBox
            // 
            this.AnimationPictureBox.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.AnimationPictureBox.Location = new System.Drawing.Point(217, 1);
            this.AnimationPictureBox.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.AnimationPictureBox.Name = "AnimationPictureBox";
            this.AnimationPictureBox.Size = new System.Drawing.Size(357, 240);
            this.AnimationPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.AnimationPictureBox.TabIndex = 114;
            this.AnimationPictureBox.TabStop = false;
            // 
            // P0
            // 
            this.P0.Hexadecimal = true;
            this.P0.Location = new System.Drawing.Point(91, 14);
            this.P0.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.P0.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P0.Name = "P0";
            this.P0.Size = new System.Drawing.Size(111, 20);
            this.P0.TabIndex = 119;
            // 
            // label1
            // 
            this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label1.Location = new System.Drawing.Point(-1, 11);
            this.label1.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(91, 21);
            this.label1.TabIndex = 118;
            this.label1.Text = "アニメーション";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // NOTIFY_KeepEmpty
            // 
            this.NOTIFY_KeepEmpty.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.NOTIFY_KeepEmpty.Location = new System.Drawing.Point(227, 11);
            this.NOTIFY_KeepEmpty.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.NOTIFY_KeepEmpty.Name = "NOTIFY_KeepEmpty";
            this.NOTIFY_KeepEmpty.Size = new System.Drawing.Size(332, 42);
            this.NOTIFY_KeepEmpty.TabIndex = 203;
            this.NOTIFY_KeepEmpty.Text = "ID=00 Emptyはnullデータとして予約されています。\r\n0x0以外の値を設定しないでください。";
            this.NOTIFY_KeepEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.NOTIFY_KeepEmpty.Visible = false;
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(227, 60);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(151, 22);
            this.OpenSourceButton.TabIndex = 204;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(405, 60);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(154, 21);
            this.SelectSourceButton.TabIndex = 205;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // ImageMapActionAnimationForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(785, 435);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.panel5);
            this.Controls.Add(this.panel6);
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Name = "ImageMapActionAnimationForm";
            this.Text = "攻撃モーション";
            this.Load += new System.EventHandler(this.ImageMapActionAnimationForm_Load);
            this.panel3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).EndInit();
            this.panel5.ResumeLayout(false);
            this.panel5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).EndInit();
            this.panel6.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.W6)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.W4)).EndInit();
            this.AnimationPanel.ResumeLayout(false);
            this.AnimationPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.AnimationPictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P0)).EndInit();
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
        private System.Windows.Forms.Button AddressListExpandsButton_255;
        private System.Windows.Forms.Label LabelFilter;
        private ListBoxEx AddressList;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown P0;
        private System.Windows.Forms.Button AnimationExportButton;
        private System.Windows.Forms.Button AnimationImportButton;
        private System.Windows.Forms.Panel AnimationPanel;
        private InterpolatedPictureBox AnimationPictureBox;
        private System.Windows.Forms.ComboBox ShowZoomComboBox;
        private System.Windows.Forms.Label label25;
        private System.Windows.Forms.Label label26;
        private System.Windows.Forms.Label label24;
        private System.Windows.Forms.NumericUpDown ShowFrameUpDown;
        private TextBoxEx Comment;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown W6;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.NumericUpDown W4;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label NOTIFY_KeepEmpty;
        private TextBoxEx BinInfo;
        private System.Windows.Forms.Button SelectSourceButton;
        private System.Windows.Forms.Button OpenSourceButton;
    }
}