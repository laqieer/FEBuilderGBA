using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// Browser form for FE-Repo resources. Shows categories, thumbnails, and allows selecting
    /// a resource file for insertion into the ROM.
    /// </summary>
    public class FERepoResourceBrowserForm : Form
    {
        TreeView categoryTree;
        ListView fileListView;
        PictureBox previewBox;
        Button insertButton;
        Button cancelButton;
        Label statusLabel;
        ImageList thumbnailList;

        string repoRoot;

        // #1380 Part B — optional seed so the browser opens pre-navigated to a
        // category/subcategory for a specific graphics editor.
        readonly string seedCategory;
        readonly string seedSubCategory;

        /// <summary>
        /// The full path to the selected resource file, or null if cancelled.
        /// </summary>
        public string SelectedFilePath { get; private set; }

        /// <summary>
        /// #1380 Part B — add an "FE-Repo" browse button to a graphics editor in
        /// its Import button's parent panel, placed in a NEW row BELOW every
        /// existing control in that panel (so it never overlaps the Import/Export
        /// row, the AP row, or the source/jump buttons), and GROW the panel's
        /// height + MinimumSize so the new button is fully visible rather than
        /// clipped by a short button panel (Copilot review on #1394).
        /// The <paramref name="rightOf"/> parameter is unused (kept for call-site
        /// readability of which Import row the button relates to).
        /// </summary>
        public static Button AddBrowseButton(Button importButton, Button rightOf, EventHandler handler)
        {
            Control parent = importButton.Parent;

            // Place the new row below the BOTTOM-MOST existing control in the
            // panel — including initially-hidden source/jump buttons, whose
            // designer Bounds still define the occupied area — so there is no
            // horizontal collision regardless of panel layout.
            int bottomMost = importButton.Bottom;
            if (parent != null)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c.Bottom > bottomMost) bottomMost = c.Bottom;
                }
            }

            var feRepoButton = new Button
            {
                Text = R._("FE-Repo"),
                Size = new Size(107, importButton.Height),
                Location = new Point(importButton.Left, bottomMost + 2)
            };
            feRepoButton.Click += handler;

            if (parent != null)
            {
                parent.Controls.Add(feRepoButton);
                // Panels in these editors have fixed designer Sizes; grow both
                // Height and MinimumSize so a layout pass cannot shrink it back
                // and clip the new button.
                int needed = feRepoButton.Bottom + 2;
                if (parent.Height < needed)
                {
                    parent.Height = needed;
                    parent.MinimumSize = new Size(parent.MinimumSize.Width, needed);
                }
            }
            return feRepoButton;
        }

        public FERepoResourceBrowserForm() : this(null, null) { }

        /// <summary>
        /// Open the browser pre-navigated to a seed category (and optional
        /// subcategory). Use
        /// <see cref="FERepoResourceBrowser.GetFERepoFolderForEditor"/> to
        /// resolve the seed for an editor kind (#1380 Part B).
        /// </summary>
        public FERepoResourceBrowserForm(string seedCategory, string seedSubCategory)
        {
            this.seedCategory = seedCategory;
            this.seedSubCategory = seedSubCategory;
            InitializeComponent();
            LoadCategories();
            SelectSeed();
        }

        void InitializeComponent()
        {
            Text = R._("FE-Repo Resource Browser");
            Size = new Size(900, 600);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;

            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 250,
                Orientation = Orientation.Vertical
            };

            // Left panel: category tree
            categoryTree = new TreeView { Dock = DockStyle.Fill };
            categoryTree.AfterSelect += CategoryTree_AfterSelect;
            splitMain.Panel1.Controls.Add(categoryTree);

            // Right panel: file list + preview
            var splitRight = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 350,
                Orientation = Orientation.Vertical
            };

            // File list with thumbnails
            thumbnailList = new ImageList { ImageSize = new Size(48, 48), ColorDepth = ColorDepth.Depth32Bit };
            fileListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                LargeImageList = thumbnailList,
                MultiSelect = false
            };
            fileListView.SelectedIndexChanged += FileListView_SelectedIndexChanged;
            fileListView.DoubleClick += FileListView_DoubleClick;
            splitRight.Panel1.Controls.Add(fileListView);

            // Preview panel
            var previewPanel = new Panel { Dock = DockStyle.Fill };
            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(240, 240, 240)
            };
            previewPanel.Controls.Add(previewBox);
            splitRight.Panel2.Controls.Add(previewPanel);

            splitMain.Panel2.Controls.Add(splitRight);

            // Bottom panel: buttons + status
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = R._("Select a resource to insert")
            };
            insertButton = new Button
            {
                Text = R._("Insert"),
                Dock = DockStyle.Right,
                Width = 80,
                Enabled = false
            };
            insertButton.Click += (s, e) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };
            cancelButton = new Button
            {
                Text = R._("Cancel"),
                Dock = DockStyle.Right,
                Width = 80
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            bottomPanel.Controls.Add(statusLabel);
            bottomPanel.Controls.Add(insertButton);
            bottomPanel.Controls.Add(cancelButton);

            Controls.Add(splitMain);
            Controls.Add(bottomPanel);
        }

        void LoadCategories()
        {
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            repoRoot = FERepoResourceBrowser.FindRepoRoot(baseDir);
            if (repoRoot == null)
            {
                categoryTree.Nodes.Add(R._("FE-Repo not found. Run: git submodule update --init resources/FE-Repo"));
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

        // #1380 Part B — pre-select the seed category (and optional
        // subcategory) node so the browser opens already showing that folder's
        // files. A non-matching seed is ignored (full category list stays).
        void SelectSeed()
        {
            if (repoRoot == null || string.IsNullOrEmpty(seedCategory)) return;

            foreach (TreeNode catNode in categoryTree.Nodes)
            {
                if (!string.Equals(catNode.Tag as string, seedCategory, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(seedSubCategory))
                {
                    foreach (TreeNode subNode in catNode.Nodes)
                    {
                        if (string.Equals(subNode.Tag as string, seedSubCategory, StringComparison.OrdinalIgnoreCase))
                        {
                            catNode.Expand();
                            categoryTree.SelectedNode = subNode;
                            return;
                        }
                    }
                    // Subcategory not present — fall back to the top-level node.
                }
                categoryTree.SelectedNode = catNode;
                return;
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

            LoadFiles(category, subCategory);
        }

        void LoadFiles(string category, string subCategory)
        {
            fileListView.Items.Clear();
            thumbnailList.Images.Clear();
            previewBox.Image?.Dispose();
            previewBox.Image = null;
            insertButton.Enabled = false;
            SelectedFilePath = null;

            var files = FERepoResourceBrowser.GetResourceFiles(repoRoot, category, subCategory, maxResults: 200);
            statusLabel.Text = string.Format(R._("{0} resources found"), files.Length);

            int maxThumbnails = files.Length;
            for (int i = 0; i < maxThumbnails; i++)
            {
                var entry = files[i];
                string key = i.ToString();
                try
                {
                    using (var img = Image.FromFile(entry.FullPath))
                    {
                        thumbnailList.Images.Add(key, new Bitmap(img, 48, 48));
                    }
                }
                catch
                {
                    thumbnailList.Images.Add(key, new Bitmap(48, 48));
                }

                var item = new ListViewItem(entry.FileName, key)
                {
                    Tag = entry.FullPath
                };
                fileListView.Items.Add(item);
            }

            if (files.Length >= 200)
            {
                statusLabel.Text += R._(" (limited to 200)");
            }
        }

        void FileListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0)
            {
                previewBox.Image?.Dispose();
                previewBox.Image = null;
                insertButton.Enabled = false;
                SelectedFilePath = null;
                return;
            }

            string path = fileListView.SelectedItems[0].Tag as string;
            SelectedFilePath = path;
            insertButton.Enabled = true;

            try
            {
                previewBox.Image?.Dispose();
                previewBox.Image = Image.FromFile(path);
                var info = new FileInfo(path);
                statusLabel.Text = string.Format(R._("{0} ({1}x{2}, {3} bytes)"),
                    Path.GetFileName(path), previewBox.Image.Width, previewBox.Image.Height, info.Length);
            }
            catch
            {
                previewBox.Image = null;
            }
        }

        void FileListView_DoubleClick(object sender, EventArgs e)
        {
            if (SelectedFilePath != null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                previewBox?.Image?.Dispose();
                thumbnailList?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
