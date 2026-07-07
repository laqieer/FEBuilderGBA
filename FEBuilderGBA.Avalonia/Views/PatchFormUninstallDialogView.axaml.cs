using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PatchFormUninstallDialogView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly PatchFormUninstallDialogViewModel _vm = new();
        public string ViewTitle => "Patch Uninstallation";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Patch Uninstallation", 1193, 350, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public PatchFormUninstallDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        /// <summary>#1462: name of the patch being uninstalled (for the dialog title/labels).</summary>
        public void SeedPatchName(string patchName) => _vm.PatchName = patchName ?? "";

        /// <summary>True when the user pressed Uninstall (vs. closing the window).</summary>
        public bool UserConfirmed => _vm.UserConfirmed;

        /// <summary>The user-selected patch-free ("clean") ROM path.</summary>
        public string OriginalFilename => _vm.OriginalFilename;

        async void SelectOriginal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path))
                    _vm.OriginalFilename = path;
            }
            catch (Exception ex)
            {
                Log.Error("PatchFormUninstallDialogView", ex.ToString());
            }
        }

        void Yes_Click(object? sender, RoutedEventArgs e)
        {
            _vm.UserConfirmed = true;
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
