using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolProblemReportSearchSavView : TranslatedWindow, IEditorView
    {
        readonly ToolProblemReportSearchSavViewModel _vm = new();
        public string ViewTitle => "No SAV file found";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolProblemReportSearchSavView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        /// <summary>True when the user confirmed a valid save path (DialogResult.OK).</summary>
        public bool Confirmed => _vm.DialogConfirmed;

        /// <summary>The picked save-file path (only meaningful when <see cref="Confirmed"/>).</summary>
        public string PickedFilename => _vm.GetFilename();

        async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog();
                dlg.Title = R._("Select SAV File");
                dlg.Filters?.Add(new FileDialogFilter { Name = "SAV Files", Extensions = { "sav" } });
                var result = await dlg.ShowAsync(this);
                if (result != null && result.Length > 0)
                {
                    // Populate the path; the user still confirms via OK (which
                    // validates). Clear any stale inline error.
                    _vm.SavFilename = result[0];
                    _vm.ErrorMessage = "";
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolProblemReportSearchSavView", ex.ToString());
            }
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            // Keep the dialog open with an inline message when the path is invalid,
            // so an empty/bad path can't silently confirm with no save (#1235).
            if (!_vm.Validate())
            {
                return;
            }
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
