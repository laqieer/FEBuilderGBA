using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OptionsView : TranslatedWindow
    {
        readonly OptionsViewModel _vm = new();
        string _originalLanguageCode = "";

        public OptionsView()
        {
            InitializeComponent();
            Opened += OnOpened;
        }
        void OnOpened(object? sender, EventArgs e)
        {
            _vm.Load();
            _originalLanguageCode = OptionsViewModel.ExtractLanguageCode(_vm.Language);

            // Populate language combo
            LanguageCombo.ItemsSource = _vm.AvailableLanguages;
            int langIdx = _vm.AvailableLanguages.IndexOf(_vm.Language);
            LanguageCombo.SelectedIndex = langIdx >= 0 ? langIdx : 0;

            // General tab
            GitPathTextBox.Text = _vm.GitPath;
            AutoBackupCheckBox.IsChecked = _vm.AutoBackup;
            AutoSaveCheckBox.IsChecked = _vm.AutoSaveEnabled;
            AutoSaveIntervalBox.Value = _vm.AutoSaveIntervalMinutes;
            Patch2UrlTextBox.Text = _vm.SubmodulePatch2Url;
            FERepoUrlTextBox.Text = _vm.SubmoduleFERepoUrl;
            FERepoMusicUrlTextBox.Text = _vm.SubmoduleFERepoMusicUrl;

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

            var allFiles = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
            var exeFiles = new FilePickerFileType(R._("Executables")) { Patterns = new[] { "*.exe", "*" } };
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = R._("Select File"),
                AllowMultiple = false,
                FileTypeFilter = new[] { exeFiles, allFiles },
            });
            if (files.Count > 0)
            {
                // #1639: these options configure paths to EXTERNAL tool
                // executables that are launched as processes (devkitARM / EA),
                // so a real filesystem path is required. On Android (no local
                // path) leave the field and message instead of silently doing
                // nothing.
                string? path = files[0].TryGetLocalPath();
                if (path != null)
                    target.Text = path;
                else
                    CoreState.Services?.ShowInfo(R._("This setting configures an external tool path and requires desktop file-system access; it is not available on this device."));
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
                Title = R._("Select Directory"),
                AllowMultiple = false,
            });
            if (folders.Count > 0)
            {
                // #1639: tool directory config — a real local directory is
                // required. On Android (no local path) message instead of
                // silently doing nothing.
                string? path = folders[0].TryGetLocalPath();
                if (path != null)
                    target.Text = path;
                else
                    CoreState.Services?.ShowInfo(R._("This setting configures an external tool directory and requires desktop file-system access; it is not available on this device."));
            }
        }

        void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            // Push UI values back to ViewModel (display string, code extracted in Save())
            if (LanguageCombo.SelectedItem is string lang)
                _vm.Language = lang;  // e.g. "ja — 日本語"
            _vm.GitPath = GitPathTextBox.Text ?? "git";
            _vm.AutoBackup = AutoBackupCheckBox.IsChecked == true;
            _vm.AutoSaveEnabled = AutoSaveCheckBox.IsChecked == true;
            _vm.AutoSaveIntervalMinutes = (int)(AutoSaveIntervalBox.Value ?? 5);
            _vm.SubmodulePatch2Url = Patch2UrlTextBox.Text ?? "";
            _vm.SubmoduleFERepoUrl = FERepoUrlTextBox.Text ?? "";
            _vm.SubmoduleFERepoMusicUrl = FERepoMusicUrlTextBox.Text ?? "";

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

            string newLangCode = OptionsViewModel.ExtractLanguageCode(_vm.Language);
            bool languageChanged = !string.Equals(_originalLanguageCode, newLangCode, StringComparison.Ordinal);

            _vm.Save();

            if (languageChanged)
            {
                // Language change takes effect immediately for new UI strings,
                // but some editors may need to be re-opened to see the change.
                // The main window refreshes via CoreState.LanguageChanged event.
            }

            Close(true);
        }

        void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
