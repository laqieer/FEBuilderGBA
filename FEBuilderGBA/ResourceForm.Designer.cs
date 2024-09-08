namespace FEBuilderGBA
{
    partial class ResourceForm
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
            this.comboBoxEx1 = new FEBuilderGBA.ComboBoxEx();
            this.labelEx2 = new FEBuilderGBA.LabelEx();
            this.resources = new FEBuilderGBA.TextBoxEx();
            this.listBoxEx2 = new FEBuilderGBA.ListBoxEx();
            this.labelEx1 = new FEBuilderGBA.LabelEx();
            this.listBoxEx1 = new FEBuilderGBA.ListBoxEx();
            this.ClipbordButton = new System.Windows.Forms.Button();
            this.SaveButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // comboBoxEx1
            // 
            this.comboBoxEx1.FormattingEnabled = true;
            this.comboBoxEx1.Items.AddRange(new object[] {
            ""});
            this.comboBoxEx1.Location = new System.Drawing.Point(12, 119);
            this.comboBoxEx1.Name = "comboBoxEx1";
            this.comboBoxEx1.Size = new System.Drawing.Size(114, 21);
            this.comboBoxEx1.TabIndex = 5;
            this.comboBoxEx1.SelectedIndexChanged += new System.EventHandler(this.comboBoxEx1_SelectedIndexChanged);
            // 
            // labelEx2
            // 
            this.labelEx2.AutoSize = true;
            this.labelEx2.ErrorMessage = "";
            this.labelEx2.Location = new System.Drawing.Point(12, 103);
            this.labelEx2.Name = "labelEx2";
            this.labelEx2.Size = new System.Drawing.Size(50, 13);
            this.labelEx2.TabIndex = 4;
            this.labelEx2.Text = "フィルター";
            // 
            // resources
            // 
            this.resources.ErrorMessage = "";
            this.resources.Location = new System.Drawing.Point(137, 6);
            this.resources.Multiline = true;
            this.resources.Name = "resources";
            this.resources.Placeholder = "";
            this.resources.ReadOnly = true;
            this.resources.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.resources.Size = new System.Drawing.Size(628, 418);
            this.resources.TabIndex = 3;
            this.resources.WordWrap = false;
            // 
            // listBoxEx2
            // 
            this.listBoxEx2.FormattingEnabled = true;
            this.listBoxEx2.IntegralHeight = false;
            this.listBoxEx2.Items.AddRange(new object[] {
            "昇順",
            "降順"});
            this.listBoxEx2.Location = new System.Drawing.Point(12, 63);
            this.listBoxEx2.Name = "listBoxEx2";
            this.listBoxEx2.Size = new System.Drawing.Size(76, 32);
            this.listBoxEx2.TabIndex = 2;
            this.listBoxEx2.SelectedIndexChanged += new System.EventHandler(this.listBoxEx2_SelectedIndexChanged);
            // 
            // labelEx1
            // 
            this.labelEx1.AutoSize = true;
            this.labelEx1.ErrorMessage = "";
            this.labelEx1.Location = new System.Drawing.Point(12, 9);
            this.labelEx1.Name = "labelEx1";
            this.labelEx1.Size = new System.Drawing.Size(34, 13);
            this.labelEx1.TabIndex = 1;
            this.labelEx1.Text = "ソート";
            // 
            // listBoxEx1
            // 
            this.listBoxEx1.FormattingEnabled = true;
            this.listBoxEx1.IntegralHeight = false;
            this.listBoxEx1.Items.AddRange(new object[] {
            "日付順",
            "種類順"});
            this.listBoxEx1.Location = new System.Drawing.Point(12, 25);
            this.listBoxEx1.Name = "listBoxEx1";
            this.listBoxEx1.Size = new System.Drawing.Size(65, 32);
            this.listBoxEx1.TabIndex = 0;
            this.listBoxEx1.SelectedIndexChanged += new System.EventHandler(this.listBoxEx1_SelectedIndexChanged);
            // 
            // ClipbordButton
            // 
            this.ClipbordButton.Location = new System.Drawing.Point(12, 364);
            this.ClipbordButton.Name = "ClipbordButton";
            this.ClipbordButton.Size = new System.Drawing.Size(119, 25);
            this.ClipbordButton.TabIndex = 6;
            this.ClipbordButton.Text = "クリップボードへ";
            this.ClipbordButton.UseVisualStyleBackColor = true;
            this.ClipbordButton.Click += new System.EventHandler(this.ClipbordButton_Click);
            // 
            // SaveButton
            // 
            this.SaveButton.Location = new System.Drawing.Point(12, 398);
            this.SaveButton.Name = "SaveButton";
            this.SaveButton.Size = new System.Drawing.Size(119, 26);
            this.SaveButton.TabIndex = 7;
            this.SaveButton.Text = "ファイルに保存";
            this.SaveButton.UseVisualStyleBackColor = true;
            this.SaveButton.Click += new System.EventHandler(this.SaveButton_Click);
            // 
            // ResourceForm
            // 
            this.ClientSize = new System.Drawing.Size(777, 436);
            this.Controls.Add(this.SaveButton);
            this.Controls.Add(this.ClipbordButton);
            this.Controls.Add(this.comboBoxEx1);
            this.Controls.Add(this.labelEx2);
            this.Controls.Add(this.resources);
            this.Controls.Add(this.listBoxEx2);
            this.Controls.Add(this.labelEx1);
            this.Controls.Add(this.listBoxEx1);
            this.Name = "ResourceForm";
            this.Text = "リソース";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private ListBoxEx listBoxEx1;
        private LabelEx labelEx1;
        private ListBoxEx listBoxEx2;
        private TextBoxEx resources;
        private LabelEx labelEx2;
        private ComboBoxEx comboBoxEx1;
        private System.Windows.Forms.Button ClipbordButton;
        private System.Windows.Forms.Button SaveButton;
    }
}
