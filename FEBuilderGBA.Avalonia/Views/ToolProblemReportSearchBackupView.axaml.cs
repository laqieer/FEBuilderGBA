using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolProblemReportSearchBackupView : Window, IEditorView
    {
        readonly ToolProblemReportSearchBackupViewModel _vm = new();
        public string ViewTitle => "Select Backup";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolProblemReportSearchBackupView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog();
                dlg.Title = "Select Backup File";
                dlg.Filters?.Add(new FileDialogFilter { Name = "Backup Files", Extensions = { "7z", "gba" } });
                var result = await dlg.ShowAsync(this);
                if (result != null && result.Length > 0)
                {
                    _vm.BackupFilename = result[0];
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolProblemReportSearchBackupView", ex.ToString());
            }
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = true;
            Close();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = false;
            Close();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
