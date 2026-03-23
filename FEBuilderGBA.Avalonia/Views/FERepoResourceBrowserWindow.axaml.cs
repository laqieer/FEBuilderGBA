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

        void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            Close(SelectedFilePath);
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
