using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolProblemReportSearchBackupView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolProblemReportSearchBackupViewModel _vm = new();
        public string ViewTitle => "No past backups found";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("No past backups found", 906, 220, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolProblemReportSearchBackupView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        /// <summary>True when the user confirmed a valid backup-ROM path (DialogResult.OK).</summary>
        public bool Confirmed => _vm.DialogConfirmed;

        /// <summary>The picked clean / old backup ROM path (only meaningful when <see cref="Confirmed"/>).</summary>
        public string PickedFilename => _vm.GetFilename();

        async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog();
                dlg.Title = R._("Select Backup File");
                // The backup is a clean / old ROM used as the UPS-delta source
                // (read as raw bytes), so accept ROM files.
                dlg.Filters?.Add(new FileDialogFilter { Name = "GBA ROMs", Extensions = { "gba", "bin" } });
                var result = await dlg.ShowAsync(TopLevel.GetTopLevel(this) as Window);
                if (result != null && result.Length > 0)
                {
                    // Populate the path; the user still confirms via OK (which
                    // validates). Clear any stale inline error.
                    _vm.BackupFilename = result[0];
                    _vm.ErrorMessage = "";
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolProblemReportSearchBackupView", ex.ToString());
            }
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            // Keep the dialog open with an inline message when the path is invalid,
            // so an empty/bad path can't silently confirm with no backup (#1235).
            if (!_vm.Validate())
            {
                return;
            }
            _vm.DialogConfirmed = true;
            RequestClose();
        }

        void Cancel_Click(object? sender, RoutedEventArgs e)
        {
            _vm.DialogConfirmed = false;
            RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
