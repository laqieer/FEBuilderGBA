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
        readonly Func<string, bool> _isGitRepo;
        readonly Func<string, string, bool> _setSubmoduleRemote;
        readonly Func<Form, string, string, string, Patch2GitResult> _runInitUpdate;
        readonly Dictionary<Control, bool> _operationEnabledStates =
            new Dictionary<Control, bool>();
        bool _operationInProgress;

        public ContentRepoSetupWizardForm()
            : this(
                ContentRepoSetupCore.IsGitAvailable(),
                GitUtil.IsGitRepo,
                GitUtil.SetSubmoduleRemote,
                ContentRepoGitWinForms.RunInitUpdate)
        {
        }

        // internal + InternalsVisibleTo(FEBuilderGBA.Tests): lets tests drive both the
        // git-available and git-unavailable (manual-instructions) layouts deterministically,
        // without depending on whatever git happens to be on the test host's PATH.
        internal ContentRepoSetupWizardForm(bool gitAvailable)
            : this(
                gitAvailable,
                GitUtil.IsGitRepo,
                GitUtil.SetSubmoduleRemote,
                ContentRepoGitWinForms.RunInitUpdate)
        {
        }

        internal ContentRepoSetupWizardForm(
            bool gitAvailable,
            Func<string, bool> isGitRepo,
            Func<string, string, bool> setSubmoduleRemote,
            Func<Form, string, string, string, Patch2GitResult> runInitUpdate = null)
        {
            InitializeComponent();
            this.Icon = Properties.Resources.icon_settings;
            _gitAvailable = gitAvailable;
            _isGitRepo = isGitRepo ?? GitUtil.IsGitRepo;
            _setSubmoduleRemote = setSubmoduleRemote ?? GitUtil.SetSubmoduleRemote;
            _runInitUpdate = runInitUpdate ?? ContentRepoGitWinForms.RunInitUpdate;
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
                // Stable, descriptor.Id-based names so tests (and any future automation) can find a
                // specific row's controls without depending on grid position.
                var name = new Label { Name = descriptor.Id + "_Name", Text = R._(descriptor.DisplayName), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
                var url = new TextBox { Name = descriptor.Id + "_Url", Text = ContentRepoSetupCore.ResolveUrl(descriptor, Program.Config).Trim(), Dock = DockStyle.Fill, Anchor = AnchorStyles.Left | AnchorStyles.Right };
                var status = new Label { Name = descriptor.Id + "_Status", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                var button = new Button { Name = descriptor.Id + "_Init", Text = R._("Initialize / Update"), Dock = DockStyle.Fill, Tag = descriptor, Visible = _gitAvailable };
                button.Click += InitUpdateButton_Click;

                RowsPanel.Controls.Add(name, 0, rowIndex);
                RowsPanel.Controls.Add(url, 1, rowIndex);
                RowsPanel.Controls.Add(status, 2, rowIndex);
                RowsPanel.Controls.Add(button, 3, rowIndex);

                var row = new RowControls(url, status, button, url.Text);
                _rows[descriptor.Id] = row;
                // Manual-instructions text mirrors whatever is currently typed (unsaved or not), so a
                // user relying on the manual path (Git unavailable) always sees the URL they're about
                // to use, not a stale saved value.
                url.TextChanged += (s, e) => ManualInstructionsTextBox.Text = BuildManualInstructions();
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
            if (_operationInProgress)
                return;
            if (sender is not Button button || button.Tag is not ContentRepoDescriptor descriptor)
                return;
            if (!_rows.TryGetValue(descriptor.Id, out RowControls row))
                return;

            SetOperationInProgress(true);
            try
            {
                // Persist this row (the same routine OnFormClosing uses) BEFORE running the repo
                // action. Keep every step inside this try so operation controls always restore.
                row.UrlTextBox.Text = (row.UrlTextBox.Text ?? "").Trim();
                if (PersistRow(descriptor, applyRemote: true))
                    Program.Config.Save();

                string url = row.UrlTextBox.Text;
                string effectiveUrl = string.IsNullOrWhiteSpace(url)
                    ? descriptor.DefaultUrl
                    : url;
                string repoDir = ContentRepoSetupCore.ResolveDir(
                    descriptor,
                    Program.BaseDirectory);
                _runInitUpdate(this, repoDir, effectiveUrl, descriptor.DisplayName);
                if (!IsDisposed && !Disposing)
                    UpdateStatus(descriptor);
            }
            finally
            {
                SetOperationInProgress(false);
            }
        }

        internal void SetOperationInProgress(bool inProgress)
        {
            if (_operationInProgress == inProgress)
                return;

            _operationInProgress = inProgress;
            if (inProgress)
            {
                _operationEnabledStates.Clear();
                foreach (var row in _rows.Values)
                    DisableForOperation(row.InitButton);
                DisableForOperation(CloseButton);
                DisableForOperation(DontShowAgainButton);
                return;
            }

            foreach (var state in _operationEnabledStates)
            {
                if (!state.Key.IsDisposed)
                    state.Key.Enabled = state.Value;
            }
            _operationEnabledStates.Clear();
        }

        void DisableForOperation(Control control)
        {
            if (control == null || control.IsDisposed)
                return;
            _operationEnabledStates[control] = control.Enabled;
            control.Enabled = false;
        }

        /// <summary>
        /// Shared row persistence (#2037). internal (InternalsVisibleTo FEBuilderGBA.Tests) so tests can
        /// drive it directly — bypassing the actual Init button (which would run a real
        /// <see cref="ContentRepoGitWinForms.RunInitUpdate"/> git operation / message-pump loop) — while
        /// exercising the exact same code path OnFormClosing and InitUpdateButton_Click use.
        /// Compares the row's current trimmed textbox value against its trimmed displayed baseline:
        /// identical is a TRUE no-op — no config key is created/touched and no git command runs, so an
        /// untouched row never pins a floating default into the saved config. A changed row persists the
        /// trimmed raw value (an empty string is valid and preserves the floating default on the next
        /// run), and — only when requested, git is available, and the row's target directory is already an
        /// initialized git repo — also updates that repo's origin remote so a later manual
        /// `git fetch`/`git pull` honors the new URL too. The row's baseline/raw are refreshed immediately
        /// after persisting, so a later call (e.g. from OnFormClosing after an earlier
        /// InitUpdateButton_Click already saved a different value) compares against what was just saved,
        /// not the value the row started with — so an A→B (persisted) →A (edited back, then closed) flow
        /// correctly re-persists A on close instead of treating A as "already saved".
        /// Returns true if the row's config key was written (i.e. a Save() is warranted).
        /// </summary>
        internal bool PersistRow(ContentRepoDescriptor descriptor, bool applyRemote)
        {
            if (descriptor == null || !_rows.TryGetValue(descriptor.Id, out RowControls row))
                return false;

            string current = (row.UrlTextBox.Text ?? "").Trim();
            row.UrlTextBox.Text = current;
            if (current == row.DisplayedBaselineTrimmed)
                return false;

            Program.Config[descriptor.ConfigKey] = current;

            if (applyRemote && _gitAvailable)
            {
                string repoDir = ContentRepoSetupCore.ResolveDir(descriptor, Program.BaseDirectory);
                try
                {
                    if (_isGitRepo(repoDir))
                    {
                        string effectiveUrl = string.IsNullOrWhiteSpace(current)
                            ? descriptor.DefaultUrl
                            : current;
                        _setSubmoduleRemote(repoDir, effectiveUrl);
                    }
                }
                catch (Exception)
                {
                    // A remote-apply failure (e.g. permissions or git process startup) must never cancel
                    // form close or abort the init action — the config key itself is already persisted
                    // above, which is the durable/authoritative part of this operation.
                }
            }

            row.DisplayedBaselineTrimmed = current;
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_operationInProgress && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }

            // Covers every way this form closes (Close button, titlebar X, Alt+F4, and Don't-show-again,
            // which also calls Close()) with one persistence pass. Naturally idempotent: if a row was
            // already saved by InitUpdateButton_Click, PersistRow compares against the refreshed baseline
            // and finds no further change, so a second pass here (or a hypothetical repeat call) is a
            // true no-op for that row.
            bool anyChanged = false;
            foreach (var descriptor in ContentRepoSetupCore.Repos)
            {
                if (PersistRow(descriptor, applyRemote: !_operationInProgress))
                    anyChanged = true;
            }
            if (anyChanged)
                Program.Config.Save();

            base.OnFormClosing(e);
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
                // Reflects whatever is currently typed in the row (even if unsaved), not the last-saved
                // config value, so editing a URL and switching to the manual-instructions panel (Git
                // unavailable) never shows a stale target.
                string url = descriptor.DefaultUrl;
                if (_rows.TryGetValue(descriptor.Id, out RowControls row))
                {
                    string current = (row.UrlTextBox.Text ?? "").Trim();
                    url = string.IsNullOrEmpty(current) ? descriptor.DefaultUrl : current;
                }
                sb.Append("- ").Append(R._(descriptor.DisplayName)).Append(": ")
                    .Append(url).Append(" -> ")
                    .AppendLine(ContentRepoSetupCore.ResolveDir(descriptor, Program.BaseDirectory));
            }
            return sb.ToString().TrimEnd();
        }

        void DontShowAgainButton_Click(object sender, EventArgs e)
        {
            if (_operationInProgress)
                return;
            ContentRepoSetupCore.SetOptOut(Program.Config);
            DialogResult = DialogResult.OK;
            Close();
        }

        void CloseButton_Click(object sender, EventArgs e)
        {
            if (_operationInProgress)
                return;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        sealed class RowControls
        {
            public RowControls(
                TextBox urlTextBox,
                Label statusLabel,
                Button initButton,
                string displayedBaselineTrimmed)
            {
                UrlTextBox = urlTextBox;
                StatusLabel = statusLabel;
                InitButton = initButton;
                DisplayedBaselineTrimmed = displayedBaselineTrimmed;
            }

            public TextBox UrlTextBox { get; }
            public Label StatusLabel { get; }
            public Button InitButton { get; }

            // Mutable: refreshed by PersistRow every time this row's config key is actually written, so
            // later comparisons (from either InitUpdateButton_Click or OnFormClosing) are always against
            // the most recently saved state, not the value the row started with.
            public string DisplayedBaselineTrimmed { get; set; }
        }
    }
}
