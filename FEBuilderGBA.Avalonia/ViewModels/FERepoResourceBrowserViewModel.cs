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
        CategoryNode _selectedCategory;
        string _repoRoot;

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

        public FERepoResourceBrowserViewModel()
        {
            string baseDir = CoreState.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            _repoRoot = FERepoResourceBrowser.FindRepoRoot(baseDir);
            LoadCategories();
        }

        void LoadCategories()
        {
            if (_repoRoot == null)
            {
                StatusText = "FE-Repo not found. Run: git submodule update --init resources/FE-Repo";
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

        void LoadCategoryFiles(CategoryNode node)
        {
            if (node == null || _repoRoot == null) return;

            ResourceFiles.Clear();
            PreviewImage?.Dispose();
            PreviewImage = null;
            CanInsert = false;
            SelectedFilePath = null;

            var files = FERepoResourceBrowser.GetResourceFiles(_repoRoot, node.Category, node.SubCategory, maxResults: 200);
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
