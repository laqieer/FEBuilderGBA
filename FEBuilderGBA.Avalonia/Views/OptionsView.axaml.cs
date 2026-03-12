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

            // Populate text fields
            GitPathTextBox.Text = _vm.GitPath;
            EmulatorPathTextBox.Text = _vm.EmulatorPath;
            AutoBackupCheckBox.IsChecked = _vm.AutoBackup;
        }

        async void BrowseGit_Click(object? sender, RoutedEventArgs e)
        {
            var allFiles = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
            var exeFiles = new FilePickerFileType("Executables") { Patterns = new[] { "*.exe", "*" } };
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Git Executable",
                AllowMultiple = false,
                FileTypeFilter = new[] { exeFiles, allFiles },
            });
            if (files.Count > 0)
            {
                string? path = files[0].TryGetLocalPath();
                if (path != null)
                    GitPathTextBox.Text = path;
            }
        }

        async void BrowseEmulator_Click(object? sender, RoutedEventArgs e)
        {
            var allFiles = new FilePickerFileType("All Files") { Patterns = new[] { "*" } };
            var exeFiles = new FilePickerFileType("Executables") { Patterns = new[] { "*.exe", "*" } };
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select GBA Emulator",
                AllowMultiple = false,
                FileTypeFilter = new[] { exeFiles, allFiles },
            });
            if (files.Count > 0)
            {
                string? path = files[0].TryGetLocalPath();
                if (path != null)
                    EmulatorPathTextBox.Text = path;
            }
        }

        void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            // Push UI values back to ViewModel
            if (LanguageCombo.SelectedItem is string lang)
                _vm.Language = lang;
            _vm.GitPath = GitPathTextBox.Text ?? "git";
            _vm.EmulatorPath = EmulatorPathTextBox.Text ?? "";
            _vm.AutoBackup = AutoBackupCheckBox.IsChecked == true;

            _vm.Save();
            Close(true);
        }

        void CancelButton_Click(object? sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
