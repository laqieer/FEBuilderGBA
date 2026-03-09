using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolSubtitleSettingDialogView : Window, IEditorView
    {
        readonly ToolSubtitleSettingDialogViewViewModel _vm = new();
        public string ViewTitle => "Subtitle Settings";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolSubtitleSettingDialogView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        async void BrowseFromRom_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
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
                var path = await FileDialogHelper.OpenRomFile(this);
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
                dlg.Title = "Select Translation Data File";
                var result = await dlg.ShowAsync(this);
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
            Close("show");
        }

        void Hide_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogResult = "hide";
            Close("hide");
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
