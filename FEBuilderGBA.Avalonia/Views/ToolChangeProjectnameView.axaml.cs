using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolChangeProjectnameView : TranslatedWindow, IEditorView
    {
        readonly ToolChangeProjectnameViewViewModel _vm = new();
        public string ViewTitle => "Change Project Name";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolChangeProjectnameView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void OK_Click(object? sender, RoutedEventArgs e)
        {
            // Perform the real project-file rename (ROM + backups + etc dir),
            // mirroring WinForms ToolChangeProjectnameForm.ChangeName (#1461).
            // TryRename validates (modified/virtual/bad-name/same-name) and sets
            // StatusMessage on failure; a non-null result is the new ROM path.
            string newPath = _vm.TryRename();
            if (newPath == null)
            {
                // Validation or IO failure — keep the dialog open so the user
                // can read the status message and correct the name.
                return;
            }

            // Reload the renamed ROM in the main window, mirroring the WinForms
            // ReOpenMainForm() + LoadROM(newROMPath) tail.
            if (WindowManager.Instance.MainWindow is MainWindow mw && File.Exists(newPath))
            {
                mw.LoadRomFile(newPath);
            }

            Close("OK");
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
