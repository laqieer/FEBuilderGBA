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
            this.listBoxEx1 = new FEBuilderGBA.ListBoxEx();
            this.labelEx1 = new FEBuilderGBA.LabelEx();
            this.listBoxEx2 = new FEBuilderGBA.ListBoxEx();
            this.resources = new FEBuilderGBA.TextBoxEx();
            this.SuspendLayout();
            // 
            // listBoxEx1
            // 
            this.listBoxEx1.FormattingEnabled = true;
            this.listBoxEx1.IntegralHeight = false;
            this.listBoxEx1.Items.AddRange(new object[] {
            "日付順",
            "種類順"});
            this.listBoxEx1.Location = new System.Drawing.Point(15, 22);
            this.listBoxEx1.Name = "listBoxEx1";
            this.listBoxEx1.Size = new System.Drawing.Size(65, 32);
            this.listBoxEx1.TabIndex = 0;
            this.listBoxEx1.SelectedIndexChanged += new System.EventHandler(this.listBoxEx1_SelectedIndexChanged);
            // 
            // labelEx1
            // 
            this.labelEx1.AutoSize = true;
            this.labelEx1.ErrorMessage = "";
            this.labelEx1.Location = new System.Drawing.Point(75, 6);
            this.labelEx1.Name = "labelEx1";
            this.labelEx1.Size = new System.Drawing.Size(34, 13);
            this.labelEx1.TabIndex = 1;
            this.labelEx1.Text = "ソート";
            // 
            // listBoxEx2
            // 
            this.listBoxEx2.FormattingEnabled = true;
            this.listBoxEx2.IntegralHeight = false;
            this.listBoxEx2.Items.AddRange(new object[] {
            "昇順",
            "降順"});
            this.listBoxEx2.Location = new System.Drawing.Point(96, 22);
            this.listBoxEx2.Name = "listBoxEx2";
            this.listBoxEx2.Size = new System.Drawing.Size(76, 32);
            this.listBoxEx2.TabIndex = 2;
            this.listBoxEx2.SelectedIndexChanged += new System.EventHandler(this.listBoxEx2_SelectedIndexChanged);
            // 
            // resources
            // 
            this.resources.ErrorMessage = "";
            this.resources.Location = new System.Drawing.Point(10, 69);
            this.resources.Multiline = true;
            this.resources.Name = "resources";
            this.resources.Placeholder = "";
            this.resources.ReadOnly = true;
            this.resources.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.resources.Size = new System.Drawing.Size(628, 332);
            this.resources.TabIndex = 3;
            this.resources.WordWrap = false;
            // 
            // ResourceForm
            // 
            this.ClientSize = new System.Drawing.Size(650, 436);
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
    }
}
