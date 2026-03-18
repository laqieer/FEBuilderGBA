using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OptionsView : Window
    {
        readonly OptionsViewModel _vm = new();

        public OptionsView()
        {
            InitializeComponent();
            Opened += OnOpened;
        }

        void OnOpened(object? sender, EventArgs e)
        {
            _vm.Load();

            // Populate language combo
            LanguageCombo.ItemsSource = _vm.AvailableLanguages;
            int langIdx = _vm.AvailableLanguages.IndexOf(_vm.Language);
            LanguageCombo.SelectedIndex = langIdx >= 0 ? langIdx : 0;

            // General tab
            GitPathTextBox.Text = _vm.GitPath;
            AutoBackupCheckBox.IsChecked = _vm.AutoBackup;

            // External Tools tab
            EmulatorTextBox.Text = _vm.Emulator;
            Emulator2TextBox.Text = _vm.Emulator2;
            BinaryEditorTextBox.Text = _vm.BinaryEditor;
            Program1TextBox.Text = _vm.Program1;
            Program2TextBox.Text = _vm.Program2;
            Program3TextBox.Text = _vm.Program3;
            SappyTextBox.Text = _vm.Sappy;
            Mid2agbTextBox.Text = _vm.Mid2agb;
            GbaMusRiperTextBox.Text = _vm.GbaMusRiper;
            SoxTextBox.Text = _vm.Sox;
            Midfix4agbTextBox.Text = _vm.Midfix4agb;
            EventAssemblerTextBox.Text = _vm.EventAssembler;
            DevkitproEabiTextBox.Text = _vm.DevkitproEabi;
            GoldroadAsmTextBox.Text = _vm.GoldroadAsm;
            CflagsTextBox.Text = _vm.Cflags;
            RetdecTextBox.Text = _vm.Retdec;
            Python3TextBox.Text = _vm.Python3;
            FeclibTextBox.Text = _vm.Feclib;
            SrccodeTexteditorTextBox.Text = _vm.SrccodeTexteditor;
            SrccodeDirectoryTextBox.Text = _vm.SrccodeDirectory;
        }

        async void BrowseFile_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string targetName)
                return;
            var target = this.FindControl<TextBox>(targetName);
            if (target == null) return;

            var allFiles = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
            var exeFiles = new FilePickerFileType("Executables") { Patterns = new[] { "*.exe", "*" } };
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select File",
                AllowMultiple = false,
                FileTypeFilter = new[] { exeFiles, allFiles },
            });
            if (files.Count > 0)
            {
                string? path = files[0].TryGetLocalPath();
                if (path != null)
                    target.Text = path;
            }
        }

        async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string targetName)
                return;
            var target = this.FindControl<TextBox>(targetName);
            if (target == null) return;

            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Directory",
                AllowMultiple = false,
            });
            if (folders.Count > 0)
            {
                string? path = folders[0].TryGetLocalPath();
                if (path != null)
                    target.Text = path;
            }
        }

        void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            // Push UI values back to ViewModel (display string, code extracted in Save())
            if (LanguageCombo.SelectedItem is string lang)
                _vm.Language = lang;  // e.g. "ja — 日本語"
            _vm.GitPath = GitPathTextBox.Text ?? "git";
            _vm.AutoBackup = AutoBackupCheckBox.IsChecked == true;

            // External Tools
            _vm.Emulator = EmulatorTextBox.Text ?? "";
            _vm.Emulator2 = Emulator2TextBox.Text ?? "";
            _vm.BinaryEditor = BinaryEditorTextBox.Text ?? "";
            _vm.Program1 = Program1TextBox.Text ?? "";
            _vm.Program2 = Program2TextBox.Text ?? "";
            _vm.Program3 = Program3TextBox.Text ?? "";
            _vm.Sappy = SappyTextBox.Text ?? "";
            _vm.Mid2agb = Mid2agbTextBox.Text ?? "";
            _vm.GbaMusRiper = GbaMusRiperTextBox.Text ?? "";
            _vm.Sox = SoxTextBox.Text ?? "";
            _vm.Midfix4agb = Midfix4agbTextBox.Text ?? "";
            _vm.EventAssembler = EventAssemblerTextBox.Text ?? "";
            _vm.DevkitproEabi = DevkitproEabiTextBox.Text ?? "";
            _vm.GoldroadAsm = GoldroadAsmTextBox.Text ?? "";
            _vm.Cflags = CflagsTextBox.Text ?? "";
            _vm.Retdec = RetdecTextBox.Text ?? "";
            _vm.Python3 = Python3TextBox.Text ?? "";
            _vm.Feclib = FeclibTextBox.Text ?? "";
            _vm.SrccodeTexteditor = SrccodeTexteditorTextBox.Text ?? "";
            _vm.SrccodeDirectory = SrccodeDirectoryTextBox.Text ?? "";

            _vm.Save();
            Close(true);
        }

        void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
