using System;
using System.Diagnostics;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OpenLastSelectedFileView : TranslatedWindow, IEditorView
    {
        readonly OpenLastSelectedFileViewModel _vm = new();

        public string ViewTitle => "Open Last Selected File";
        public bool IsLoaded => _vm.IsLoaded;

        public OpenLastSelectedFileView()
        {
            InitializeComponent();
            Opened += (_, _) => Refresh();
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
        void Open_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasFile) return;
                Process.Start(new ProcessStartInfo(_vm.LastFile) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("OpenLastSelectedFileView.Open_Click failed: " + ex);
            }
        }

        // Reveal the file in the system file manager (WF U.SelectFileByExplorer).
        void Folder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasFile) return;
                RevealInExplorer(_vm.LastFile);
            }
            catch (Exception ex)
            {
                Log.Error("OpenLastSelectedFileView.Folder_Click failed: " + ex);
            }
        }

        // No try/catch here — Folder_Click's handler catches + LOGS any failure (a silent
        // swallow here would hide platform-specific errors and make the caller's catch moot).
        static void RevealInExplorer(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            else
            {
                string dir = Path.GetDirectoryName(path) ?? "";
                if (!string.IsNullOrEmpty(dir))
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
