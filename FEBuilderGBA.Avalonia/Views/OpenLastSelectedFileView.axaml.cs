using System;
using global::Avalonia;
using System.Diagnostics;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OpenLastSelectedFileView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly OpenLastSelectedFileViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "Open Last Selected File";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Open Last Selected File", 760, 240);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public OpenLastSelectedFileView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                Refresh();
            }
        }

        void Refresh()
        {
            try
            {
                _vm.Load();
                PathTextBox.Text = _vm.LastFile;
                bool has = _vm.HasFile;
                OpenButton.IsEnabled = has;
                FolderButton.IsEnabled = has;
            }
            catch (Exception ex)
            {
                Log.Error("OpenLastSelectedFileView.Refresh failed: " + ex);
            }
        }

        // Open the last-selected file with its default application (WF U.OpenURLOrFile).
        async void Open_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasFile) return;
                var result = await ExternalLauncher.Current.OpenPathAsync(_vm.LastFile);
                if (!result.IsSucceeded)
                    Log.Error("OpenLastSelectedFileView.Open_Click launch failed: " + result.Message);
            }
            catch (Exception ex)
            {
                Log.Error("OpenLastSelectedFileView.Open_Click failed: " + ex);
            }
        }

        // Reveal the file in the system file manager (WF U.SelectFileByExplorer).
        async void Folder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasFile) return;
                var result = await ExternalLauncher.Current.RevealPathAsync(_vm.LastFile);
                if (!result.IsSucceeded)
                    Log.Error("OpenLastSelectedFileView.Folder_Click launch failed: " + result.Message);
            }
            catch (Exception ex)
            {
                Log.Error("OpenLastSelectedFileView.Folder_Click failed: " + ex);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
