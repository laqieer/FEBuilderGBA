namespace FEBuilderGBA
{
    partial class ContentRepoSetupWizardForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TableLayoutPanel RootPanel;
        private System.Windows.Forms.Label HeaderLabel;
        private System.Windows.Forms.Label IntroLabel;
        private System.Windows.Forms.TableLayoutPanel RowsPanel;
        private System.Windows.Forms.Panel ManualPanel;
        private System.Windows.Forms.Label ManualHeaderLabel;
        private System.Windows.Forms.TextBox ManualInstructionsTextBox;
        private System.Windows.Forms.FlowLayoutPanel ButtonPanel;
        private System.Windows.Forms.Button DontShowAgainButton;
        private System.Windows.Forms.Button CloseButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.RootPanel = new System.Windows.Forms.TableLayoutPanel();
            this.HeaderLabel = new System.Windows.Forms.Label();
            this.IntroLabel = new System.Windows.Forms.Label();
            this.RowsPanel = new System.Windows.Forms.TableLayoutPanel();
            this.ManualPanel = new System.Windows.Forms.Panel();
            this.ManualHeaderLabel = new System.Windows.Forms.Label();
            this.ManualInstructionsTextBox = new System.Windows.Forms.TextBox();
            this.ButtonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.DontShowAgainButton = new System.Windows.Forms.Button();
            this.CloseButton = new System.Windows.Forms.Button();
            this.RootPanel.SuspendLayout();
            this.ManualPanel.SuspendLayout();
            this.ButtonPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // RootPanel
            // 
            this.RootPanel.ColumnCount = 1;
            this.RootPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.RootPanel.Controls.Add(this.HeaderLabel, 0, 0);
            this.RootPanel.Controls.Add(this.IntroLabel, 0, 1);
            this.RootPanel.Controls.Add(this.RowsPanel, 0, 2);
            this.RootPanel.Controls.Add(this.ManualPanel, 0, 3);
            this.RootPanel.Controls.Add(this.ButtonPanel, 0, 4);
            this.RootPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RootPanel.Location = new System.Drawing.Point(12, 12);
            this.RootPanel.Name = "RootPanel";
            this.RootPanel.RowCount = 5;
            this.RootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 36F));
            this.RootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 58F));
            this.RootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 205F));
            this.RootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.RootPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 44F));
            this.RootPanel.Size = new System.Drawing.Size(840, 477);
            this.RootPanel.TabIndex = 0;
            // 
            // HeaderLabel
            // 
            this.HeaderLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.HeaderLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 14F, System.Drawing.FontStyle.Bold);
            this.HeaderLabel.Location = new System.Drawing.Point(3, 0);
            this.HeaderLabel.Name = "HeaderLabel";
            this.HeaderLabel.Size = new System.Drawing.Size(834, 36);
            this.HeaderLabel.TabIndex = 0;
            this.HeaderLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // IntroLabel
            // 
            this.IntroLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.IntroLabel.Location = new System.Drawing.Point(3, 36);
            this.IntroLabel.Name = "IntroLabel";
            this.IntroLabel.Size = new System.Drawing.Size(834, 58);
            this.IntroLabel.TabIndex = 1;
            // 
            // RowsPanel
            // 
            this.RowsPanel.ColumnCount = 4;
            this.RowsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 130F));
            this.RowsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.RowsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.RowsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 140F));
            this.RowsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.RowsPanel.Location = new System.Drawing.Point(3, 97);
            this.RowsPanel.Name = "RowsPanel";
            this.RowsPanel.RowCount = 1;
            this.RowsPanel.Size = new System.Drawing.Size(834, 199);
            this.RowsPanel.TabIndex = 2;
            // 
            // ManualPanel
            // 
            this.ManualPanel.Controls.Add(this.ManualInstructionsTextBox);
            this.ManualPanel.Controls.Add(this.ManualHeaderLabel);
            this.ManualPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ManualPanel.Location = new System.Drawing.Point(3, 302);
            this.ManualPanel.Name = "ManualPanel";
            this.ManualPanel.Size = new System.Drawing.Size(834, 128);
            this.ManualPanel.TabIndex = 3;
            // 
            // ManualHeaderLabel
            // 
            this.ManualHeaderLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.ManualHeaderLabel.Location = new System.Drawing.Point(0, 0);
            this.ManualHeaderLabel.Name = "ManualHeaderLabel";
            this.ManualHeaderLabel.Size = new System.Drawing.Size(834, 28);
            this.ManualHeaderLabel.TabIndex = 0;
            // 
            // ManualInstructionsTextBox
            // 
            this.ManualInstructionsTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ManualInstructionsTextBox.Location = new System.Drawing.Point(0, 28);
            this.ManualInstructionsTextBox.Multiline = true;
            this.ManualInstructionsTextBox.Name = "ManualInstructionsTextBox";
            this.ManualInstructionsTextBox.ReadOnly = true;
            this.ManualInstructionsTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.ManualInstructionsTextBox.Size = new System.Drawing.Size(834, 100);
            this.ManualInstructionsTextBox.TabIndex = 1;
            // 
            // ButtonPanel
            // 
            this.ButtonPanel.Controls.Add(this.CloseButton);
            this.ButtonPanel.Controls.Add(this.DontShowAgainButton);
            this.ButtonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ButtonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.ButtonPanel.Location = new System.Drawing.Point(3, 436);
            this.ButtonPanel.Name = "ButtonPanel";
            this.ButtonPanel.Size = new System.Drawing.Size(834, 38);
            this.ButtonPanel.TabIndex = 4;
            // 
            // DontShowAgainButton
            // 
            this.DontShowAgainButton.AutoSize = true;
            this.DontShowAgainButton.Location = new System.Drawing.Point(587, 3);
            this.DontShowAgainButton.Name = "DontShowAgainButton";
            this.DontShowAgainButton.Size = new System.Drawing.Size(128, 28);
            this.DontShowAgainButton.TabIndex = 1;
            this.DontShowAgainButton.UseVisualStyleBackColor = true;
            this.DontShowAgainButton.Click += new System.EventHandler(this.DontShowAgainButton_Click);
            // 
            // CloseButton
            // 
            this.CloseButton.Location = new System.Drawing.Point(721, 3);
            this.CloseButton.Name = "CloseButton";
            this.CloseButton.Size = new System.Drawing.Size(110, 28);
            this.CloseButton.TabIndex = 0;
            this.CloseButton.UseVisualStyleBackColor = true;
            this.CloseButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // ContentRepoSetupWizardForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(864, 501);
            this.Controls.Add(this.RootPanel);
            this.Name = "ContentRepoSetupWizardForm";
            this.Padding = new System.Windows.Forms.Padding(12);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Content Repository Setup";
            this.RootPanel.ResumeLayout(false);
            this.ManualPanel.ResumeLayout(false);
            this.ManualPanel.PerformLayout();
            this.ButtonPanel.ResumeLayout(false);
            this.ButtonPanel.PerformLayout();
            this.ResumeLayout(false);
        }
    }
}
