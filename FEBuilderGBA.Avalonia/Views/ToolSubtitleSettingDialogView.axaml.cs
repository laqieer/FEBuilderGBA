using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolSubtitleSettingDialogView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolSubtitleSettingDialogViewViewModel _vm = new();
        public string ViewTitle => "Subtitle Settings";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Subtitle Settings", 910, 357, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolSubtitleSettingDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void BrowseFromRom_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path))
                    _vm.TranslateFromRomFilename = path;
            }
            catch (Exception ex)
            {
                Log.Error("ToolSubtitleSettingDialogView", ex.ToString());
            }
        }

        async void BrowseToRom_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(TopLevel.GetTopLevel(this));
                if (!string.IsNullOrEmpty(path))
                    _vm.TranslateToRomFilename = path;
            }
            catch (Exception ex)
            {
                Log.Error("ToolSubtitleSettingDialogView", ex.ToString());
            }
        }

        async void BrowseTranslateData_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog();
                dlg.Title = R._("Select Translation Data File");
                var result = await dlg.ShowAsync(TopLevel.GetTopLevel(this) as Window);
                if (result != null && result.Length > 0)
                    _vm.TranslateDataFilename = result[0];
            }
            catch (Exception ex)
            {
                Log.Error("ToolSubtitleSettingDialogView", ex.ToString());
            }
        }

        void Show_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "show";
            DialogResult = "show"; RequestClose();
        }

        void Hide_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "hide";
            DialogResult = "hide"; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
