using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolDiffDebugSelectView : Window, IEditorView
    {
        readonly ToolDiffDebugSelectViewModel _vm = new();
        public string ViewTitle => "Comparison Debug Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolDiffDebugSelectView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        async void BrowseOriginal_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                {
                    _vm.OriginalFilename = path;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolDiffDebugSelectView", ex.ToString());
            }
        }

        void Compare_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.GetSelectedBackupPath() == null)
                return;
            _vm.DialogResult = "compare";
            // Placeholder: would open ToolThreeMargeView with selected backup
        }

        void TestPlay_Click(object? sender, RoutedEventArgs e)
        {
            if (_vm.GetSelectedBackupPath() == null)
                return;
            _vm.DialogResult = "testplay";
            // Placeholder: would launch emulator with selected backup ROM
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
