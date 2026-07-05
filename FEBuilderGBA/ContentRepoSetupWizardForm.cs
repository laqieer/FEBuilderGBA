using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    public partial class ContentRepoSetupWizardForm : Form
    {
        readonly Dictionary<string, RowControls> _rows = new Dictionary<string, RowControls>();
        readonly bool _gitAvailable;

        public ContentRepoSetupWizardForm()
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon_settings;
            _gitAvailable = ContentRepoSetupCore.IsGitAvailable();
            this.Text = R._("Content Repository Setup");
            HeaderLabel.Text = R._("Content Repository Setup");
            IntroLabel.Text = R._("FEBuilderGBA uses separate content repositories for patches and community assets. Configure the remote URL for each repository, then initialize any repository that is not ready.");
            DontShowAgainButton.Text = R._("Don't show this again");
            CloseButton.Text = R._("Close");
            ManualHeaderLabel.Text = R._("Git was not found. Initialize buttons are hidden; download and extract these repositories manually:");
            BuildRows();
            ManualPanel.Visible = !_gitAvailable;
        }

        void BuildRows()
        {
            RowsPanel.SuspendLayout();
            RowsPanel.RowCount = ContentRepoSetupCore.Repos.Count + 1;
            RowsPanel.RowStyles.Clear();
            RowsPanel.Controls.Clear();
            _rows.Clear();

            RowsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            AddHeader(R._("Repository"), 0, 0);
            AddHeader(R._("Remote URL"), 1, 0);
            AddHeader(R._("Status"), 2, 0);
            AddHeader(R._("Action"), 3, 0);

            int rowIndex = 1;
            foreach (var descriptor in ContentRepoSetupCore.Repos)
            {
                RowsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
                var name = new Label { Text = descriptor.DisplayName, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
                var url = new TextBox { Text = ContentRepoSetupCore.ResolveUrl(descriptor, Program.Config), Dock = DockStyle.Fill, Anchor = AnchorStyles.Left | AnchorStyles.Right };
                var status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                var button = new Button { Text = R._("Initialize / Update"), Dock = DockStyle.Fill, Tag = descriptor, Visible = _gitAvailable };
                button.Click += InitUpdateButton_Click;

                RowsPanel.Controls.Add(name, 0, rowIndex);
                RowsPanel.Controls.Add(url, 1, rowIndex);
                RowsPanel.Controls.Add(status, 2, rowIndex);
                RowsPanel.Controls.Add(button, 3, rowIndex);
                _rows[descriptor.Id] = new RowControls(url, status, button);
                UpdateStatus(descriptor);
                rowIndex++;
            }

            ManualInstructionsTextBox.Text = BuildManualInstructions();
            RowsPanel.ResumeLayout();
        }

        void AddHeader(string text, int column, int row)
        {
            RowsPanel.Controls.Add(new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) }, column, row);
        }

        void InitUpdateButton_Click(object sender, EventArgs e)
        {
            if (sender is not Button button || button.Tag is not ContentRepoDescriptor descriptor)
                return;
            if (!_rows.TryGetValue(descriptor.Id, out RowControls controls))
                return;

            string url = (controls.UrlTextBox.Text ?? "").Trim();
            controls.UrlTextBox.Text = url;
            Program.Config[descriptor.ConfigKey] = url;
            Program.Config.Save();

            string effectiveUrl = string.IsNullOrWhiteSpace(url) ? descriptor.DefaultUrl : url;
            string repoDir = ContentRepoSetupCore.ResolveDir(descriptor, Program.BaseDirectory);
            button.Enabled = false;
            try
            {
                ContentRepoGitWinForms.RunInitUpdate(this, repoDir, effectiveUrl, descriptor.DisplayName);
                UpdateStatus(descriptor);
            }
            finally
            {
                button.Enabled = true;
            }
        }

        void UpdateStatus(ContentRepoDescriptor descriptor)
        {
            if (!_rows.TryGetValue(descriptor.Id, out RowControls controls))
                return;
            bool ready = ContentRepoSetupCore.IsRepoReady(descriptor, Program.BaseDirectory);
            controls.StatusLabel.Text = ready ? R._("Ready") : R._("Needs initialization");
        }

        string BuildManualInstructions()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(R._("Download each repository ZIP, extract it, and place the extracted contents in the matching folder:"));
            foreach (var descriptor in ContentRepoSetupCore.Repos)
            {
                sb.Append("- ").Append(descriptor.DisplayName).Append(": ")
                    .Append(ContentRepoSetupCore.ResolveUrl(descriptor, Program.Config)).Append(" -> ")
                    .AppendLine(ContentRepoSetupCore.ResolveDir(descriptor, Program.BaseDirectory));
            }
            return sb.ToString().TrimEnd();
        }

        void DontShowAgainButton_Click(object sender, EventArgs e)
        {
            ContentRepoSetupCore.SetOptOut(Program.Config);
            DialogResult = DialogResult.OK;
            Close();
        }

        void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        sealed class RowControls
        {
            public RowControls(TextBox urlTextBox, Label statusLabel, Button initButton)
            {
                UrlTextBox = urlTextBox;
                StatusLabel = statusLabel;
                InitButton = initButton;
            }

            public TextBox UrlTextBox { get; }
            public Label StatusLabel { get; }
            public Button InitButton { get; }
        }
    }
}
