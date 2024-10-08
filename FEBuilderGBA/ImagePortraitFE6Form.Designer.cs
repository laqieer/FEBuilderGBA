﻿namespace FEBuilderGBA
{
    partial class ImagePortraitFE6Form
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
            this.panel1 = new System.Windows.Forms.Panel();
            this.ReloadListButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.ReadCount = new System.Windows.Forms.NumericUpDown();
            this.ReadStartAddress = new System.Windows.Forms.NumericUpDown();
            this.label3 = new System.Windows.Forms.Label();
            this.BlockSize = new FEBuilderGBA.TextBoxEx();
            this.SelectAddress = new FEBuilderGBA.TextBoxEx();
            this.label22 = new System.Windows.Forms.Label();
            this.WriteButton = new System.Windows.Forms.Button();
            this.Address = new System.Windows.Forms.NumericUpDown();
            this.AddressPanel = new System.Windows.Forms.Panel();
            this.label23 = new System.Windows.Forms.Label();
            this.B14 = new System.Windows.Forms.NumericUpDown();
            this.B15 = new System.Windows.Forms.NumericUpDown();
            this.J_0 = new System.Windows.Forms.Label();
            this.J_4 = new System.Windows.Forms.Label();
            this.J_8 = new System.Windows.Forms.Label();
            this.J_12 = new System.Windows.Forms.Label();
            this.J_14 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.B12 = new System.Windows.Forms.NumericUpDown();
            this.B13 = new System.Windows.Forms.NumericUpDown();
            this.label13 = new System.Windows.Forms.Label();
            this.D8 = new System.Windows.Forms.NumericUpDown();
            this.D4 = new System.Windows.Forms.NumericUpDown();
            this.D0 = new System.Windows.Forms.NumericUpDown();
            this.DragTargetPanel = new System.Windows.Forms.Panel();
            this.Comment = new FEBuilderGBA.TextBoxEx();
            this.label11 = new System.Windows.Forms.Label();
            this.X_PIC_ZZZ = new FEBuilderGBA.InterpolatedPictureBox();
            this.label24 = new System.Windows.Forms.Label();
            this.ShowFrameUpDown = new System.Windows.Forms.NumericUpDown();
            this.label17 = new System.Windows.Forms.Label();
            this.X_MAP_PIC = new FEBuilderGBA.InterpolatedPictureBox();
            this.X_UNIT_PIC = new FEBuilderGBA.InterpolatedPictureBox();
            this.panel6 = new System.Windows.Forms.Panel();
            this.AddressListExpandsButton_32766 = new System.Windows.Forms.Button();
            this.LabelFilter = new System.Windows.Forms.Label();
            this.AddressList = new FEBuilderGBA.ListBoxEx();
            this.DragTargetPanel2 = new System.Windows.Forms.Panel();
            this.ExportButton = new System.Windows.Forms.Button();
            this.ImportButton = new System.Windows.Forms.Button();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).BeginInit();
            this.AddressPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.B14)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.B15)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.B12)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.B13)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.D8)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.D4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.D0)).BeginInit();
            this.DragTargetPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.X_PIC_ZZZ)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_MAP_PIC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_UNIT_PIC)).BeginInit();
            this.panel6.SuspendLayout();
            this.DragTargetPanel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.ReloadListButton);
            this.panel1.Controls.Add(this.label1);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Controls.Add(this.ReadCount);
            this.panel1.Controls.Add(this.ReadStartAddress);
            this.panel1.Location = new System.Drawing.Point(13, 12);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(756, 21);
            this.panel1.TabIndex = 61;
            // 
            // ReloadListButton
            // 
            this.ReloadListButton.Location = new System.Drawing.Point(305, -1);
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
            this.label2.Location = new System.Drawing.Point(173, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(55, 21);
            this.label2.TabIndex = 24;
            this.label2.Text = "読込数";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ReadCount
            // 
            this.ReadCount.Location = new System.Drawing.Point(233, 1);
            this.ReadCount.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReadCount.Name = "ReadCount";
            this.ReadCount.Size = new System.Drawing.Size(51, 20);
            this.ReadCount.TabIndex = 28;
            // 
            // ReadStartAddress
            // 
            this.ReadStartAddress.Hexadecimal = true;
            this.ReadStartAddress.Location = new System.Drawing.Point(85, 1);
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
            // label3
            // 
            this.label3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label3.Location = new System.Drawing.Point(157, -1);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(46, 21);
            this.label3.TabIndex = 52;
            this.label3.Text = "Size:";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // BlockSize
            // 
            this.BlockSize.ErrorMessage = "";
            this.BlockSize.Location = new System.Drawing.Point(207, 0);
            this.BlockSize.Name = "BlockSize";
            this.BlockSize.Placeholder = "";
            this.BlockSize.ReadOnly = true;
            this.BlockSize.Size = new System.Drawing.Size(55, 20);
            this.BlockSize.TabIndex = 52;
            // 
            // SelectAddress
            // 
            this.SelectAddress.ErrorMessage = "";
            this.SelectAddress.Location = new System.Drawing.Point(347, 1);
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
            this.label22.Location = new System.Drawing.Point(268, -1);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(79, 21);
            this.label22.TabIndex = 39;
            this.label22.Text = "選択アドレス:";
            this.label22.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WriteButton
            // 
            this.WriteButton.Location = new System.Drawing.Point(475, -1);
            this.WriteButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(107, 20);
            this.WriteButton.TabIndex = 1;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            // 
            // Address
            // 
            this.Address.Hexadecimal = true;
            this.Address.Location = new System.Drawing.Point(60, 1);
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
            this.AddressPanel.Location = new System.Drawing.Point(187, 31);
            this.AddressPanel.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.AddressPanel.Name = "AddressPanel";
            this.AddressPanel.Size = new System.Drawing.Size(582, 21);
            this.AddressPanel.TabIndex = 60;
            // 
            // label23
            // 
            this.label23.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label23.Location = new System.Drawing.Point(-1, -1);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(55, 22);
            this.label23.TabIndex = 1;
            this.label23.Text = "アドレス";
            this.label23.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // B14
            // 
            this.B14.Hexadecimal = true;
            this.B14.Location = new System.Drawing.Point(126, 121);
            this.B14.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.B14.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.B14.Name = "B14";
            this.B14.Size = new System.Drawing.Size(39, 20);
            this.B14.TabIndex = 11;
            // 
            // B15
            // 
            this.B15.Hexadecimal = true;
            this.B15.Location = new System.Drawing.Point(171, 121);
            this.B15.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.B15.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.B15.Name = "B15";
            this.B15.Size = new System.Drawing.Size(39, 20);
            this.B15.TabIndex = 12;
            // 
            // J_0
            // 
            this.J_0.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_0.Location = new System.Drawing.Point(1, 7);
            this.J_0.Name = "J_0";
            this.J_0.Size = new System.Drawing.Size(120, 21);
            this.J_0.TabIndex = 68;
            this.J_0.Text = "ユニット顔";
            this.J_0.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // J_4
            // 
            this.J_4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_4.Location = new System.Drawing.Point(1, 33);
            this.J_4.Name = "J_4";
            this.J_4.Size = new System.Drawing.Size(120, 21);
            this.J_4.TabIndex = 69;
            this.J_4.Text = "マップ顔";
            this.J_4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // J_8
            // 
            this.J_8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_8.Location = new System.Drawing.Point(1, 60);
            this.J_8.Name = "J_8";
            this.J_8.Size = new System.Drawing.Size(120, 21);
            this.J_8.TabIndex = 70;
            this.J_8.Text = "パレット";
            this.J_8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.J_8.Click += new System.EventHandler(this.J_8_Click);
            // 
            // J_12
            // 
            this.J_12.AccessibleDescription = "@PORTRAIT_MOUTH_COORD";
            this.J_12.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_12.Location = new System.Drawing.Point(1, 89);
            this.J_12.Name = "J_12";
            this.J_12.Size = new System.Drawing.Size(93, 21);
            this.J_12.TabIndex = 72;
            this.J_12.Text = "口座標";
            this.J_12.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // J_14
            // 
            this.J_14.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_14.Location = new System.Drawing.Point(1, 121);
            this.J_14.Name = "J_14";
            this.J_14.Size = new System.Drawing.Size(120, 21);
            this.J_14.TabIndex = 75;
            this.J_14.Text = "00";
            this.J_14.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label14
            // 
            this.label14.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label14.Location = new System.Drawing.Point(95, 89);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(25, 21);
            this.label14.TabIndex = 78;
            this.label14.Text = "X:";
            this.label14.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // B12
            // 
            this.B12.Location = new System.Drawing.Point(125, 92);
            this.B12.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.B12.Name = "B12";
            this.B12.Size = new System.Drawing.Size(51, 20);
            this.B12.TabIndex = 5;
            // 
            // B13
            // 
            this.B13.Location = new System.Drawing.Point(213, 93);
            this.B13.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.B13.Name = "B13";
            this.B13.Size = new System.Drawing.Size(51, 20);
            this.B13.TabIndex = 6;
            // 
            // label13
            // 
            this.label13.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label13.Location = new System.Drawing.Point(182, 90);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(25, 21);
            this.label13.TabIndex = 80;
            this.label13.Text = "Y:";
            this.label13.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // D8
            // 
            this.D8.Hexadecimal = true;
            this.D8.Location = new System.Drawing.Point(123, 60);
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
            // D4
            // 
            this.D4.Hexadecimal = true;
            this.D4.Location = new System.Drawing.Point(123, 33);
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
            // D0
            // 
            this.D0.Hexadecimal = true;
            this.D0.Location = new System.Drawing.Point(123, 7);
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
            // DragTargetPanel
            // 
            this.DragTargetPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.DragTargetPanel.Controls.Add(this.Comment);
            this.DragTargetPanel.Controls.Add(this.label11);
            this.DragTargetPanel.Controls.Add(this.X_PIC_ZZZ);
            this.DragTargetPanel.Controls.Add(this.label24);
            this.DragTargetPanel.Controls.Add(this.ShowFrameUpDown);
            this.DragTargetPanel.Controls.Add(this.label17);
            this.DragTargetPanel.Controls.Add(this.X_MAP_PIC);
            this.DragTargetPanel.Controls.Add(this.D0);
            this.DragTargetPanel.Controls.Add(this.D4);
            this.DragTargetPanel.Controls.Add(this.D8);
            this.DragTargetPanel.Controls.Add(this.label13);
            this.DragTargetPanel.Controls.Add(this.B13);
            this.DragTargetPanel.Controls.Add(this.B12);
            this.DragTargetPanel.Controls.Add(this.label14);
            this.DragTargetPanel.Controls.Add(this.J_14);
            this.DragTargetPanel.Controls.Add(this.J_12);
            this.DragTargetPanel.Controls.Add(this.J_8);
            this.DragTargetPanel.Controls.Add(this.J_4);
            this.DragTargetPanel.Controls.Add(this.J_0);
            this.DragTargetPanel.Controls.Add(this.X_UNIT_PIC);
            this.DragTargetPanel.Controls.Add(this.B15);
            this.DragTargetPanel.Controls.Add(this.B14);
            this.DragTargetPanel.Location = new System.Drawing.Point(187, 51);
            this.DragTargetPanel.Name = "DragTargetPanel";
            this.DragTargetPanel.Size = new System.Drawing.Size(582, 263);
            this.DragTargetPanel.TabIndex = 62;
            // 
            // Comment
            // 
            this.Comment.ErrorMessage = "";
            this.Comment.Location = new System.Drawing.Point(126, 242);
            this.Comment.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Comment.Name = "Comment";
            this.Comment.Placeholder = "";
            this.Comment.Size = new System.Drawing.Size(217, 20);
            this.Comment.TabIndex = 202;
            // 
            // label11
            // 
            this.label11.AccessibleDescription = "@COMMENT";
            this.label11.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label11.Location = new System.Drawing.Point(1, 240);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(120, 21);
            this.label11.TabIndex = 201;
            this.label11.Text = "コメント";
            this.label11.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_PIC_ZZZ
            // 
            this.X_PIC_ZZZ.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_PIC_ZZZ.Location = new System.Drawing.Point(357, 152);
            this.X_PIC_ZZZ.Name = "X_PIC_ZZZ";
            this.X_PIC_ZZZ.Size = new System.Drawing.Size(101, 89);
            this.X_PIC_ZZZ.TabIndex = 176;
            this.X_PIC_ZZZ.TabStop = false;
            // 
            // label24
            // 
            this.label24.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label24.Location = new System.Drawing.Point(449, 7);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(107, 21);
            this.label24.TabIndex = 175;
            this.label24.Text = "表示例";
            this.label24.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ShowFrameUpDown
            // 
            this.ShowFrameUpDown.Hexadecimal = true;
            this.ShowFrameUpDown.Location = new System.Drawing.Point(515, 30);
            this.ShowFrameUpDown.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ShowFrameUpDown.Maximum = new decimal(new int[] {
            9,
            0,
            0,
            0});
            this.ShowFrameUpDown.Name = "ShowFrameUpDown";
            this.ShowFrameUpDown.Size = new System.Drawing.Size(39, 20);
            this.ShowFrameUpDown.TabIndex = 174;
            this.ShowFrameUpDown.ValueChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // label17
            // 
            this.label17.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label17.Location = new System.Drawing.Point(449, 29);
            this.label17.Name = "label17";
            this.label17.Size = new System.Drawing.Size(62, 21);
            this.label17.TabIndex = 173;
            this.label17.Text = "フレーム";
            this.label17.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_MAP_PIC
            // 
            this.X_MAP_PIC.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_MAP_PIC.Location = new System.Drawing.Point(270, 152);
            this.X_MAP_PIC.Name = "X_MAP_PIC";
            this.X_MAP_PIC.Size = new System.Drawing.Size(71, 65);
            this.X_MAP_PIC.TabIndex = 87;
            this.X_MAP_PIC.TabStop = false;
            // 
            // X_UNIT_PIC
            // 
            this.X_UNIT_PIC.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_UNIT_PIC.Location = new System.Drawing.Point(270, 7);
            this.X_UNIT_PIC.Name = "X_UNIT_PIC";
            this.X_UNIT_PIC.Size = new System.Drawing.Size(149, 131);
            this.X_UNIT_PIC.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.X_UNIT_PIC.TabIndex = 67;
            this.X_UNIT_PIC.TabStop = false;
            // 
            // panel6
            // 
            this.panel6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel6.Controls.Add(this.AddressListExpandsButton_32766);
            this.panel6.Controls.Add(this.LabelFilter);
            this.panel6.Controls.Add(this.AddressList);
            this.panel6.Location = new System.Drawing.Point(13, 31);
            this.panel6.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.panel6.Name = "panel6";
            this.panel6.Size = new System.Drawing.Size(169, 359);
            this.panel6.TabIndex = 86;
            // 
            // AddressListExpandsButton_32766
            // 
            this.AddressListExpandsButton_32766.Location = new System.Drawing.Point(1, 337);
            this.AddressListExpandsButton_32766.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.AddressListExpandsButton_32766.Name = "AddressListExpandsButton_32766";
            this.AddressListExpandsButton_32766.Size = new System.Drawing.Size(164, 20);
            this.AddressListExpandsButton_32766.TabIndex = 114;
            this.AddressListExpandsButton_32766.Text = "リストの拡張";
            this.AddressListExpandsButton_32766.UseVisualStyleBackColor = true;
            // 
            // LabelFilter
            // 
            this.LabelFilter.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.LabelFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.LabelFilter.Location = new System.Drawing.Point(1, 0);
            this.LabelFilter.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.LabelFilter.Name = "LabelFilter";
            this.LabelFilter.Size = new System.Drawing.Size(167, 18);
            this.LabelFilter.TabIndex = 55;
            this.LabelFilter.Text = "名前";
            this.LabelFilter.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AddressList
            // 
            this.AddressList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.AddressList.FormattingEnabled = true;
            this.AddressList.IntegralHeight = false;
            this.AddressList.Location = new System.Drawing.Point(1, 17);
            this.AddressList.Name = "AddressList";
            this.AddressList.Size = new System.Drawing.Size(169, 316);
            this.AddressList.TabIndex = 0;
            this.AddressList.SelectedIndexChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // DragTargetPanel2
            // 
            this.DragTargetPanel2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.DragTargetPanel2.Controls.Add(this.SelectSourceButton);
            this.DragTargetPanel2.Controls.Add(this.OpenSourceButton);
            this.DragTargetPanel2.Controls.Add(this.ExportButton);
            this.DragTargetPanel2.Controls.Add(this.ImportButton);
            this.DragTargetPanel2.Location = new System.Drawing.Point(187, 341);
            this.DragTargetPanel2.Name = "DragTargetPanel2";
            this.DragTargetPanel2.Size = new System.Drawing.Size(582, 49);
            this.DragTargetPanel2.TabIndex = 87;
            // 
            // ExportButton
            // 
            this.ExportButton.Location = new System.Drawing.Point(125, 11);
            this.ExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ExportButton.Name = "ExportButton";
            this.ExportButton.Size = new System.Drawing.Size(107, 20);
            this.ExportButton.TabIndex = 1;
            this.ExportButton.Text = "画像取出";
            this.ExportButton.UseVisualStyleBackColor = true;
            this.ExportButton.Click += new System.EventHandler(this.ExportButton_Click);
            // 
            // ImportButton
            // 
            this.ImportButton.Location = new System.Drawing.Point(5, 11);
            this.ImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ImportButton.Name = "ImportButton";
            this.ImportButton.Size = new System.Drawing.Size(107, 20);
            this.ImportButton.TabIndex = 0;
            this.ImportButton.Text = "画像読込";
            this.ImportButton.UseVisualStyleBackColor = true;
            this.ImportButton.Click += new System.EventHandler(this.ImportButton_Click);
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(244, 11);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(113, 20);
            this.OpenSourceButton.TabIndex = 2;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(370, 11);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(118, 20);
            this.SelectSourceButton.TabIndex = 3;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // ImagePortraitFE6Form
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(779, 413);
            this.Controls.Add(this.DragTargetPanel2);
            this.Controls.Add(this.panel6);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.DragTargetPanel);
            this.Controls.Add(this.AddressPanel);
            this.Name = "ImagePortraitFE6Form";
            this.Text = "顔画像";
            this.Load += new System.EventHandler(this.ImagePortraitFE6Form_Load);
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).EndInit();
            this.AddressPanel.ResumeLayout(false);
            this.AddressPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.B14)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.B15)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.B12)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.B13)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.D8)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.D4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.D0)).EndInit();
            this.DragTargetPanel.ResumeLayout(false);
            this.DragTargetPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.X_PIC_ZZZ)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_MAP_PIC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_UNIT_PIC)).EndInit();
            this.panel6.ResumeLayout(false);
            this.DragTargetPanel2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button ReloadListButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.NumericUpDown ReadCount;
        private System.Windows.Forms.NumericUpDown ReadStartAddress;
        private System.Windows.Forms.Label label3;
        private FEBuilderGBA.TextBoxEx BlockSize;
        private FEBuilderGBA.TextBoxEx SelectAddress;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.Button WriteButton;
        private System.Windows.Forms.NumericUpDown Address;
        private System.Windows.Forms.Panel AddressPanel;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.NumericUpDown B14;
        private System.Windows.Forms.NumericUpDown B15;
        private System.Windows.Forms.Label J_0;
        private System.Windows.Forms.Label J_4;
        private System.Windows.Forms.Label J_8;
        private System.Windows.Forms.Label J_12;
        private System.Windows.Forms.Label J_14;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.NumericUpDown B12;
        private System.Windows.Forms.NumericUpDown B13;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.NumericUpDown D8;
        private System.Windows.Forms.NumericUpDown D4;
        private System.Windows.Forms.NumericUpDown D0;
        private System.Windows.Forms.Panel DragTargetPanel;
        private System.Windows.Forms.Panel panel6;
        private System.Windows.Forms.Label LabelFilter;
        private ListBoxEx AddressList;
        private System.Windows.Forms.Button AddressListExpandsButton_32766;
        private System.Windows.Forms.Panel DragTargetPanel2;
        private System.Windows.Forms.Button ExportButton;
        private System.Windows.Forms.Button ImportButton;
        private InterpolatedPictureBox X_UNIT_PIC;
        private InterpolatedPictureBox X_MAP_PIC;
        private System.Windows.Forms.Label label24;
        private System.Windows.Forms.NumericUpDown ShowFrameUpDown;
        private System.Windows.Forms.Label label17;
        private InterpolatedPictureBox X_PIC_ZZZ;
        private TextBoxEx Comment;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Button SelectSourceButton;
        private System.Windows.Forms.Button OpenSourceButton;
    }
}