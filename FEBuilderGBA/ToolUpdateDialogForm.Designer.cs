namespace FEBuilderGBA
{
    partial class ToolUpdateDialogForm
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
            this.Message = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.FormIcon = new System.Windows.Forms.PictureBox();
            this.IgnoreButton = new System.Windows.Forms.Button();
            this.OpenBrowserButton = new System.Windows.Forms.Button();
            this.AutoUpdateButton = new System.Windows.Forms.Button();
            this.UpdateCoreButton = new System.Windows.Forms.Button();
            this.UpdatePatch2GitButton = new System.Windows.Forms.Button();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.FormIcon)).BeginInit();
            this.SuspendLayout();
            //
            // Message
            //
            this.Message.Location = new System.Drawing.Point(181, 24);
            this.Message.Name = "Message";
            this.Message.Size = new System.Drawing.Size(677, 128);
            this.Message.TabIndex = 0;
            this.Message.Text = "最新版({0})があるようです。\r\nアップデートしますか？\r\n\r\n{1}";
            //
            // panel1
            //
            this.panel1.Controls.Add(this.FormIcon);
            this.panel1.Controls.Add(this.IgnoreButton);
            this.panel1.Controls.Add(this.OpenBrowserButton);
            this.panel1.Controls.Add(this.AutoUpdateButton);
            this.panel1.Controls.Add(this.UpdateCoreButton);
            this.panel1.Controls.Add(this.UpdatePatch2GitButton);
            this.panel1.Controls.Add(this.Message);
            this.panel1.Location = new System.Drawing.Point(13, 13);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(879, 345);
            this.panel1.TabIndex = 1;
            //
            // FormIcon
            //
            this.FormIcon.Location = new System.Drawing.Point(18, 24);
            this.FormIcon.Name = "FormIcon";
            this.FormIcon.Size = new System.Drawing.Size(128, 128);
            this.FormIcon.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.FormIcon.TabIndex = 5;
            this.FormIcon.TabStop = false;
            //
            // AutoUpdateButton  (row 1 — always visible)
            //
            this.AutoUpdateButton.Location = new System.Drawing.Point(17, 182);
            this.AutoUpdateButton.Name = "AutoUpdateButton";
            this.AutoUpdateButton.Size = new System.Drawing.Size(841, 34);
            this.AutoUpdateButton.TabIndex = 1;
            this.AutoUpdateButton.Text = "全自動でアップデートします";
            this.AutoUpdateButton.UseVisualStyleBackColor = true;
            this.AutoUpdateButton.Click += new System.EventHandler(this.AutoUpdateButton_Click);
            //
            // UpdateCoreButton  (row 2 — split-package mode only, hidden by default)
            //
            this.UpdateCoreButton.Location = new System.Drawing.Point(17, 222);
            this.UpdateCoreButton.Name = "UpdateCoreButton";
            this.UpdateCoreButton.Size = new System.Drawing.Size(841, 34);
            this.UpdateCoreButton.TabIndex = 2;
            this.UpdateCoreButton.Text = "プログラム本体を更新します";
            this.UpdateCoreButton.UseVisualStyleBackColor = true;
            this.UpdateCoreButton.Visible = false;
            this.UpdateCoreButton.Click += new System.EventHandler(this.UpdateCoreButton_Click);
            //
            // UpdatePatch2GitButton  (row 4 — shown when git is found, hidden by default)
            //
            this.UpdatePatch2GitButton.Location = new System.Drawing.Point(17, 302);
            this.UpdatePatch2GitButton.Name = "UpdatePatch2GitButton";
            this.UpdatePatch2GitButton.Size = new System.Drawing.Size(841, 34);
            this.UpdatePatch2GitButton.TabIndex = 6;
            this.UpdatePatch2GitButton.Text = "Gitでパッチデータを更新します";
            this.UpdatePatch2GitButton.UseVisualStyleBackColor = true;
            this.UpdatePatch2GitButton.Visible = false;
            this.UpdatePatch2GitButton.Click += new System.EventHandler(this.UpdatePatch2GitButton_Click);
            //
            // OpenBrowserButton  (row 5)
            //
            this.OpenBrowserButton.Location = new System.Drawing.Point(17, 234);
            this.OpenBrowserButton.Name = "OpenBrowserButton";
            this.OpenBrowserButton.Size = new System.Drawing.Size(841, 34);
            this.OpenBrowserButton.TabIndex = 4;
            this.OpenBrowserButton.Text = "ブラウザでURLを開きます";
            this.OpenBrowserButton.UseVisualStyleBackColor = true;
            this.OpenBrowserButton.Click += new System.EventHandler(this.OpenBrowserButton_Click);
            //
            // IgnoreButton  (row 5)
            //
            this.IgnoreButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.IgnoreButton.Location = new System.Drawing.Point(17, 289);
            this.IgnoreButton.Name = "IgnoreButton";
            this.IgnoreButton.Size = new System.Drawing.Size(841, 34);
            this.IgnoreButton.TabIndex = 5;
            this.IgnoreButton.Text = "アップデートしません";
            this.IgnoreButton.UseVisualStyleBackColor = true;
            this.IgnoreButton.Click += new System.EventHandler(this.IgnoreButton_Click);
            //
            // ToolUpdateDialogForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(144F, 144F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.AutoSize = true;
            this.CancelButton = this.IgnoreButton;
            this.ClientSize = new System.Drawing.Size(904, 378);
            this.Controls.Add(this.panel1);
            this.Name = "ToolUpdateDialogForm";
            this.Text = "UpdateDialog";
            this.Load += new System.EventHandler(this.UpdateDialog_Load);
            this.panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.FormIcon)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label Message;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button AutoUpdateButton;
        private System.Windows.Forms.Button UpdateCoreButton;
        private System.Windows.Forms.Button UpdatePatch2GitButton;
        private System.Windows.Forms.Button OpenBrowserButton;
        private System.Windows.Forms.Button IgnoreButton;
        private System.Windows.Forms.PictureBox FormIcon;
    }
}
