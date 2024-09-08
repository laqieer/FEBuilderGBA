namespace FEBuilderGBA
{
    partial class ImageItemIconForm
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
            this.DragTargetPanel2 = new System.Windows.Forms.Panel();
            this.LinkInternt = new System.Windows.Forms.Label();
            this.JumpToSystemPalette = new System.Windows.Forms.Label();
            this.ImportButton = new System.Windows.Forms.Button();
            this.ExportButton = new System.Windows.Forms.Button();
            this.ReloadListButton = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.ReadCount = new System.Windows.Forms.NumericUpDown();
            this.ReadStartAddress = new System.Windows.Forms.NumericUpDown();
            this.AddressPanel = new System.Windows.Forms.Panel();
            this.WriteButton = new System.Windows.Forms.Button();
            this.BlockSize = new FEBuilderGBA.TextBoxEx();
            this.SelectAddress = new FEBuilderGBA.TextBoxEx();
            this.label22 = new System.Windows.Forms.Label();
            this.Address = new System.Windows.Forms.NumericUpDown();
            this.label23 = new System.Windows.Forms.Label();
            this.DragTargetPanel = new System.Windows.Forms.Panel();
            this.Comment = new FEBuilderGBA.TextBoxEx();
            this.label6 = new System.Windows.Forms.Label();
            this.X_ICON_REF_ITEM = new FEBuilderGBA.ListBoxEx();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.X_ICON_BIG_PIC = new FEBuilderGBA.InterpolatedPictureBox();
            this.label7 = new System.Windows.Forms.Label();
            this.X_ICON_PIC = new FEBuilderGBA.InterpolatedPictureBox();
            this.panel6 = new System.Windows.Forms.Panel();
            this.ItemIconListExpandsButton = new System.Windows.Forms.Button();
            this.LabelFilter = new System.Windows.Forms.Label();
            this.AddressList = new FEBuilderGBA.ListBoxEx();
            this.OpenSourceButton = new System.Windows.Forms.Button();
            this.SelectSourceButton = new System.Windows.Forms.Button();
            this.DragTargetPanel2.SuspendLayout();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).BeginInit();
            this.AddressPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).BeginInit();
            this.DragTargetPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.X_ICON_BIG_PIC)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_ICON_PIC)).BeginInit();
            this.panel6.SuspendLayout();
            this.SuspendLayout();
            // 
            // DragTargetPanel2
            // 
            this.DragTargetPanel2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.DragTargetPanel2.Controls.Add(this.LinkInternt);
            this.DragTargetPanel2.Controls.Add(this.JumpToSystemPalette);
            this.DragTargetPanel2.Controls.Add(this.ImportButton);
            this.DragTargetPanel2.Controls.Add(this.ExportButton);
            this.DragTargetPanel2.Location = new System.Drawing.Point(191, 279);
            this.DragTargetPanel2.Name = "DragTargetPanel2";
            this.DragTargetPanel2.Size = new System.Drawing.Size(541, 41);
            this.DragTargetPanel2.TabIndex = 95;
            // 
            // LinkInternt
            // 
            this.LinkInternt.AutoSize = true;
            this.LinkInternt.Location = new System.Drawing.Point(327, 13);
            this.LinkInternt.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.LinkInternt.Name = "LinkInternt";
            this.LinkInternt.Size = new System.Drawing.Size(183, 13);
            this.LinkInternt.TabIndex = 30;
            this.LinkInternt.Text = "インターネットから新しいリソースを探す";
            this.LinkInternt.Click += new System.EventHandler(this.LinkInternt_Click);
            // 
            // JumpToSystemPalette
            // 
            this.JumpToSystemPalette.AccessibleDescription = "@EXPLAIN_ITEMICON_SYSTEM_PALETTE";
            this.JumpToSystemPalette.AutoSize = true;
            this.JumpToSystemPalette.Cursor = System.Windows.Forms.Cursors.Hand;
            this.JumpToSystemPalette.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Underline, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.JumpToSystemPalette.Location = new System.Drawing.Point(242, 13);
            this.JumpToSystemPalette.Name = "JumpToSystemPalette";
            this.JumpToSystemPalette.Size = new System.Drawing.Size(73, 12);
            this.JumpToSystemPalette.TabIndex = 201;
            this.JumpToSystemPalette.Text = "パレットの変更";
            this.JumpToSystemPalette.Click += new System.EventHandler(this.JumpToSystemPalette_Click);
            // 
            // ImportButton
            // 
            this.ImportButton.Location = new System.Drawing.Point(11, 9);
            this.ImportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ImportButton.Name = "ImportButton";
            this.ImportButton.Size = new System.Drawing.Size(107, 20);
            this.ImportButton.TabIndex = 65;
            this.ImportButton.Text = "画像読込";
            this.ImportButton.UseVisualStyleBackColor = true;
            this.ImportButton.Click += new System.EventHandler(this.ImportButton_Click);
            // 
            // ExportButton
            // 
            this.ExportButton.Location = new System.Drawing.Point(123, 9);
            this.ExportButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ExportButton.Name = "ExportButton";
            this.ExportButton.Size = new System.Drawing.Size(107, 20);
            this.ExportButton.TabIndex = 64;
            this.ExportButton.Text = "画像取出";
            this.ExportButton.UseVisualStyleBackColor = true;
            this.ExportButton.Click += new System.EventHandler(this.ExportButton_Click);
            // 
            // ReloadListButton
            // 
            this.ReloadListButton.Location = new System.Drawing.Point(304, -1);
            this.ReloadListButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReloadListButton.Name = "ReloadListButton";
            this.ReloadListButton.Size = new System.Drawing.Size(74, 21);
            this.ReloadListButton.TabIndex = 25;
            this.ReloadListButton.Text = "再取得";
            this.ReloadListButton.UseVisualStyleBackColor = true;
            // 
            // label3
            // 
            this.label3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label3.Location = new System.Drawing.Point(151, -1);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(48, 21);
            this.label3.TabIndex = 52;
            this.label3.Text = "Size:";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label1
            // 
            this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label1.Location = new System.Drawing.Point(-1, -1);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(78, 22);
            this.label1.TabIndex = 23;
            this.label1.Text = "先頭アドレス";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label2
            // 
            this.label2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label2.Location = new System.Drawing.Point(179, -1);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(57, 22);
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
            this.panel1.Location = new System.Drawing.Point(11, 11);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(719, 21);
            this.panel1.TabIndex = 94;
            // 
            // ReadCount
            // 
            this.ReadCount.Location = new System.Drawing.Point(241, 1);
            this.ReadCount.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.ReadCount.Name = "ReadCount";
            this.ReadCount.Size = new System.Drawing.Size(53, 20);
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
            this.ReadStartAddress.Size = new System.Drawing.Size(87, 20);
            this.ReadStartAddress.TabIndex = 0;
            // 
            // AddressPanel
            // 
            this.AddressPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.AddressPanel.Controls.Add(this.WriteButton);
            this.AddressPanel.Controls.Add(this.BlockSize);
            this.AddressPanel.Controls.Add(this.label3);
            this.AddressPanel.Controls.Add(this.SelectAddress);
            this.AddressPanel.Controls.Add(this.label22);
            this.AddressPanel.Controls.Add(this.Address);
            this.AddressPanel.Controls.Add(this.label23);
            this.AddressPanel.Location = new System.Drawing.Point(191, 30);
            this.AddressPanel.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.AddressPanel.Name = "AddressPanel";
            this.AddressPanel.Size = new System.Drawing.Size(539, 21);
            this.AddressPanel.TabIndex = 93;
            // 
            // WriteButton
            // 
            this.WriteButton.Location = new System.Drawing.Point(413, -1);
            this.WriteButton.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.WriteButton.Name = "WriteButton";
            this.WriteButton.Size = new System.Drawing.Size(125, 20);
            this.WriteButton.TabIndex = 53;
            this.WriteButton.Text = "書き込み";
            this.WriteButton.UseVisualStyleBackColor = true;
            // 
            // BlockSize
            // 
            this.BlockSize.ErrorMessage = "";
            this.BlockSize.Location = new System.Drawing.Point(202, 1);
            this.BlockSize.Name = "BlockSize";
            this.BlockSize.Placeholder = "";
            this.BlockSize.ReadOnly = true;
            this.BlockSize.Size = new System.Drawing.Size(49, 20);
            this.BlockSize.TabIndex = 52;
            // 
            // SelectAddress
            // 
            this.SelectAddress.ErrorMessage = "";
            this.SelectAddress.Location = new System.Drawing.Point(335, 1);
            this.SelectAddress.Name = "SelectAddress";
            this.SelectAddress.Placeholder = "";
            this.SelectAddress.ReadOnly = true;
            this.SelectAddress.Size = new System.Drawing.Size(73, 20);
            this.SelectAddress.TabIndex = 40;
            // 
            // label22
            // 
            this.label22.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label22.Location = new System.Drawing.Point(253, -1);
            this.label22.Name = "label22";
            this.label22.Size = new System.Drawing.Size(82, 21);
            this.label22.TabIndex = 39;
            this.label22.Text = "選択アドレス:";
            this.label22.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Address
            // 
            this.Address.Hexadecimal = true;
            this.Address.Location = new System.Drawing.Point(61, 1);
            this.Address.Margin = new System.Windows.Forms.Padding(3, 1, 3, 1);
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
            this.label23.Location = new System.Drawing.Point(-2, -1);
            this.label23.Name = "label23";
            this.label23.Size = new System.Drawing.Size(58, 22);
            this.label23.TabIndex = 1;
            this.label23.Text = "アドレス";
            this.label23.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // DragTargetPanel
            // 
            this.DragTargetPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.DragTargetPanel.Controls.Add(this.SelectSourceButton);
            this.DragTargetPanel.Controls.Add(this.OpenSourceButton);
            this.DragTargetPanel.Controls.Add(this.Comment);
            this.DragTargetPanel.Controls.Add(this.label6);
            this.DragTargetPanel.Controls.Add(this.X_ICON_REF_ITEM);
            this.DragTargetPanel.Controls.Add(this.label5);
            this.DragTargetPanel.Controls.Add(this.label4);
            this.DragTargetPanel.Controls.Add(this.X_ICON_BIG_PIC);
            this.DragTargetPanel.Controls.Add(this.label7);
            this.DragTargetPanel.Controls.Add(this.X_ICON_PIC);
            this.DragTargetPanel.Location = new System.Drawing.Point(191, 50);
            this.DragTargetPanel.Name = "DragTargetPanel";
            this.DragTargetPanel.Size = new System.Drawing.Size(540, 223);
            this.DragTargetPanel.TabIndex = 92;
            // 
            // Comment
            // 
            this.Comment.ErrorMessage = "";
            this.Comment.Location = new System.Drawing.Point(116, 146);
            this.Comment.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Comment.Name = "Comment";
            this.Comment.Placeholder = "";
            this.Comment.Size = new System.Drawing.Size(231, 20);
            this.Comment.TabIndex = 200;
            // 
            // label6
            // 
            this.label6.AccessibleDescription = "@COMMENT";
            this.label6.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label6.Location = new System.Drawing.Point(3, 144);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(104, 21);
            this.label6.TabIndex = 199;
            this.label6.Text = "コメント";
            this.label6.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_ICON_REF_ITEM
            // 
            this.X_ICON_REF_ITEM.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.X_ICON_REF_ITEM.FormattingEnabled = true;
            this.X_ICON_REF_ITEM.IntegralHeight = false;
            this.X_ICON_REF_ITEM.Location = new System.Drawing.Point(368, 6);
            this.X_ICON_REF_ITEM.Name = "X_ICON_REF_ITEM";
            this.X_ICON_REF_ITEM.Size = new System.Drawing.Size(172, 213);
            this.X_ICON_REF_ITEM.TabIndex = 92;
            // 
            // label5
            // 
            this.label5.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label5.Location = new System.Drawing.Point(209, 5);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(139, 21);
            this.label5.TabIndex = 71;
            this.label5.Text = "参照アイテム";
            this.label5.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label4
            // 
            this.label4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label4.Location = new System.Drawing.Point(3, 32);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(103, 21);
            this.label4.TabIndex = 70;
            this.label4.Text = "拡大";
            this.label4.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_ICON_BIG_PIC
            // 
            this.X_ICON_BIG_PIC.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_ICON_BIG_PIC.Location = new System.Drawing.Point(117, 32);
            this.X_ICON_BIG_PIC.Name = "X_ICON_BIG_PIC";
            this.X_ICON_BIG_PIC.Size = new System.Drawing.Size(53, 51);
            this.X_ICON_BIG_PIC.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.X_ICON_BIG_PIC.TabIndex = 69;
            this.X_ICON_BIG_PIC.TabStop = false;
            // 
            // label7
            // 
            this.label7.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label7.Location = new System.Drawing.Point(3, 5);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(103, 21);
            this.label7.TabIndex = 68;
            this.label7.Text = "原寸大";
            this.label7.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // X_ICON_PIC
            // 
            this.X_ICON_PIC.Interpolation = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
            this.X_ICON_PIC.Location = new System.Drawing.Point(117, 5);
            this.X_ICON_PIC.Name = "X_ICON_PIC";
            this.X_ICON_PIC.Size = new System.Drawing.Size(27, 25);
            this.X_ICON_PIC.TabIndex = 67;
            this.X_ICON_PIC.TabStop = false;
            // 
            // panel6
            // 
            this.panel6.Controls.Add(this.ItemIconListExpandsButton);
            this.panel6.Controls.Add(this.LabelFilter);
            this.panel6.Controls.Add(this.AddressList);
            this.panel6.Location = new System.Drawing.Point(11, 32);
            this.panel6.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.panel6.Name = "panel6";
            this.panel6.Size = new System.Drawing.Size(175, 287);
            this.panel6.TabIndex = 72;
            // 
            // ItemIconListExpandsButton
            // 
            this.ItemIconListExpandsButton.Location = new System.Drawing.Point(1, 267);
            this.ItemIconListExpandsButton.Margin = new System.Windows.Forms.Padding(1, 1, 1, 1);
            this.ItemIconListExpandsButton.Name = "ItemIconListExpandsButton";
            this.ItemIconListExpandsButton.Size = new System.Drawing.Size(173, 20);
            this.ItemIconListExpandsButton.TabIndex = 117;
            this.ItemIconListExpandsButton.Text = "リストの拡張";
            this.ItemIconListExpandsButton.UseVisualStyleBackColor = true;
            this.ItemIconListExpandsButton.Click += new System.EventHandler(this.ItemIconListExpandsButton_Click);
            // 
            // LabelFilter
            // 
            this.LabelFilter.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.LabelFilter.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.LabelFilter.Location = new System.Drawing.Point(1, 0);
            this.LabelFilter.Margin = new System.Windows.Forms.Padding(1, 0, 1, 0);
            this.LabelFilter.Name = "LabelFilter";
            this.LabelFilter.Size = new System.Drawing.Size(174, 18);
            this.LabelFilter.TabIndex = 55;
            this.LabelFilter.Text = "名前";
            this.LabelFilter.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // AddressList
            // 
            this.AddressList.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.AddressList.FormattingEnabled = true;
            this.AddressList.IntegralHeight = false;
            this.AddressList.Location = new System.Drawing.Point(1, 16);
            this.AddressList.Name = "AddressList";
            this.AddressList.Size = new System.Drawing.Size(175, 244);
            this.AddressList.TabIndex = 0;
            this.AddressList.SelectedIndexChanged += new System.EventHandler(this.AddressList_SelectedIndexChanged);
            // 
            // OpenSourceButton
            // 
            this.OpenSourceButton.Location = new System.Drawing.Point(3, 198);
            this.OpenSourceButton.Name = "OpenSourceButton";
            this.OpenSourceButton.Size = new System.Drawing.Size(115, 20);
            this.OpenSourceButton.TabIndex = 201;
            this.OpenSourceButton.Text = "ソースファイルを開く";
            this.OpenSourceButton.UseVisualStyleBackColor = true;
            this.OpenSourceButton.Click += new System.EventHandler(this.OpenSourceButton_Click);
            // 
            // SelectSourceButton
            // 
            this.SelectSourceButton.Location = new System.Drawing.Point(120, 198);
            this.SelectSourceButton.Name = "SelectSourceButton";
            this.SelectSourceButton.Size = new System.Drawing.Size(121, 21);
            this.SelectSourceButton.TabIndex = 202;
            this.SelectSourceButton.Text = "ソースフォルダーを開く";
            this.SelectSourceButton.UseVisualStyleBackColor = true;
            this.SelectSourceButton.Click += new System.EventHandler(this.SelectSourceButton_Click);
            // 
            // ImageItemIconForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(733, 320);
            this.Controls.Add(this.panel6);
            this.Controls.Add(this.DragTargetPanel2);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.AddressPanel);
            this.Controls.Add(this.DragTargetPanel);
            this.Name = "ImageItemIconForm";
            this.Text = "アイテムアイコン";
            this.Load += new System.EventHandler(this.ImageIconForm_Load);
            this.DragTargetPanel2.ResumeLayout(false);
            this.DragTargetPanel2.PerformLayout();
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.ReadCount)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ReadStartAddress)).EndInit();
            this.AddressPanel.ResumeLayout(false);
            this.AddressPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.Address)).EndInit();
            this.DragTargetPanel.ResumeLayout(false);
            this.DragTargetPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.X_ICON_BIG_PIC)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.X_ICON_PIC)).EndInit();
            this.panel6.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel DragTargetPanel2;
        private System.Windows.Forms.Button ReloadListButton;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.NumericUpDown ReadCount;
        private System.Windows.Forms.NumericUpDown ReadStartAddress;
        private FEBuilderGBA.TextBoxEx BlockSize;
        private System.Windows.Forms.Panel AddressPanel;
        private FEBuilderGBA.TextBoxEx SelectAddress;
        private System.Windows.Forms.Label label22;
        private System.Windows.Forms.NumericUpDown Address;
        private System.Windows.Forms.Label label23;
        private System.Windows.Forms.Panel DragTargetPanel;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label7;
        private ListBoxEx X_ICON_REF_ITEM;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Panel panel6;
        private System.Windows.Forms.Label LabelFilter;
        private ListBoxEx AddressList;
        private System.Windows.Forms.Button ImportButton;
        private System.Windows.Forms.Button ExportButton;
        private InterpolatedPictureBox X_ICON_PIC;
        private InterpolatedPictureBox X_ICON_BIG_PIC;
        private System.Windows.Forms.Button ItemIconListExpandsButton;
        private TextBoxEx Comment;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Button WriteButton;
        private System.Windows.Forms.Label JumpToSystemPalette;
        private System.Windows.Forms.Label LinkInternt;
        private System.Windows.Forms.Button SelectSourceButton;
        private System.Windows.Forms.Button OpenSourceButton;
    }
}