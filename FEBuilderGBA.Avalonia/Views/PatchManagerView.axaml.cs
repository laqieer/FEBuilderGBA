using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class PatchManagerView : Window, IEditorView
    {
        readonly PatchManagerViewModel _vm = new();

        public string ViewTitle => "Patch Manager";
        public bool IsLoaded => _vm.IsLoaded;

        public PatchManagerView()
        {
            InitializeComponent();
            Opened += (_, _) => LoadPatches();
            PatchListBox.SelectionChanged += OnPatchSelected;
            SearchBox.TextChanged += OnSearchTextChanged;
            InstallButton.Click += OnInstallClick;
            UninstallButton.Click += OnUninstallClick;
        }

        void LoadPatches()
        {
            try
            {
                _vm.LoadPatchList();
                PatchListBox.ItemsSource = _vm.FilteredPatches;
                UpdateSummary();
            }
            catch (Exception ex)
            {
                Log.Error("PatchManagerView.LoadPatches failed: {0}", ex.Message);
            }
        }

        void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _vm.FilterText = SearchBox.Text ?? "";
            UpdateSummary();
        }

        void OnPatchSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (PatchListBox.SelectedItem is PatchEntry patch)
            {
                _vm.SelectedPatch = patch;
                UpdateDetails(patch);
            }
        }

        void UpdateSummary()
        {
            string filter = string.IsNullOrWhiteSpace(_vm.FilterText) ? "" : $" (filtered: {_vm.FilteredPatches.Count})";
            SummaryLabel.Text = $"Total: {_vm.TotalCount} patches | Installed: {_vm.InstalledCount}{filter}";
        }

        void UpdateDetails(PatchEntry patch)
        {
            DetailName.Text = patch.Name;
            DetailStatus.Text = patch.StatusText;
            DetailAuthor.Text = string.IsNullOrEmpty(patch.Author) ? "(unknown)" : patch.Author;
            DetailType.Text = string.IsNullOrEmpty(patch.Type) ? "(not specified)" : patch.Type;
            DetailTags.Text = string.IsNullOrEmpty(patch.Tags) ? "(none)" : patch.Tags;
            DetailDirectory.Text = patch.DirectoryPath;
            DetailDescription.Text = string.IsNullOrEmpty(patch.Description)
                ? "(no description available)"
                : patch.Description;

            UpdateActionButtons();
            StatusMessageLabel.Text = "";
        }

        void UpdateActionButtons()
        {
            InstallButton.IsEnabled = _vm.CanInstall;
            UninstallButton.IsEnabled = _vm.CanUninstall;
        }

        void OnInstallClick(object? sender, RoutedEventArgs e)
        {
            string msg = _vm.InstallPatch();
            StatusMessageLabel.Text = msg;

            // Refresh the detail display
            if (_vm.SelectedPatch != null)
            {
                DetailStatus.Text = _vm.SelectedPatch.StatusText;
                UpdateActionButtons();
            }
            UpdateSummary();

            // Refresh the list to show updated status
            PatchListBox.ItemsSource = null;
            PatchListBox.ItemsSource = _vm.FilteredPatches;
        }

        void OnUninstallClick(object? sender, RoutedEventArgs e)
        {
            string msg = _vm.UninstallPatch();
            StatusMessageLabel.Text = msg;
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem()
        {
            if (PatchListBox.ItemCount > 0)
                PatchListBox.SelectedIndex = 0;
        }
    }
}
