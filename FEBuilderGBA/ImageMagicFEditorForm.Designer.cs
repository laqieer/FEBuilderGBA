﻿namespace FEBuilderGBA
{
    partial class ImageMagicFEditorForm
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
            this.panel8 = new System.Windows.Forms.Panel();
            this.MagicListExpandsButton = new System.Windows.Forms.Button();
            this.AddressList = new FEBuilderGBA.ListBoxEx();
            this.LabelFilter = new System.Windows.Forms.Label();
            this.panel3 = new System.Windows.Forms.Panel();
            this.ReloadListButton = new System.Windows.Forms.Button();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.ReadCount = new System.Windows.Forms.NumericUpDown();
            this.ReadStartAddress = new System.Windows.Forms.NumericUpDown();
            this.LinkInternt = new System.Windows.Forms.Label();
            this.DragTargetPanel = new System.Windows.Forms.Panel();
            this.BinInfo = new FEBuilderGBA.TextBoxEx();
            this.MagicComment = new FEBuilderGBA.TextBoxEx();
            this.label2 = new System.Windows.Forms.Label();
            this.X_N_JumpEditor = new System.Windows.Forms.Button();
            this.DimComboBox = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.ShowZoomComboBox = new System.Windows.Forms.ComboBox();
            this.label25 = new System.Windows.Forms.Label();
            this.P16 = new System.Windows.Forms.NumericUpDown();
            this.J_16 = new System.Windows.Forms.Label();
            this.label26 = new System.Windows.Forms.Label();
            this.label24 = new System.Windows.Forms.Label();
            this.MagicAnimeExportButton = new System.Windows.Forms.Button();
            this.MagicAnimeImportButton = new System.Windows.Forms.Button();
            this.X_B_ANIME_PIC2 = new FEBuilderGBA.InterpolatedPictureBox();
            this.ShowFrameUpDown = new System.Windows.Forms.NumericUpDown();
            this.numericUpDown26 = new System.Windows.Forms.NumericUpDown();
            this.P12 = new System.Windows.Forms.NumericUpDown();
            this.P8 = new System.Windows.Forms.NumericUpDown();
            this.P4 = new System.Windows.Forms.NumericUpDown();
            this.P0 = new System.Windows.Forms.NumericUpDown();
            this.J_12 = new System.Windows.Forms.Label();
            this.J_8 = new System.Windows.Forms.Label();
            this.J_4 = new System.Windows.Forms.Label();
            this.J_0 = new System.Windows.Forms.Label();
            this.panel5 = new System.Windows.Forms.Panel();
            this.BlockSize = new FEBuilderGBA.TextBoxEx();
            this.label14 = new System.Windows.Forms.Label();
            this.SelectAddress = new FEBuilderGBA.TextBoxEx();
            this.label15 = new System.Windows.Forms.Label();
            this.WriteButton = new System.Windows.Forms.Button();
            this.Address = new System.Windows.Forms.NumericUpDown();
            this.label16 = new System.Windows.Forms.Label();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.panel8.SuspendLayout();
            this.panel3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).BeginInit();
            this.DragTargetPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.P16)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_B_ANIME_PIC2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown26)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P12)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P8)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P4)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.P0)).BeginInit();
            this.panel5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).BeginInit();
            this.SuspendLayout();
            // 
            // panel8
            // 
            this.panel8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel8.Controls.Add(this.MagicListExpandsButton);
            this.panel8.Controls.Add(this.AddressList);
            this.panel8.Controls.Add(this.LabelFilter);
            this.panel8.Location = new System.Drawing.Point(9, 23);
            this.panel8.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.panel8.Name = "panel8";
            this.panel8.Size = new System.Drawing.Size(188, 333);
            this.panel8.TabIndex = 117;
            // 
            // MagicListExpandsButton
            // 
            this.MagicListExpandsButton.Location = new System.Drawing.Point(1, 313);
            this.MagicListExpandsButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MagicListExpandsButton.Name = "MagicListExpandsButton";
            this.MagicListExpandsButton.Size = new System.Drawing.Size(183, 20);
            this.MagicListExpandsButton.TabIndex = 115;
            this.MagicListExpandsButton.Text = "リストの拡張";
            this.MagicListExpandsButton.UseVisualStyleBackColor = true;
            this.MagicListExpandsButton.Click += new System.EventHandler(this.MagicListExpandsButton_Click);
            // 
            // AddressList
            // 
            this.AddressList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.AddressList.FormattingEnabled = true;
            this.AddressList.IntegralHeight = false;
            this.AddressList.Location = new System.Drawing.Point(-1, 19);
            this.AddressList.Name = "AddressList";
            this.AddressList.Size = new System.Drawing.Size(189, 292);
            this.AddressList.TabIndex = 107;
            this.AddressList.SelectedIndexChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // LabelFilter
            // 
            this.LabelFilter.AccessibleDescription = "@EXTENDSMAGIC_LISTBOX";
            this.LabelFilter.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.LabelFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.LabelFilter.Location = new System.Drawing.Point(-1, -1);
            this.LabelFilter.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.LabelFilter.Name = "LabelFilter";
            this.LabelFilter.Size = new System.Drawing.Size(189, 21);
            this.LabelFilter.TabIndex = 108;
            this.LabelFilter.Text = "名前";
            this.LabelFilter.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // panel3
            // 
            this.panel3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel3.Controls.Add(this.ReloadListButton);
            this.panel3.Controls.Add(this.label8);
            this.panel3.Controls.Add(this.label9);
            this.panel3.Controls.Add(this.ReadCount);
            this.panel3.Controls.Add(this.ReadStartAddress);
            this.panel3.Location = new System.Drawing.Point(9, 4);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(805, 21);
            this.panel3.TabIndex = 115;
            // 
            // ReloadListButton
            // 
            this.ReloadListButton.Location = new System.Drawing.Point(337, -1);
            this.ReloadListButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReloadListButton.Name = "ReloadListButton";
            this.ReloadListButton.Size = new System.Drawing.Size(74, 21);
            this.ReloadListButton.TabIndex = 25;
            this.ReloadListButton.Text = "再取得";
            this.ReloadListButton.UseVisualStyleBackColor = true;
            // 
            // label8
            // 
            this.label8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label8.Location = new System.Drawing.Point(-1, -1);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(88, 22);
            this.label8.TabIndex = 23;
            this.label8.Text = "先頭アドレス";
            this.label8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label9
            // 
            this.label9.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label9.Location = new System.Drawing.Point(191, 0);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(62, 22);
            this.label9.TabIndex = 24;
            this.label9.Text = "読込数";
            this.label9.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ReadCount
            // 
            this.ReadCount.Location = new System.Drawing.Point(259, 1);
            this.ReadCount.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReadCount.Maximum = new decimal(new int[] {
            256,
            0,
            0,
            0});
            this.ReadCount.Name = "ReadCount";
            this.ReadCount.Size = new System.Drawing.Size(53, 20);
            this.ReadCount.TabIndex = 28;
            // 
            // ReadStartAddress
            // 
            this.ReadStartAddress.Hexadecimal = true;
            this.ReadStartAddress.Location = new System.Drawing.Point(100, 1);
            this.ReadStartAddress.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReadStartAddress.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.ReadStartAddress.Name = "ReadStartAddress";
            this.ReadStartAddress.Size = new System.Drawing.Size(87, 20);
            this.ReadStartAddress.TabIndex = 27;
            // 
            // LinkInternt
            // 
            this.LinkInternt.AutoSize = true;
            this.LinkInternt.Location = new System.Drawing.Point(5, 293);
            this.LinkInternt.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.LinkInternt.Name = "LinkInternt";
            this.LinkInternt.Size = new System.Drawing.Size(183, 13);
            this.LinkInternt.TabIndex = 29;
            this.LinkInternt.Text = "インターネットから新しいリソースを探す";
            this.LinkInternt.Click += new System.EventHandler(this.LinkInternt_Click);
            // 
            // DragTargetPanel
            // 
            this.DragTargetPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.DragTargetPanel.Controls.Add(this.SelectSourceButton);
            this.DragTargetPanel.Controls.Add(this.OpenSourceButton);
            this.DragTargetPanel.Controls.Add(this.BinInfo);
            this.DragTargetPanel.Controls.Add(this.LinkInternt);
            this.DragTargetPanel.Controls.Add(this.MagicComment);
            this.DragTargetPanel.Controls.Add(this.label2);
            this.DragTargetPanel.Controls.Add(this.X_N_JumpEditor);
            this.DragTargetPanel.Controls.Add(this.DimComboBox);
            this.DragTargetPanel.Controls.Add(this.label3);
            this.DragTargetPanel.Controls.Add(this.ShowZoomComboBox);
            this.DragTargetPanel.Controls.Add(this.label25);
            this.DragTargetPanel.Controls.Add(this.P16);
            this.DragTargetPanel.Controls.Add(this.J_16);
            this.DragTargetPanel.Controls.Add(this.label26);
            this.DragTargetPanel.Controls.Add(this.label24);
            this.DragTargetPanel.Controls.Add(this.MagicAnimeExportButton);
            this.DragTargetPanel.Controls.Add(this.MagicAnimeImportButton);
            this.DragTargetPanel.Controls.Add(this.X_B_ANIME_PIC2);
            this.DragTargetPanel.Controls.Add(this.ShowFrameUpDown);
            this.DragTargetPanel.Controls.Add(this.numericUpDown26);
            this.DragTargetPanel.Controls.Add(this.P12);
            this.DragTargetPanel.Controls.Add(this.P8);
            this.DragTargetPanel.Controls.Add(this.P4);
            this.DragTargetPanel.Controls.Add(this.P0);
            this.DragTargetPanel.Controls.Add(this.J_12);
            this.DragTargetPanel.Controls.Add(this.J_8);
            this.DragTargetPanel.Controls.Add(this.J_4);
            this.DragTargetPanel.Controls.Add(this.J_0);
            this.DragTargetPanel.Location = new System.Drawing.Point(201, 43);
            this.DragTargetPanel.Name = "DragTargetPanel";
            this.DragTargetPanel.Size = new System.Drawing.Size(612, 313);
            this.DragTargetPanel.TabIndex = 116;
            // 
            // BinInfo
            // 
            this.BinInfo.ErrorMessage = "";
            this.BinInfo.Location = new System.Drawing.Point(207, 198);
            this.BinInfo.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.BinInfo.Name = "BinInfo";
            this.BinInfo.Placeholder = "";
            this.BinInfo.ReadOnly = true;
            this.BinInfo.Size = new System.Drawing.Size(401, 20);
            this.BinInfo.TabIndex = 195;
            // 
            // MagicComment
            // 
            this.MagicComment.ErrorMessage = "";
            this.MagicComment.Location = new System.Drawing.Point(75, 140);
            this.MagicComment.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.MagicComment.Name = "MagicComment";
            this.MagicComment.Placeholder = "";
            this.MagicComment.Size = new System.Drawing.Size(127, 20);
            this.MagicComment.TabIndex = 194;
            // 
            // label2
            // 
            this.label2.AccessibleDescription = "@COMMENT";
            this.label2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label2.Location = new System.Drawing.Point(3, 138);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(68, 21);
            this.label2.TabIndex = 193;
            this.label2.Text = "コメント";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_N_JumpEditor
            // 
            this.X_N_JumpEditor.Location = new System.Drawing.Point(109, 244);
            this.X_N_JumpEditor.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.X_N_JumpEditor.Name = "X_N_JumpEditor";
            this.X_N_JumpEditor.Size = new System.Drawing.Size(91, 21);
            this.X_N_JumpEditor.TabIndex = 188;
            this.X_N_JumpEditor.Text = "エディタ";
            this.X_N_JumpEditor.UseVisualStyleBackColor = true;
            this.X_N_JumpEditor.Click += new System.EventHandler(this.X_N_JumpEditor_Click);
            // 
            // DimComboBox
            // 
            this.DimComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.DimComboBox.FormattingEnabled = true;
            this.DimComboBox.Items.AddRange(new object[] {
            "dim_pc",
            "dim",
            "NULL(EMPTY)"});
            this.DimComboBox.Location = new System.Drawing.Point(75, 115);
            this.DimComboBox.Name = "DimComboBox";
            this.DimComboBox.Size = new System.Drawing.Size(127, 21);
            this.DimComboBox.TabIndex = 184;
            // 
            // label3
            // 
            this.label3.AccessibleDescription = "@MAGICDIM";
            this.label3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label3.Location = new System.Drawing.Point(3, 115);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(68, 21);
            this.label3.TabIndex = 183;
            this.label3.Text = "dim";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // ShowZoomComboBox
            // 
            this.ShowZoomComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.ShowZoomComboBox.FormattingEnabled = true;
            this.ShowZoomComboBox.Items.AddRange(new object[] {
            "拡大して描画",
            "拡大しないで描画"});
            this.ShowZoomComboBox.Location = new System.Drawing.Point(84, 195);
            this.ShowZoomComboBox.Name = "ShowZoomComboBox";
            this.ShowZoomComboBox.Size = new System.Drawing.Size(118, 21);
            this.ShowZoomComboBox.TabIndex = 182;
            this.ShowZoomComboBox.SelectedIndexChanged += new System.EventHandler(this.ShowZoomComboBox_SelectedIndexChanged);
            // 
            // label25
            // 
            this.label25.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label25.Location = new System.Drawing.Point(3, 194);
            this.label25.Name = "label25";
            this.label25.Size = new System.Drawing.Size(77, 22);
            this.label25.TabIndex = 181;
            this.label25.Text = "拡大";
            this.label25.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // P16
            // 
            this.P16.Hexadecimal = true;
            this.P16.Location = new System.Drawing.Point(120, 85);
            this.P16.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.P16.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P16.Name = "P16";
            this.P16.Size = new System.Drawing.Size(81, 20);
            this.P16.TabIndex = 177;
            // 
            // J_16
            // 
            this.J_16.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_16.Location = new System.Drawing.Point(1, 83);
            this.J_16.Name = "J_16";
            this.J_16.Size = new System.Drawing.Size(114, 21);
            this.J_16.TabIndex = 176;
            this.J_16.Text = "OBBGLeftToRight";
            this.J_16.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label26
            // 
            this.label26.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label26.Location = new System.Drawing.Point(3, 215);
            this.label26.Name = "label26";
            this.label26.Size = new System.Drawing.Size(77, 21);
            this.label26.TabIndex = 175;
            this.label26.Text = "フレーム";
            this.label26.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label24
            // 
            this.label24.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label24.Location = new System.Drawing.Point(3, 173);
            this.label24.Name = "label24";
            this.label24.Size = new System.Drawing.Size(199, 21);
            this.label24.TabIndex = 171;
            this.label24.Text = "表示例";
            this.label24.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // MagicAnimeExportButton
            // 
            this.MagicAnimeExportButton.Location = new System.Drawing.Point(481, 269);
            this.MagicAnimeExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.MagicAnimeExportButton.Name = "MagicAnimeExportButton";
            this.MagicAnimeExportButton.Size = new System.Drawing.Size(127, 41);
            this.MagicAnimeExportButton.TabIndex = 168;
            this.MagicAnimeExportButton.Text = "魔法アニメの書出し";
            this.MagicAnimeExportButton.UseVisualStyleBackColor = true;
            this.MagicAnimeExportButton.Click += new System.EventHandler(this.MagicAnimeExportButton_Click);
            // 
            // MagicAnimeImportButton
            // 
            this.MagicAnimeImportButton.Location = new System.Drawing.Point(347, 269);
            this.MagicAnimeImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.MagicAnimeImportButton.Name = "MagicAnimeImportButton";
            this.MagicAnimeImportButton.Size = new System.Drawing.Size(127, 41);
            this.MagicAnimeImportButton.TabIndex = 167;
            this.MagicAnimeImportButton.Text = "魔法アニメの読込";
            this.MagicAnimeImportButton.UseVisualStyleBackColor = true;
            this.MagicAnimeImportButton.Click += new System.EventHandler(this.MagicAnimeImportButton_Click);
            // 
            // X_B_ANIME_PIC2
            // 
            this.X_B_ANIME_PIC2.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_B_ANIME_PIC2.Location = new System.Drawing.Point(207, 5);
            this.X_B_ANIME_PIC2.Name = "X_B_ANIME_PIC2";
            this.X_B_ANIME_PIC2.Size = new System.Drawing.Size(400, 188);
            this.X_B_ANIME_PIC2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.X_B_ANIME_PIC2.TabIndex = 166;
            this.X_B_ANIME_PIC2.TabStop = false;
            // 
            // ShowFrameUpDown
            // 
            this.ShowFrameUpDown.Hexadecimal = true;
            this.ShowFrameUpDown.Location = new System.Drawing.Point(85, 217);
            this.ShowFrameUpDown.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ShowFrameUpDown.Maximum = new decimal(new int[] {
            255,
            0,
            0,
            0});
            this.ShowFrameUpDown.Name = "ShowFrameUpDown";
            this.ShowFrameUpDown.Size = new System.Drawing.Size(44, 20);
            this.ShowFrameUpDown.TabIndex = 160;
            this.ShowFrameUpDown.ValueChanged += new System.EventHandler(this.ShowFrameUpDown_ValueChanged);
            // 
            // numericUpDown26
            // 
            this.numericUpDown26.Hexadecimal = true;
            this.numericUpDown26.Location = new System.Drawing.Point(-87, 29);
            this.numericUpDown26.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.numericUpDown26.Maximum = new decimal(new int[] {
            255,
            0,
            0,
            0});
            this.numericUpDown26.Name = "numericUpDown26";
            this.numericUpDown26.Size = new System.Drawing.Size(44, 20);
            this.numericUpDown26.TabIndex = 154;
            // 
            // P12
            // 
            this.P12.Hexadecimal = true;
            this.P12.Location = new System.Drawing.Point(120, 65);
            this.P12.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.P12.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P12.Name = "P12";
            this.P12.Size = new System.Drawing.Size(81, 20);
            this.P12.TabIndex = 80;
            // 
            // P8
            // 
            this.P8.Hexadecimal = true;
            this.P8.Location = new System.Drawing.Point(120, 45);
            this.P8.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.P8.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P8.Name = "P8";
            this.P8.Size = new System.Drawing.Size(81, 20);
            this.P8.TabIndex = 79;
            // 
            // P4
            // 
            this.P4.Hexadecimal = true;
            this.P4.Location = new System.Drawing.Point(120, 24);
            this.P4.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.P4.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P4.Name = "P4";
            this.P4.Size = new System.Drawing.Size(81, 20);
            this.P4.TabIndex = 78;
            // 
            // P0
            // 
            this.P0.Hexadecimal = true;
            this.P0.Location = new System.Drawing.Point(120, 5);
            this.P0.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.P0.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.P0.Name = "P0";
            this.P0.Size = new System.Drawing.Size(81, 20);
            this.P0.TabIndex = 77;
            // 
            // J_12
            // 
            this.J_12.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_12.Location = new System.Drawing.Point(1, 63);
            this.J_12.Name = "J_12";
            this.J_12.Size = new System.Drawing.Size(114, 21);
            this.J_12.TabIndex = 75;
            this.J_12.Text = "OBJBGRightToLeft";
            this.J_12.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // J_8
            // 
            this.J_8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_8.Location = new System.Drawing.Point(1, 43);
            this.J_8.Name = "J_8";
            this.J_8.Size = new System.Drawing.Size(114, 21);
            this.J_8.TabIndex = 74;
            this.J_8.Text = "OBJLeftToRight";
            this.J_8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // J_4
            // 
            this.J_4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_4.Location = new System.Drawing.Point(1, 23);
            this.J_4.Name = "J_4";
            this.J_4.Size = new System.Drawing.Size(114, 21);
            this.J_4.TabIndex = 73;
            this.J_4.Text = "OBJRightToLeft";
            this.J_4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // J_0
            // 
            this.J_0.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.J_0.Location = new System.Drawing.Point(1, 3);
            this.J_0.Name = "J_0";
            this.J_0.Size = new System.Drawing.Size(114, 21);
            this.J_0.TabIndex = 72;
            this.J_0.Text = "FrameData";
            this.J_0.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panel5
            // 
            this.panel5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel5.Controls.Add(this.BlockSize);
            this.panel5.Controls.Add(this.label14);
            this.panel5.Controls.Add(this.SelectAddress);
            this.panel5.Controls.Add(this.label15);
            this.panel5.Controls.Add(this.WriteButton);
            this.panel5.Controls.Add(this.Address);
            this.panel5.Controls.Add(this.label16);
            this.panel5.Location = new System.Drawing.Point(201, 23);
            this.panel5.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.panel5.Name = "panel5";
            this.panel5.Size = new System.Drawing.Size(612, 21);
            this.panel5.TabIndex = 114;
            // 
            // BlockSize
            // 
            this.BlockSize.ErrorMessage = "";
            this.BlockSize.Location = new System.Drawing.Point(220, 1);
            this.BlockSize.Name = "BlockSize";
            this.BlockSize.Placeholder = "";
            this.BlockSize.ReadOnly = true;
            this.BlockSize.Size = new System.Drawing.Size(57, 20);
            this.BlockSize.TabIndex = 52;
            // 
            // label14
            // 
            this.label14.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label14.Location = new System.Drawing.Point(169, -1);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(48, 21);
            this.label14.TabIndex = 52;
            this.label14.Text = "Size:";
            this.label14.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // SelectAddress
            // 
            this.SelectAddress.ErrorMessage = "";
            this.SelectAddress.Location = new System.Drawing.Point(372, 1);
            this.SelectAddress.Name = "SelectAddress";
            this.SelectAddress.Placeholder = "";
            this.SelectAddress.ReadOnly = true;
            this.SelectAddress.Size = new System.Drawing.Size(114, 20);
            this.SelectAddress.TabIndex = 40;
            // 
            // label15
            // 
            this.label15.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label15.Location = new System.Drawing.Point(281, -1);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(82, 21);
            this.label15.TabIndex = 39;
            this.label15.Text = "選択アドレス:";
            this.label15.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WriteButton
            // 
            this.WriteButton.Location = new System.Drawing.Point(499, -1);
            this.WriteButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(111, 20);
            this.WriteButton.TabIndex = 9;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            this.WriteButton.Click += new System.EventHandler(this.N_WriteButton_Click);
            // 
            // Address
            // 
            this.Address.Hexadecimal = true;
            this.Address.Location = new System.Drawing.Point(66, 1);
            this.Address.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.Address.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.Address.Name = "Address";
            this.Address.Size = new System.Drawing.Size(87, 20);
            this.Address.TabIndex = 8;
            // 
            // label16
            // 
            this.label16.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label16.Location = new System.Drawing.Point(-1, 0);
            this.label16.Name = "label16";
            this.label16.Size = new System.Drawing.Size(62, 22);
            this.label16.TabIndex = 1;
            this.label16.Text = "アドレス";
            this.label16.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(346, 244);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(127, 20);
            this.OpenSourceButton.TabIndex = 196;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(479, 244);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(126, 21);
            this.SelectSourceButton.TabIndex = 197;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // ImageMagicFEditorForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(819, 365);
            this.Controls.Add(this.panel8);
            this.Controls.Add(this.panel3);
            this.Controls.Add(this.DragTargetPanel);
            this.Controls.Add(this.panel5);
            this.Name = "ImageMagicFEditorForm";
            this.Text = "追加魔法 FEditorAdvCSA";
            this.Load += new System.EventHandler(this.ImageMagicForm_Load);
            this.panel8.ResumeLayout(false);
            this.panel3.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).EndInit();
            this.DragTargetPanel.ResumeLayout(false);
            this.DragTargetPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.P16)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_B_ANIME_PIC2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ShowFrameUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown26)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P12)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P8)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P4)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.P0)).EndInit();
            this.panel5.ResumeLayout(false);
            this.panel5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panel8;
        private ListBoxEx AddressList;
        private System.Windows.Forms.Label LabelFilter;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.Button ReloadListButton;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.NumericUpDown ReadCount;
        private System.Windows.Forms.NumericUpDown ReadStartAddress;
        private System.Windows.Forms.Panel DragTargetPanel;
        private System.Windows.Forms.Label label26;
        private System.Windows.Forms.Label label24;
        private System.Windows.Forms.Button MagicAnimeExportButton;
        private System.Windows.Forms.Button MagicAnimeImportButton;
        private System.Windows.Forms.NumericUpDown ShowFrameUpDown;
        private System.Windows.Forms.NumericUpDown numericUpDown26;
        private System.Windows.Forms.NumericUpDown P12;
        private System.Windows.Forms.NumericUpDown P8;
        private System.Windows.Forms.NumericUpDown P4;
        private System.Windows.Forms.NumericUpDown P0;
        private System.Windows.Forms.Label J_12;
        private System.Windows.Forms.Label J_8;
        private System.Windows.Forms.Label J_4;
        private System.Windows.Forms.Label J_0;
        private System.Windows.Forms.Panel panel5;
        private FEBuilderGBA.TextBoxEx BlockSize;
        private System.Windows.Forms.Label label14;
        private FEBuilderGBA.TextBoxEx SelectAddress;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Button WriteButton;
        private System.Windows.Forms.NumericUpDown Address;
        private System.Windows.Forms.Label label16;
        private System.Windows.Forms.NumericUpDown P16;
        private System.Windows.Forms.Label J_16;
        private System.Windows.Forms.ComboBox ShowZoomComboBox;
        private System.Windows.Forms.Label label25;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox DimComboBox;
        private InterpolatedPictureBox X_B_ANIME_PIC2;
        private System.Windows.Forms.Button MagicListExpandsButton;
        private System.Windows.Forms.Button X_N_JumpEditor;
        private TextBoxEx MagicComment;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label LinkInternt;
        private TextBoxEx BinInfo;
        private System.Windows.Forms.Button SelectSourceButton;
        private System.Windows.Forms.Button OpenSourceButton;
    }
}