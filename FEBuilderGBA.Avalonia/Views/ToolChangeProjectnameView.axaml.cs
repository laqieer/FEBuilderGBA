using global::Avalonia;
using System;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolChangeProjectnameView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolChangeProjectnameViewViewModel _vm = new();
        public string ViewTitle => "Change Project Name";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Change Project Name", 791, 360, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight, CanResize: false);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

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

            DialogResult = "OK"; RequestClose();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
