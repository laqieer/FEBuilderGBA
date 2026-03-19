using Avalonia.Controls;
using Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class FERepoResourceBrowserWindow : Window
    {
        public string SelectedFilePath => (DataContext as FERepoResourceBrowserViewModel)?.SelectedFilePath;

        public FERepoResourceBrowserWindow() : this(false) { }

        public FERepoResourceBrowserWindow(bool musicMode)
        {
            InitializeComponent();
            DataContext = new FERepoResourceBrowserViewModel(musicMode);
            if (musicMode) Title = "FE-Repo Music Browser";
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
