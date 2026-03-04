using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolProblemReportSearchSavView : Window, IEditorView
    {
        readonly ToolProblemReportSearchSavViewModel _vm = new();
        public string ViewTitle => "Select SAV File";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolProblemReportSearchSavView()
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
                dlg.Title = "Select SAV File";
                dlg.Filters?.Add(new FileDialogFilter { Name = "SAV Files", Extensions = { "sav" } });
                var result = await dlg.ShowAsync(this);
                if (result != null && result.Length > 0)
                {
                    _vm.SavFilename = result[0];
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolProblemReportSearchSavView", ex.ToString());
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
