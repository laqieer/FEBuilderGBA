using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.ViewModels;

using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class FERepoResourceBrowserWindow : TranslatedUserControl, IEmbeddableEditor
    {
        public string ViewTitle => "FE-Repo Resource Browser";
        public new bool IsLoaded => true;
        public EditorDescriptor Descriptor => new("FE-Repo Resource Browser", 900, 600, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public void NavigateTo(uint address) { }

        public string SelectedFilePath => (DataContext as FERepoResourceBrowserViewModel)?.SelectedFilePath ?? "";

        public FERepoResourceBrowserWindow() : this(false) { }

        public FERepoResourceBrowserWindow(bool musicMode)
        {
            InitializeComponent();
            Configure(musicMode);
        }

        /// <summary>
        /// Open the browser pre-navigated to a seed category/subcategory
        /// (#1380 Part B). Use FERepoResourceBrowser.GetFERepoFolderForEditor
        /// to obtain the seed for an editor kind.
        /// </summary>
        public FERepoResourceBrowserWindow(string seedCategory, string seedSubCategory)
        {
            InitializeComponent();
            Configure(false, seedCategory, seedSubCategory);
        }

        /// <summary>
        /// Open the browser in graphics or music mode, optionally pre-navigated
        /// to a seed category/subcategory (#1383 — music sibling of the #1380
        /// seed ctor). Music mode sets the music title and resolves the music
        /// repo root.
        /// </summary>
        public FERepoResourceBrowserWindow(bool musicMode, string seedCategory, string seedSubCategory)
        {
            InitializeComponent();
            Configure(musicMode, seedCategory, seedSubCategory);
        }

        public void Configure(bool musicMode, string? seedCategory = null, string? seedSubCategory = null)
        {
            DataContext = seedCategory == null || seedSubCategory == null
                ? new FERepoResourceBrowserViewModel(musicMode)
                : new FERepoResourceBrowserViewModel(musicMode, seedCategory, seedSubCategory);
        }

        void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            { DialogResult = SelectedFilePath; RequestClose(); }
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            { DialogResult = null; RequestClose(); }
        }

        async void CopyInitCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Copy BOTH the source-build submodule command AND the
                // released-build clone command for this browser's mode (graphics
                // vs music), so a released-zip user can copy the clone command
                // that the empty-state surfaces (#1669 review). Falls back to the
                // graphics submodule command if the DataContext is unexpectedly null.
                var vm = DataContext as FERepoResourceBrowserViewModel;
                string cmd = vm?.CopyCommand
                             ?? FERepoResourceBrowserViewModel.SubmoduleInitCommand;
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(cmd);
            }
            catch (Exception ex)
            {
                Log.Error("FERepoResourceBrowserWindow.CopyInitCommand failed: " + ex.ToString());
            }
        }
    }
}
