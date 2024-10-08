﻿namespace FEBuilderGBA
{
    partial class MapEditorForm
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
            this.Zoom = new System.Windows.Forms.ComboBox();
            this.label4 = new System.Windows.Forms.Label();
            this.SizeChangeButton = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.MapStyle = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.MAPCOMBO = new System.Windows.Forms.ComboBox();
            this.MAPCHIPLISTPanel = new System.Windows.Forms.Panel();
            this.MAPCHIPLIST = new FEBuilderGBA.InterpolatedPictureBox();
            this.MapPanel = new System.Windows.Forms.Panel();
            this.MAP = new FEBuilderGBA.InterpolatedPictureBox();
            this.ControlPanel = new System.Windows.Forms.Panel();
            this.panel4 = new System.Windows.Forms.Panel();
            this.SaveASbutton = new System.Windows.Forms.Button();
            this.label7 = new System.Windows.Forms.Label();
            this.WriteButton = new System.Windows.Forms.Button();
            this.LoadButton = new System.Windows.Forms.Button();
            this.MapAddress = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.TilesetZoom = new System.Windows.Forms.ComboBox();
            this.NewMapChange = new System.Windows.Forms.Button();
            this.RedoButton = new System.Windows.Forms.Button();
            this.UndoButon = new System.Windows.Forms.Button();
            this.label6 = new System.Windows.Forms.Label();
            this.MapChange = new System.Windows.Forms.ComboBox();
            this.StyleChangeButton = new System.Windows.Forms.Button();
            this.MapSizeText = new FEBuilderGBA.TextBoxEx();
            this.panel1 = new System.Windows.Forms.Panel();
            this.MapChipInfo = new FEBuilderGBA.TextBoxEx();
            this.panel2 = new System.Windows.Forms.Panel();
            this.splitter1 = new System.Windows.Forms.Splitter();
            this.panel3 = new System.Windows.Forms.Panel();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.MAPCHIPLISTPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MAPCHIPLIST)).BeginInit();
            this.MapPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MAP)).BeginInit();
            this.ControlPanel.SuspendLayout();
            this.panel4.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MapAddress)).BeginInit();
            this.panel1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.panel3.SuspendLayout();
            this.SuspendLayout();
            // 
            // Zoom
            // 
            this.Zoom.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.Zoom.FormattingEnabled = true;
            this.Zoom.Items.AddRange(new object[] {
            "ズームなし",
            "2倍ズーム",
            "3倍ズーム",
            "4倍ズーム"});
            this.Zoom.Location = new System.Drawing.Point(597, 45);
            this.Zoom.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.Zoom.Name = "Zoom";
            this.Zoom.Size = new System.Drawing.Size(177, 21);
            this.Zoom.TabIndex = 8;
            this.Zoom.SelectedIndexChanged += new System.EventHandler(this.Zoom_SelectedIndexChanged);
            // 
            // label4
            // 
            this.label4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label4.Location = new System.Drawing.Point(513, 1);
            this.label4.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(81, 22);
            this.label4.TabIndex = 7;
            this.label4.Text = "UNDO";
            this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // SizeChangeButton
            // 
            this.SizeChangeButton.Location = new System.Drawing.Point(679, 24);
            this.SizeChangeButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.SizeChangeButton.Name = "SizeChangeButton";
            this.SizeChangeButton.Size = new System.Drawing.Size(93, 21);
            this.SizeChangeButton.TabIndex = 6;
            this.SizeChangeButton.Text = "サイズ変更";
            this.SizeChangeButton.UseVisualStyleBackColor = true;
            this.SizeChangeButton.Click += new System.EventHandler(this.SizeChangeButton_Click);
            // 
            // label3
            // 
            this.label3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label3.Location = new System.Drawing.Point(513, 21);
            this.label3.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(81, 22);
            this.label3.TabIndex = 4;
            this.label3.Text = "マップサイズ";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label2
            // 
            this.label2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label2.Location = new System.Drawing.Point(0, 24);
            this.label2.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(89, 22);
            this.label2.TabIndex = 3;
            this.label2.Text = "マップスタイル";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // MapStyle
            // 
            this.MapStyle.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.MapStyle.FormattingEnabled = true;
            this.MapStyle.Location = new System.Drawing.Point(92, 24);
            this.MapStyle.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MapStyle.Name = "MapStyle";
            this.MapStyle.Size = new System.Drawing.Size(225, 21);
            this.MapStyle.TabIndex = 2;
            this.MapStyle.SelectedIndexChanged += new System.EventHandler(this.MapStyle_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label1.Location = new System.Drawing.Point(0, 3);
            this.label1.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(89, 22);
            this.label1.TabIndex = 1;
            this.label1.Text = "編集マップ変更";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // MAPCOMBO
            // 
            this.MAPCOMBO.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.MAPCOMBO.FormattingEnabled = true;
            this.MAPCOMBO.Location = new System.Drawing.Point(92, 3);
            this.MAPCOMBO.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MAPCOMBO.Name = "MAPCOMBO";
            this.MAPCOMBO.Size = new System.Drawing.Size(224, 21);
            this.MAPCOMBO.TabIndex = 0;
            this.MAPCOMBO.SelectedIndexChanged += new System.EventHandler(this.MAPCOMBO_SelectedIndexChanged);
            // 
            // MAPCHIPLISTPanel
            // 
            this.MAPCHIPLISTPanel.AutoScroll = true;
            this.MAPCHIPLISTPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.MAPCHIPLISTPanel.Controls.Add(this.MAPCHIPLIST);
            this.MAPCHIPLISTPanel.Dock = System.Windows.Forms.DockStyle.Left;
            this.MAPCHIPLISTPanel.Location = new System.Drawing.Point(0, 0);
            this.MAPCHIPLISTPanel.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MAPCHIPLISTPanel.Name = "MAPCHIPLISTPanel";
            this.MAPCHIPLISTPanel.Size = new System.Drawing.Size(515, 534);
            this.MAPCHIPLISTPanel.TabIndex = 1;
            // 
            // MAPCHIPLIST
            // 
            this.MAPCHIPLIST.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.MAPCHIPLIST.Location = new System.Drawing.Point(0, 0);
            this.MAPCHIPLIST.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MAPCHIPLIST.Name = "MAPCHIPLIST";
            this.MAPCHIPLIST.Size = new System.Drawing.Size(32, 32);
            this.MAPCHIPLIST.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.MAPCHIPLIST.TabIndex = 0;
            this.MAPCHIPLIST.TabStop = false;
            this.MAPCHIPLIST.Paint += new System.Windows.Forms.PaintEventHandler(this.MAPCHIPLIST_Paint);
            this.MAPCHIPLIST.MouseDown += new System.Windows.Forms.MouseEventHandler(this.MAPCHIPLIST_MouseDown);
            this.MAPCHIPLIST.MouseLeave += new System.EventHandler(this.MAPCHIPLIST_MouseLeave);
            this.MAPCHIPLIST.MouseMove += new System.Windows.Forms.MouseEventHandler(this.MAPCHIPLIST_MouseMove);
            // 
            // MapPanel
            // 
            this.MapPanel.AutoScroll = true;
            this.MapPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.MapPanel.Controls.Add(this.MAP);
            this.MapPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MapPanel.Location = new System.Drawing.Point(0, 0);
            this.MapPanel.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MapPanel.Name = "MapPanel";
            this.MapPanel.Size = new System.Drawing.Size(562, 514);
            this.MapPanel.TabIndex = 0;
            // 
            // MAP
            // 
            this.MAP.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.MAP.Location = new System.Drawing.Point(0, 0);
            this.MAP.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MAP.Name = "MAP";
            this.MAP.Size = new System.Drawing.Size(32, 32);
            this.MAP.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.MAP.TabIndex = 0;
            this.MAP.TabStop = false;
            this.MAP.Paint += new System.Windows.Forms.PaintEventHandler(this.MAP_Paint);
            this.MAP.MouseDown += new System.Windows.Forms.MouseEventHandler(this.MAP_MouseDown);
            this.MAP.MouseLeave += new System.EventHandler(this.MAP_MouseLeave);
            this.MAP.MouseMove += new System.Windows.Forms.MouseEventHandler(this.MAP_MouseMove);
            // 
            // ControlPanel
            // 
            this.ControlPanel.Controls.Add(this.panel4);
            this.ControlPanel.Controls.Add(this.label5);
            this.ControlPanel.Controls.Add(this.TilesetZoom);
            this.ControlPanel.Controls.Add(this.NewMapChange);
            this.ControlPanel.Controls.Add(this.RedoButton);
            this.ControlPanel.Controls.Add(this.UndoButon);
            this.ControlPanel.Controls.Add(this.label6);
            this.ControlPanel.Controls.Add(this.MapChange);
            this.ControlPanel.Controls.Add(this.StyleChangeButton);
            this.ControlPanel.Controls.Add(this.SizeChangeButton);
            this.ControlPanel.Controls.Add(this.MapSizeText);
            this.ControlPanel.Controls.Add(this.Zoom);
            this.ControlPanel.Controls.Add(this.MAPCOMBO);
            this.ControlPanel.Controls.Add(this.label3);
            this.ControlPanel.Controls.Add(this.label4);
            this.ControlPanel.Controls.Add(this.MapStyle);
            this.ControlPanel.Controls.Add(this.label2);
            this.ControlPanel.Controls.Add(this.label1);
            this.ControlPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ControlPanel.Location = new System.Drawing.Point(0, 0);
            this.ControlPanel.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.ControlPanel.Name = "ControlPanel";
            this.ControlPanel.Size = new System.Drawing.Size(1077, 64);
            this.ControlPanel.TabIndex = 1;
            // 
            // panel4
            // 
            this.panel4.Controls.Add(this.SelectSourceButton);
            this.panel4.Controls.Add(this.OpenSourceButton);
            this.panel4.Controls.Add(this.SaveASbutton);
            this.panel4.Controls.Add(this.label7);
            this.panel4.Controls.Add(this.WriteButton);
            this.panel4.Controls.Add(this.LoadButton);
            this.panel4.Controls.Add(this.MapAddress);
            this.panel4.Dock = System.Windows.Forms.DockStyle.Right;
            this.panel4.Location = new System.Drawing.Point(773, 0);
            this.panel4.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.panel4.Name = "panel4";
            this.panel4.Size = new System.Drawing.Size(304, 64);
            this.panel4.TabIndex = 33;
            // 
            // SaveASbutton
            // 
            this.SaveASbutton.Location = new System.Drawing.Point(1, 1);
            this.SaveASbutton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.SaveASbutton.Name = "SaveASbutton";
            this.SaveASbutton.Size = new System.Drawing.Size(138, 23);
            this.SaveASbutton.TabIndex = 18;
            this.SaveASbutton.Text = "ファイルに保存";
            this.SaveASbutton.UseVisualStyleBackColor = true;
            this.SaveASbutton.Click += new System.EventHandler(this.SaveASbutton_Click);
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(163, 48);
            this.label7.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(45, 13);
            this.label7.TabIndex = 32;
            this.label7.Text = "Address";
            // 
            // WriteButton
            // 
            this.WriteButton.Location = new System.Drawing.Point(145, 3);
            this.WriteButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(156, 22);
            this.WriteButton.TabIndex = 16;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            this.WriteButton.Click += new System.EventHandler(this.WriteButton_Click);
            // 
            // LoadButton
            // 
            this.LoadButton.Location = new System.Drawing.Point(3, 43);
            this.LoadButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.LoadButton.Name = "LoadButton";
            this.LoadButton.Size = new System.Drawing.Size(138, 23);
            this.LoadButton.TabIndex = 19;
            this.LoadButton.Text = "ファイルから読込";
            this.LoadButton.UseVisualStyleBackColor = true;
            this.LoadButton.Click += new System.EventHandler(this.LoadButton_Click);
            // 
            // MapAddress
            // 
            this.MapAddress.Hexadecimal = true;
            this.MapAddress.Location = new System.Drawing.Point(222, 46);
            this.MapAddress.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MapAddress.Maximum = new decimal(new int[] {
            -1,
            0,
            0,
            0});
            this.MapAddress.Name = "MapAddress";
            this.MapAddress.ReadOnly = true;
            this.MapAddress.Size = new System.Drawing.Size(78, 20);
            this.MapAddress.TabIndex = 28;
            this.MapAddress.ValueChanged += new System.EventHandler(this.MapAddress_ValueChanged);
            // 
            // label5
            // 
            this.label5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label5.Location = new System.Drawing.Point(0, 44);
            this.label5.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(89, 22);
            this.label5.TabIndex = 31;
            this.label5.Text = "タイルの拡大";
            this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // TilesetZoom
            // 
            this.TilesetZoom.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.TilesetZoom.FormattingEnabled = true;
            this.TilesetZoom.Items.AddRange(new object[] {
            "ズームなし",
            "2倍ズーム",
            "3倍ズーム",
            "4倍ズーム"});
            this.TilesetZoom.Location = new System.Drawing.Point(92, 45);
            this.TilesetZoom.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.TilesetZoom.Name = "TilesetZoom";
            this.TilesetZoom.Size = new System.Drawing.Size(224, 21);
            this.TilesetZoom.TabIndex = 30;
            this.TilesetZoom.SelectedIndexChanged += new System.EventHandler(this.TilesetZoom_SelectedIndexChanged);
            // 
            // NewMapChange
            // 
            this.NewMapChange.Location = new System.Drawing.Point(318, 43);
            this.NewMapChange.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.NewMapChange.Name = "NewMapChange";
            this.NewMapChange.Size = new System.Drawing.Size(193, 21);
            this.NewMapChange.TabIndex = 29;
            this.NewMapChange.Text = "マップ変化追加";
            this.NewMapChange.UseVisualStyleBackColor = true;
            this.NewMapChange.Click += new System.EventHandler(this.NewMapChange_Click);
            // 
            // RedoButton
            // 
            this.RedoButton.Location = new System.Drawing.Point(679, 1);
            this.RedoButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.RedoButton.Name = "RedoButton";
            this.RedoButton.Size = new System.Drawing.Size(49, 21);
            this.RedoButton.TabIndex = 25;
            this.RedoButton.Text = "Redo";
            this.RedoButton.UseVisualStyleBackColor = true;
            this.RedoButton.Click += new System.EventHandler(this.RedoButton_Click);
            // 
            // UndoButon
            // 
            this.UndoButon.Location = new System.Drawing.Point(598, 1);
            this.UndoButon.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.UndoButon.Name = "UndoButon";
            this.UndoButon.Size = new System.Drawing.Size(77, 21);
            this.UndoButon.TabIndex = 24;
            this.UndoButon.Text = "Undo";
            this.UndoButon.UseVisualStyleBackColor = true;
            this.UndoButon.Click += new System.EventHandler(this.UndoButon_Click);
            // 
            // label6
            // 
            this.label6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label6.Location = new System.Drawing.Point(513, 41);
            this.label6.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(81, 22);
            this.label6.TabIndex = 22;
            this.label6.Text = "拡大";
            this.label6.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // MapChange
            // 
            this.MapChange.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.MapChange.FormattingEnabled = true;
            this.MapChange.Location = new System.Drawing.Point(318, 3);
            this.MapChange.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MapChange.Name = "MapChange";
            this.MapChange.Size = new System.Drawing.Size(195, 21);
            this.MapChange.TabIndex = 20;
            this.MapChange.SelectedIndexChanged += new System.EventHandler(this.MapChange_SelectedIndexChanged);
            // 
            // StyleChangeButton
            // 
            this.StyleChangeButton.Location = new System.Drawing.Point(318, 23);
            this.StyleChangeButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.StyleChangeButton.Name = "StyleChangeButton";
            this.StyleChangeButton.Size = new System.Drawing.Size(193, 21);
            this.StyleChangeButton.TabIndex = 17;
            this.StyleChangeButton.Text = "スタイル編集";
            this.StyleChangeButton.UseVisualStyleBackColor = true;
            this.StyleChangeButton.Click += new System.EventHandler(this.StyleChangeButton_Click);
            // 
            // MapSizeText
            // 
            this.MapSizeText.ErrorMessage = "";
            this.MapSizeText.Location = new System.Drawing.Point(598, 25);
            this.MapSizeText.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.MapSizeText.Name = "MapSizeText";
            this.MapSizeText.Placeholder = "";
            this.MapSizeText.ReadOnly = true;
            this.MapSizeText.Size = new System.Drawing.Size(79, 20);
            this.MapSizeText.TabIndex = 5;
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.MapChipInfo);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 514);
            this.panel1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(562, 20);
            this.panel1.TabIndex = 3;
            // 
            // MapChipInfo
            // 
            this.MapChipInfo.Dock = System.Windows.Forms.DockStyle.Fill;
            this.MapChipInfo.ErrorMessage = "";
            this.MapChipInfo.Location = new System.Drawing.Point(0, 0);
            this.MapChipInfo.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.MapChipInfo.Name = "MapChipInfo";
            this.MapChipInfo.Placeholder = "";
            this.MapChipInfo.ReadOnly = true;
            this.MapChipInfo.Size = new System.Drawing.Size(562, 20);
            this.MapChipInfo.TabIndex = 0;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.splitter1);
            this.panel2.Controls.Add(this.panel3);
            this.panel2.Controls.Add(this.MAPCHIPLISTPanel);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel2.Location = new System.Drawing.Point(0, 64);
            this.panel2.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(1077, 534);
            this.panel2.TabIndex = 4;
            // 
            // splitter1
            // 
            this.splitter1.Location = new System.Drawing.Point(515, 0);
            this.splitter1.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.splitter1.Name = "splitter1";
            this.splitter1.Size = new System.Drawing.Size(2, 534);
            this.splitter1.TabIndex = 6;
            this.splitter1.TabStop = false;
            // 
            // panel3
            // 
            this.panel3.Controls.Add(this.MapPanel);
            this.panel3.Controls.Add(this.panel1);
            this.panel3.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panel3.Location = new System.Drawing.Point(515, 0);
            this.panel3.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.panel3.Name = "panel3";
            this.panel3.Size = new System.Drawing.Size(562, 534);
            this.panel3.TabIndex = 5;
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(1, 24);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(139, 22);
            this.OpenSourceButton.TabIndex = 33;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(145, 25);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(155, 21);
            this.SelectSourceButton.TabIndex = 34;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // MapEditorForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(1077, 598);
            this.Controls.Add(this.panel2);
            this.Controls.Add(this.ControlPanel);
            this.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.Name = "MapEditorForm";
            this.Text = "マップエディタ";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.MapEditorForm_FormClosed);
            this.Load += new System.EventHandler(this.MapEditForm_Load);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.MapEditorForm_KeyDown);
            this.MAPCHIPLISTPanel.ResumeLayout(false);
            this.MAPCHIPLISTPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MAPCHIPLIST)).EndInit();
            this.MapPanel.ResumeLayout(false);
            this.MapPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MAP)).EndInit();
            this.ControlPanel.ResumeLayout(false);
            this.ControlPanel.PerformLayout();
            this.panel4.ResumeLayout(false);
            this.panel4.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.MapAddress)).EndInit();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.panel2.ResumeLayout(false);
            this.panel3.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox MAPCOMBO;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ComboBox MapStyle;
        private System.Windows.Forms.Button SizeChangeButton;
        private FEBuilderGBA.TextBoxEx MapSizeText;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox Zoom;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Panel MAPCHIPLISTPanel;
        private System.Windows.Forms.Panel MapPanel;
        private System.Windows.Forms.Panel ControlPanel;
        private System.Windows.Forms.Button WriteButton;
        private System.Windows.Forms.Button StyleChangeButton;
        private System.Windows.Forms.Button SaveASbutton;
        private System.Windows.Forms.Button LoadButton;
        private System.Windows.Forms.ComboBox MapChange;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button RedoButton;
        private System.Windows.Forms.Button UndoButon;
        private System.Windows.Forms.NumericUpDown MapAddress;
        private System.Windows.Forms.Button NewMapChange;
        private InterpolatedPictureBox MAPCHIPLIST;
        private InterpolatedPictureBox MAP;
        private System.Windows.Forms.Panel panel1;
        private FEBuilderGBA.TextBoxEx MapChipInfo;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.ComboBox TilesetZoom;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Panel panel3;
        private System.Windows.Forms.Panel panel4;
        private System.Windows.Forms.Splitter splitter1;
        private System.Windows.Forms.Button SelectSourceButton;
        private System.Windows.Forms.Button OpenSourceButton;
    }
}