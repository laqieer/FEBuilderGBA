using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class FERepoResourceBrowserViewModel : ViewModelBase
    {
        ObservableCollection<CategoryNode> _categories = new();
        ObservableCollection<ResourceFileItem> _resourceFiles = new();
        ResourceFileItem _selectedFile;
        Bitmap _previewImage;
        string _statusText = "Select a category to browse";
        bool _canInsert;
        bool _notFound;
        CategoryNode _selectedCategory;
        string _repoRoot;
        bool _musicMode;

        /// <summary>The git command shown/copied when the submodule is missing (#1380 Part A).</summary>
        public const string SubmoduleInitCommand = "git submodule update --init resources/FE-Repo";

        public ObservableCollection<CategoryNode> Categories
        {
            get => _categories;
            set => SetField(ref _categories, value);
        }

        public ObservableCollection<ResourceFileItem> ResourceFiles
        {
            get => _resourceFiles;
            set => SetField(ref _resourceFiles, value);
        }

        public ResourceFileItem SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (SetField(ref _selectedFile, value))
                    OnSelectedFileChanged(value);
            }
        }

        public Bitmap PreviewImage
        {
            get => _previewImage;
            set => SetField(ref _previewImage, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public bool CanInsert
        {
            get => _canInsert;
            set => SetField(ref _canInsert, value);
        }

        /// <summary>
        /// True when the FE-Repo submodule was not found (empty placeholder or
        /// absent). The View binds a "copy init command" affordance to this so
        /// the actionable message is not buried behind a blank tree (#1380).
        /// </summary>
        public bool NotFound
        {
            get => _notFound;
            set => SetField(ref _notFound, value);
        }

        public CategoryNode SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetField(ref _selectedCategory, value) && value != null)
                    LoadCategoryFiles(value);
            }
        }

        public string SelectedFilePath { get; set; }

        public FERepoResourceBrowserViewModel() : this(false) { }

        public FERepoResourceBrowserViewModel(bool musicMode) : this(musicMode, null, null) { }

        /// <summary>
        /// Construct the browser, optionally pre-navigated to a seed
        /// category/subcategory (#1380 Part B). Seed is navigation only; full
        /// browsing remains available.
        /// </summary>
        public FERepoResourceBrowserViewModel(bool musicMode, string seedCategory, string seedSubCategory)
        {
            _musicMode = musicMode;
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            _repoRoot = musicMode
                ? FERepoResourceBrowser.FindMusicRepoRoot(baseDir)
                : FERepoResourceBrowser.FindRepoRoot(baseDir);
            LoadCategories();
            if (!string.IsNullOrEmpty(seedCategory))
                SelectSeed(seedCategory, seedSubCategory);
        }

        void LoadCategories()
        {
            if (_repoRoot == null)
            {
                NotFound = true;
                StatusText = "FE-Repo not found. Run: " + SubmoduleInitCommand;
                return;
            }

            foreach (string cat in FERepoResourceBrowser.GetCategories(_repoRoot))
            {
                var node = new CategoryNode { Name = cat, Category = cat };
                foreach (string sub in FERepoResourceBrowser.GetSubCategories(_repoRoot, cat))
                {
                    node.Children.Add(new CategoryNode { Name = sub, Category = cat, SubCategory = sub });
                }
                Categories.Add(node);
            }
        }

        /// <summary>
        /// Pre-select the category (and optional subcategory) node so the
        /// browser opens already showing that folder's files. A non-matching
        /// seed is ignored (the browser stays on the full category list).
        /// </summary>
        void SelectSeed(string category, string subCategory)
        {
            var top = Categories.FirstOrDefault(c =>
                string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));
            if (top == null) return;

            if (!string.IsNullOrEmpty(subCategory))
            {
                var child = top.Children.FirstOrDefault(c =>
                    string.Equals(c.SubCategory, subCategory, StringComparison.OrdinalIgnoreCase));
                if (child != null)
                {
                    SelectedCategory = child;
                    return;
                }
                // Subcategory not present — fall back to the top-level category.
            }
            SelectedCategory = top;
        }

        void LoadCategoryFiles(CategoryNode node)
        {
            if (node == null || _repoRoot == null) return;

            ResourceFiles.Clear();
            PreviewImage?.Dispose();
            PreviewImage = null;
            CanInsert = false;
            SelectedFilePath = null;

            var files = _musicMode
                ? FERepoResourceBrowser.GetMusicFiles(_repoRoot, node.Category, node.SubCategory, maxResults: 500)
                : FERepoResourceBrowser.GetResourceFiles(_repoRoot, node.Category, node.SubCategory, maxResults: 200);
            StatusText = $"{files.Length} resources found";
            if (files.Length >= 200) StatusText += " (limited to 200)";

            for (int i = 0; i < files.Length; i++)
            {
                ResourceFiles.Add(new ResourceFileItem
                {
                    FullPath = files[i].FullPath,
                    FileName = files[i].FileName,
                    FileSize = files[i].FileSize
                });
            }

        }

        void OnSelectedFileChanged(ResourceFileItem value)
        {
            PreviewImage?.Dispose();
            PreviewImage = null;
            CanInsert = false;
            SelectedFilePath = null;

            if (value == null) return;

            try
            {
                PreviewImage = new Bitmap(value.FullPath);
                SelectedFilePath = value.FullPath;
                CanInsert = true;
                var info = new FileInfo(value.FullPath);
                StatusText = $"{value.FileName} ({PreviewImage.PixelSize.Width}x{PreviewImage.PixelSize.Height}, {info.Length} bytes)";
            }
            catch
            {
                StatusText = $"Failed to load: {value.FileName}";
            }
        }
    }

    public class CategoryNode
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public ObservableCollection<CategoryNode> Children { get; set; } = new();
    }

    public class ResourceFileItem
    {
        public string FullPath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
    }
}
