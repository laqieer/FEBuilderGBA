namespace FEBuilderGBA
{
    partial class WorldMapImageFE7Form
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
            this.AllWriteButton = new System.Windows.Forms.Button();
            this.panel1 = new System.Windows.Forms.Panel();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.DecreaseColorTSAToolForWorldmapEventButton = new System.Windows.Forms.Button();
            this.WMEvent_Import = new System.Windows.Forms.Button();
            this.WMEvent_ExportButton = new System.Windows.Forms.Button();
            this.WMEvent_Picture = new FEBuilderGBA.InterpolatedPictureBox();
            this.WMEvent_HEADERTSA = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.WMEvent_PALETTE = new System.Windows.Forms.NumericUpDown();
            this.label7 = new System.Windows.Forms.Label();
            this.WMEvent_ZIMAGE = new System.Windows.Forms.NumericUpDown();
            this.label8 = new System.Windows.Forms.Label();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.DecreaseColorTSAToolButton = new System.Windows.Forms.Button();
            this.ImportButton = new System.Windows.Forms.Button();
            this.ExportButton = new System.Windows.Forms.Button();
            this.WMPictureBox = new FEBuilderGBA.InterpolatedPictureBox();
            this.WMtsaMap = new System.Windows.Forms.NumericUpDown();
            this.label4 = new System.Windows.Forms.Label();
            this.WMPalette = new System.Windows.Forms.NumericUpDown();
            this.label1 = new System.Windows.Forms.Label();
            this.WMImageMap = new System.Windows.Forms.NumericUpDown();
            this.label2 = new System.Windows.Forms.Label();
            this.WMTabControl = new System.Windows.Forms.TabControl();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_Picture)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_HEADERTSA)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_PALETTE)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_ZIMAGE)).BeginInit();
            this.tabPage1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.WMPictureBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMtsaMap)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMPalette)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMImageMap)).BeginInit();
            this.WMTabControl.SuspendLayout();
            this.SuspendLayout();
            // 
            // AllWriteButton
            // 
            this.AllWriteButton.Location = new System.Drawing.Point(457, 0);
            this.AllWriteButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.AllWriteButton.Name = "AllWriteButton";
            this.AllWriteButton.Size = new System.Drawing.Size(363, 20);
            this.AllWriteButton.TabIndex = 3;
            this.AllWriteButton.Text = "ポインタを書き込む";
            this.AllWriteButton.UseVisualStyleBackColor = true;
            this.AllWriteButton.Click += new System.EventHandler(this.WriteButton_Click);
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.AllWriteButton);
            this.panel1.Location = new System.Drawing.Point(203, 5);
            this.panel1.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(823, 23);
            this.panel1.TabIndex = 1;
            // 
            // tabPage2
            // 
            this.tabPage2.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage2.Controls.Add(this.DecreaseColorTSAToolForWorldmapEventButton);
            this.tabPage2.Controls.Add(this.WMEvent_Import);
            this.tabPage2.Controls.Add(this.WMEvent_ExportButton);
            this.tabPage2.Controls.Add(this.WMEvent_Picture);
            this.tabPage2.Controls.Add(this.WMEvent_HEADERTSA);
            this.tabPage2.Controls.Add(this.label5);
            this.tabPage2.Controls.Add(this.WMEvent_PALETTE);
            this.tabPage2.Controls.Add(this.label7);
            this.tabPage2.Controls.Add(this.WMEvent_ZIMAGE);
            this.tabPage2.Controls.Add(this.label8);
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.tabPage2.Size = new System.Drawing.Size(1012, 549);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "イベント用";
            // 
            // DecreaseColorTSAToolForWorldmapEventButton
            // 
            this.DecreaseColorTSAToolForWorldmapEventButton.Location = new System.Drawing.Point(41, 203);
            this.DecreaseColorTSAToolForWorldmapEventButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.DecreaseColorTSAToolForWorldmapEventButton.Name = "DecreaseColorTSAToolForWorldmapEventButton";
            this.DecreaseColorTSAToolForWorldmapEventButton.Size = new System.Drawing.Size(137, 20);
            this.DecreaseColorTSAToolForWorldmapEventButton.TabIndex = 101;
            this.DecreaseColorTSAToolForWorldmapEventButton.Text = "減色ツール";
            this.DecreaseColorTSAToolForWorldmapEventButton.UseVisualStyleBackColor = true;
            this.DecreaseColorTSAToolForWorldmapEventButton.Click += new System.EventHandler(this.DecreaseColorTSAToolForWorldmapEventButton_Click);
            // 
            // WMEvent_Import
            // 
            this.WMEvent_Import.Location = new System.Drawing.Point(41, 107);
            this.WMEvent_Import.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.WMEvent_Import.Name = "WMEvent_Import";
            this.WMEvent_Import.Size = new System.Drawing.Size(137, 20);
            this.WMEvent_Import.TabIndex = 99;
            this.WMEvent_Import.Text = "画像読込";
            this.WMEvent_Import.UseVisualStyleBackColor = true;
            // 
            // WMEvent_ExportButton
            // 
            this.WMEvent_ExportButton.Location = new System.Drawing.Point(41, 136);
            this.WMEvent_ExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.WMEvent_ExportButton.Name = "WMEvent_ExportButton";
            this.WMEvent_ExportButton.Size = new System.Drawing.Size(137, 20);
            this.WMEvent_ExportButton.TabIndex = 98;
            this.WMEvent_ExportButton.Text = "画像取出";
            this.WMEvent_ExportButton.UseVisualStyleBackColor = true;
            // 
            // WMEvent_Picture
            // 
            this.WMEvent_Picture.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.WMEvent_Picture.Location = new System.Drawing.Point(239, 7);
            this.WMEvent_Picture.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMEvent_Picture.Name = "WMEvent_Picture";
            this.WMEvent_Picture.Size = new System.Drawing.Size(379, 345);
            this.WMEvent_Picture.TabIndex = 97;
            this.WMEvent_Picture.TabStop = false;
            // 
            // WMEvent_HEADERTSA
            // 
            this.WMEvent_HEADERTSA.Hexadecimal = true;
            this.WMEvent_HEADERTSA.Location = new System.Drawing.Point(105, 49);
            this.WMEvent_HEADERTSA.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMEvent_HEADERTSA.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.WMEvent_HEADERTSA.Name = "WMEvent_HEADERTSA";
            this.WMEvent_HEADERTSA.Size = new System.Drawing.Size(80, 20);
            this.WMEvent_HEADERTSA.TabIndex = 96;
            // 
            // label5
            // 
            this.label5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label5.Location = new System.Drawing.Point(3, 48);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(99, 21);
            this.label5.TabIndex = 95;
            this.label5.Text = "TSA";
            this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WMEvent_PALETTE
            // 
            this.WMEvent_PALETTE.Hexadecimal = true;
            this.WMEvent_PALETTE.Location = new System.Drawing.Point(105, 29);
            this.WMEvent_PALETTE.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMEvent_PALETTE.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.WMEvent_PALETTE.Name = "WMEvent_PALETTE";
            this.WMEvent_PALETTE.Size = new System.Drawing.Size(80, 20);
            this.WMEvent_PALETTE.TabIndex = 92;
            // 
            // label7
            // 
            this.label7.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label7.Location = new System.Drawing.Point(3, 27);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(99, 21);
            this.label7.TabIndex = 91;
            this.label7.Text = "パレット";
            this.label7.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WMEvent_ZIMAGE
            // 
            this.WMEvent_ZIMAGE.Hexadecimal = true;
            this.WMEvent_ZIMAGE.Location = new System.Drawing.Point(105, 8);
            this.WMEvent_ZIMAGE.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMEvent_ZIMAGE.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.WMEvent_ZIMAGE.Name = "WMEvent_ZIMAGE";
            this.WMEvent_ZIMAGE.Size = new System.Drawing.Size(80, 20);
            this.WMEvent_ZIMAGE.TabIndex = 90;
            // 
            // label8
            // 
            this.label8.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label8.Location = new System.Drawing.Point(3, 7);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(99, 21);
            this.label8.TabIndex = 89;
            this.label8.Text = "画像";
            this.label8.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // tabPage1
            // 
            this.tabPage1.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage1.Controls.Add(this.SelectSourceButton);
            this.tabPage1.Controls.Add(this.OpenSourceButton);
            this.tabPage1.Controls.Add(this.DecreaseColorTSAToolButton);
            this.tabPage1.Controls.Add(this.ImportButton);
            this.tabPage1.Controls.Add(this.ExportButton);
            this.tabPage1.Controls.Add(this.WMPictureBox);
            this.tabPage1.Controls.Add(this.WMtsaMap);
            this.tabPage1.Controls.Add(this.label4);
            this.tabPage1.Controls.Add(this.WMPalette);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.WMImageMap);
            this.tabPage1.Controls.Add(this.label2);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.tabPage1.Size = new System.Drawing.Size(1012, 549);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "メインフィールドマップ";
            // 
            // DecreaseColorTSAToolButton
            // 
            this.DecreaseColorTSAToolButton.Location = new System.Drawing.Point(7, 355);
            this.DecreaseColorTSAToolButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.DecreaseColorTSAToolButton.Name = "DecreaseColorTSAToolButton";
            this.DecreaseColorTSAToolButton.Size = new System.Drawing.Size(171, 20);
            this.DecreaseColorTSAToolButton.TabIndex = 93;
            this.DecreaseColorTSAToolButton.Text = "減色ツール";
            this.DecreaseColorTSAToolButton.UseVisualStyleBackColor = true;
            this.DecreaseColorTSAToolButton.Click += new System.EventHandler(this.DecreaseColorTSAToolButton_Click);
            // 
            // ImportButton
            // 
            this.ImportButton.Location = new System.Drawing.Point(7, 131);
            this.ImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ImportButton.Name = "ImportButton";
            this.ImportButton.Size = new System.Drawing.Size(171, 20);
            this.ImportButton.TabIndex = 92;
            this.ImportButton.Text = "画像読込";
            this.ImportButton.UseVisualStyleBackColor = true;
            this.ImportButton.Click += new System.EventHandler(this.ImportButton_Click);
            // 
            // ExportButton
            // 
            this.ExportButton.Location = new System.Drawing.Point(7, 161);
            this.ExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ExportButton.Name = "ExportButton";
            this.ExportButton.Size = new System.Drawing.Size(171, 20);
            this.ExportButton.TabIndex = 91;
            this.ExportButton.Text = "画像取出";
            this.ExportButton.UseVisualStyleBackColor = true;
            this.ExportButton.Click += new System.EventHandler(this.ExportButton_Click);
            // 
            // WMPictureBox
            // 
            this.WMPictureBox.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.WMPictureBox.Location = new System.Drawing.Point(191, 0);
            this.WMPictureBox.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMPictureBox.Name = "WMPictureBox";
            this.WMPictureBox.Size = new System.Drawing.Size(819, 551);
            this.WMPictureBox.TabIndex = 88;
            this.WMPictureBox.TabStop = false;
            // 
            // WMtsaMap
            // 
            this.WMtsaMap.Hexadecimal = true;
            this.WMtsaMap.Location = new System.Drawing.Point(108, 55);
            this.WMtsaMap.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMtsaMap.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.WMtsaMap.Name = "WMtsaMap";
            this.WMtsaMap.Size = new System.Drawing.Size(80, 20);
            this.WMtsaMap.TabIndex = 87;
            // 
            // label4
            // 
            this.label4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label4.Location = new System.Drawing.Point(5, 55);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(99, 21);
            this.label4.TabIndex = 86;
            this.label4.Text = "12分割TSA";
            this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WMPalette
            // 
            this.WMPalette.Hexadecimal = true;
            this.WMPalette.Location = new System.Drawing.Point(108, 35);
            this.WMPalette.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMPalette.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.WMPalette.Name = "WMPalette";
            this.WMPalette.Size = new System.Drawing.Size(80, 20);
            this.WMPalette.TabIndex = 83;
            // 
            // label1
            // 
            this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label1.Location = new System.Drawing.Point(5, 35);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(99, 21);
            this.label1.TabIndex = 82;
            this.label1.Text = "パレット";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WMImageMap
            // 
            this.WMImageMap.Hexadecimal = true;
            this.WMImageMap.Location = new System.Drawing.Point(108, 13);
            this.WMImageMap.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMImageMap.Maximum = new decimal(new int[] {
            -559939585,
            902409669,
            54,
            0});
            this.WMImageMap.Name = "WMImageMap";
            this.WMImageMap.Size = new System.Drawing.Size(80, 20);
            this.WMImageMap.TabIndex = 81;
            // 
            // label2
            // 
            this.label2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label2.Location = new System.Drawing.Point(5, 13);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(99, 21);
            this.label2.TabIndex = 77;
            this.label2.Text = "12分割画像";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // WMTabControl
            // 
            this.WMTabControl.Controls.Add(this.tabPage1);
            this.WMTabControl.Controls.Add(this.tabPage2);
            this.WMTabControl.Location = new System.Drawing.Point(8, 7);
            this.WMTabControl.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.WMTabControl.Name = "WMTabControl";
            this.WMTabControl.SelectedIndex = 0;
            this.WMTabControl.Size = new System.Drawing.Size(1020, 575);
            this.WMTabControl.TabIndex = 0;
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(7, 193);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(170, 22);
            this.OpenSourceButton.TabIndex = 94;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(8, 225);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(168, 21);
            this.SelectSourceButton.TabIndex = 95;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // WorldMapImageFE7Form
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(1031, 585);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.WMTabControl);
            this.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.Name = "WorldMapImageFE7Form";
            this.Text = "ワールドマップ画像";
            this.Load += new System.EventHandler(this.WorldMapImageFE7Form_Load);
            this.panel1.ResumeLayout(false);
            this.tabPage2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_Picture)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_HEADERTSA)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_PALETTE)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMEvent_ZIMAGE)).EndInit();
            this.tabPage1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.WMPictureBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMtsaMap)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMPalette)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.WMImageMap)).EndInit();
            this.WMTabControl.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button AllWriteButton;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.Button WMEvent_Import;
        private System.Windows.Forms.Button WMEvent_ExportButton;
        private System.Windows.Forms.NumericUpDown WMEvent_HEADERTSA;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.NumericUpDown WMEvent_PALETTE;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.NumericUpDown WMEvent_ZIMAGE;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.Button ImportButton;
        private System.Windows.Forms.Button ExportButton;
        private System.Windows.Forms.NumericUpDown WMtsaMap;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown WMPalette;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.NumericUpDown WMImageMap;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TabControl WMTabControl;
        private InterpolatedPictureBox WMEvent_Picture;
        private InterpolatedPictureBox WMPictureBox;
        private System.Windows.Forms.Button DecreaseColorTSAToolButton;
        private System.Windows.Forms.Button DecreaseColorTSAToolForWorldmapEventButton;
        private System.Windows.Forms.Button OpenSourceButton;
        private System.Windows.Forms.Button SelectSourceButton;
    }
}