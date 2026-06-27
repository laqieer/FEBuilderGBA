using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// Browser form for FE-Repo-Music resources. Shows categories and .s files
    /// for song import via the song exchange system.
    /// </summary>
    public class FERepoMusicBrowserForm : Form
    {
        TreeView categoryTree;
        ListView fileListView;
        Button selectButton;
        Button cancelButton;
        Label statusLabel;
        TextBox previewBox;

        string repoRoot;

        public string SelectedFilePath { get; private set; }

        public FERepoMusicBrowserForm()
        {
            InitializeComponent();
            LoadCategories();
        }

        void InitializeComponent()
        {
            Text = R._("FE-Repo Music Browser");
            Size = new Size(800, 500);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 250,
                Orientation = Orientation.Vertical
            };

            categoryTree = new TreeView { Dock = DockStyle.Fill };
            categoryTree.AfterSelect += CategoryTree_AfterSelect;
            splitMain.Panel1.Controls.Add(categoryTree);

            var splitRight = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 250,
                Orientation = Orientation.Vertical
            };

            fileListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false
            };
            fileListView.Columns.Add(R._("Filename"), 300);
            fileListView.Columns.Add(R._("Size"), 80);
            fileListView.SelectedIndexChanged += FileListView_SelectedIndexChanged;
            fileListView.DoubleClick += (s, e) => { if (SelectedFilePath != null) { DialogResult = DialogResult.OK; Close(); } };
            splitRight.Panel1.Controls.Add(fileListView);

            previewBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 9)
            };
            splitRight.Panel2.Controls.Add(previewBox);

            splitMain.Panel2.Controls.Add(splitRight);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = R._("Select a music file") };
            selectButton = new Button { Text = R._("Select"), Dock = DockStyle.Right, Width = 80, Enabled = false };
            selectButton.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            cancelButton = new Button { Text = R._("Cancel"), Dock = DockStyle.Right, Width = 80 };
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottomPanel.Controls.Add(statusLabel);
            bottomPanel.Controls.Add(selectButton);
            bottomPanel.Controls.Add(cancelButton);

            Controls.Add(splitMain);
            Controls.Add(bottomPanel);
        }

        void LoadCategories()
        {
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            repoRoot = FERepoResourceBrowser.FindMusicRepoRoot(baseDir);
            if (repoRoot == null)
            {
                // Source clones init the submodule. Released-zip users (no git
                // repo / no submodule / no scripts/ folder) shallow-clone the
                // public repo straight into resources/ next to the exe (#1644).
                categoryTree.Nodes.Add(R._("FE-Repo-Music not found. Run: git submodule update --init resources/FE-Repo-Music-No-Preview"));
                categoryTree.Nodes.Add(R._("Released build (no git repo)? Run: ") + FERepoResourceBrowser.MusicCloneCommand);
                return;
            }

            string[] categories = FERepoResourceBrowser.GetCategories(repoRoot);
            foreach (string cat in categories)
            {
                var catNode = new TreeNode(cat) { Tag = cat };
                string[] subs = FERepoResourceBrowser.GetSubCategories(repoRoot, cat);
                foreach (string sub in subs)
                {
                    catNode.Nodes.Add(new TreeNode(sub) { Tag = sub });
                }
                categoryTree.Nodes.Add(catNode);
            }
        }

        void CategoryTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (repoRoot == null || e.Node?.Tag == null) return;

            string category, subCategory = null;
            if (e.Node.Parent != null)
            {
                category = e.Node.Parent.Tag as string;
                subCategory = e.Node.Tag as string;
            }
            else
            {
                category = e.Node.Tag as string;
            }

            fileListView.Items.Clear();
            previewBox.Text = "";
            selectButton.Enabled = false;
            SelectedFilePath = null;

            var files = FERepoResourceBrowser.GetMusicFiles(repoRoot, category, subCategory, maxResults: 500);
            statusLabel.Text = string.Format(R._("{0} music files found"), files.Length);

            foreach (var entry in files)
            {
                var item = new ListViewItem(entry.FileName) { Tag = entry.FullPath };
                item.SubItems.Add(U.ToHexString4((uint)entry.FileSize));
                fileListView.Items.Add(item);
            }
        }

        void FileListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0)
            {
                previewBox.Text = "";
                selectButton.Enabled = false;
                SelectedFilePath = null;
                return;
            }

            string path = fileListView.SelectedItems[0].Tag as string;
            SelectedFilePath = path;
            selectButton.Enabled = true;

            try
            {
                // Show first 50 lines of .s file as preview
                string[] lines = File.ReadAllLines(path);
                int previewLines = Math.Min(lines.Length, 50);
                previewBox.Text = string.Join(Environment.NewLine, lines, 0, previewLines);
                if (lines.Length > previewLines)
                    previewBox.Text += Environment.NewLine + $"... ({lines.Length - previewLines} more lines)";

                var info = new FileInfo(path);
                statusLabel.Text = string.Format(R._("{0} ({1} lines, {2} bytes)"),
                    Path.GetFileName(path), lines.Length, info.Length);
            }
            catch
            {
                previewBox.Text = R._("(Unable to preview file)");
            }
        }
    }
}
