#nullable enable

using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Dialogs
{
    public partial class GenerateRandomMapDialogContent : TranslatedUserControl, IEmbeddableEditor
    {
        readonly GenerateRandomMapDialogViewModel _vm;

        public string ViewTitle => _vm.TitleText;
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new(
            _vm.TitleText,
            620,
            520,
            CanResize: false);
        public object? DialogResult => _vm.Result;
        public GenerateRandomMapDialogResult? Result => _vm.Result;
        public bool CanClose => !_vm.IsBusy;
        public event EventHandler? CloseRequested;

        public GenerateRandomMapDialogContent()
            : this(new GenerateRandomMapDialogViewModel())
        {
        }

        internal GenerateRandomMapDialogContent(GenerateRandomMapDialogViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
            InitializeComponent();
            DataContext = _vm;
            _vm.SetBrowseHandlers(BrowseFEMapCreatorAsync, BrowseAssetsDirAsync);
            _vm.CloseRequested += (_, _) => RequestClose();
        }

        public void Configure(int width, int height)
        {
            _vm.Initialize(width, height);
        }

        public void NavigateTo(uint address) { }

        void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        async Task<string?> BrowseFEMapCreatorAsync()
        {
            IStorageProvider? storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider == null)
                return null;

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Select FEMapCreator program"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Programs") { Patterns = new[] { "*.exe", "*.dll" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } },
                }
            });

            if (files == null || files.Count == 0)
                return null;

            return files[0].TryGetLocalPath();
        }

        async Task<string?> BrowseAssetsDirAsync()
        {
            IStorageProvider? storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider == null)
                return null;

            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = R._("Select FEMapCreator assets directory"),
                AllowMultiple = false,
            });

            if (folders == null || folders.Count == 0)
                return null;

            return folders[0].TryGetLocalPath();
        }
    }
}
