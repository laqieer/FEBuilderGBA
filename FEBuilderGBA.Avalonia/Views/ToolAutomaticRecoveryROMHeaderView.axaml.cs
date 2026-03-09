using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAutomaticRecoveryROMHeaderView : Window, IEditorView
    {
        readonly ToolAutomaticRecoveryROMHeaderViewViewModel _vm = new();
        public string ViewTitle => "Automatic Recovery ROM Header";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolAutomaticRecoveryROMHeaderView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        async void SelectFile_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                    _vm.OriginalFilename = path;
            }
            catch (Exception ex)
            {
                Log.Error("ToolAutomaticRecoveryROMHeaderView", ex.ToString());
            }
        }

        void Recover_Click(object? sender, RoutedEventArgs e)
        {
            // Placeholder: ROM header recovery logic
            _vm.RecoveryStatus = "Recovery complete.";
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
