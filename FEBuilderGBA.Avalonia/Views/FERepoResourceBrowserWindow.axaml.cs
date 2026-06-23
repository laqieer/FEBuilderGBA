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
                var clipboard = Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(FERepoResourceBrowserViewModel.SubmoduleInitCommand);
            }
            catch (Exception ex)
            {
                Log.Error("FERepoResourceBrowserWindow.CopyInitCommand failed: " + ex.ToString());
            }
        }
    }
}
