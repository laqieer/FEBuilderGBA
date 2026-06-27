using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.ViewModels;

using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class FERepoResourceBrowserWindow : TranslatedWindow
    {
        public string SelectedFilePath => (DataContext as FERepoResourceBrowserViewModel)?.SelectedFilePath;

        public FERepoResourceBrowserWindow() : this(false) { }

        public FERepoResourceBrowserWindow(bool musicMode)
        {
            InitializeComponent();
            DataContext = new FERepoResourceBrowserViewModel(musicMode);
            if (musicMode) Title = R._("FE-Repo Music Browser");
        }

        /// <summary>
        /// Open the browser pre-navigated to a seed category/subcategory
        /// (#1380 Part B). Use FERepoResourceBrowser.GetFERepoFolderForEditor
        /// to obtain the seed for an editor kind.
        /// </summary>
        public FERepoResourceBrowserWindow(string seedCategory, string seedSubCategory)
        {
            InitializeComponent();
            DataContext = new FERepoResourceBrowserViewModel(false, seedCategory, seedSubCategory);
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
            DataContext = new FERepoResourceBrowserViewModel(musicMode, seedCategory, seedSubCategory);
            if (musicMode) Title = R._("FE-Repo Music Browser");
        }

        void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            Close(SelectedFilePath);
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
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
                var clipboard = Clipboard;
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
