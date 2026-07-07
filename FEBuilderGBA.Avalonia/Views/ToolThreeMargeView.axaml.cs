using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using global::Avalonia.Platform.Storage;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolThreeMargeView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolThreeMargeViewViewModel _vm = new();
        public string ViewTitle => "Three-Way Merge";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Three-Way Merge", 900, 700, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolThreeMargeView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        async void BrowseOriginal_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this) as Window);
            if (path != null)
                _vm.OriginalPath = path;
        }

        async void BrowseMine_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this) as Window);
            if (path != null)
                _vm.MyPath = path;
        }

        async void BrowseTheirs_Click(object? sender, RoutedEventArgs e)
        {
            var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this) as Window);
            if (path != null)
                _vm.TheirsPath = path;
        }

        void Merge_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanMerge)
                return;
            _vm.RunMerge();
        }

        async void Save_Click(object? sender, RoutedEventArgs e)
        {
            // #1639: the merged ROM is a single-file output → pick the handle and
            // write through the SAF bridge so Android content:// targets work.
            // SaveMerged returns false (no write) on no merge result — the bridge's
            // missing-output guard then returns null, so nothing is streamed back.
            var file = await FileDialogHelper.SaveRomFilePick(TopLevel.GetTopLevel(this) as Window, "merged.gba");
            if (file == null) return;
            string? written = await FileDialogHelper.WriteViaAsync(file, p => _vm.SaveMerged(p));
            // On a SAF target the VM's status shows the temp path; rewrite it with
            // the chosen document name once the bridge has written the file.
            if (written != null && string.IsNullOrEmpty(file.TryGetLocalPath()))
                _vm.StatusText = $"Merged ROM saved to: {file.Name ?? written}";
        }

        void Close_Click(object? sender, RoutedEventArgs e) => RequestClose();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
