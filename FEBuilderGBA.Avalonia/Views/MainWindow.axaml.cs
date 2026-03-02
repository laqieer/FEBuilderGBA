using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void OpenRom_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open ROM File",
                Filters = new System.Collections.Generic.List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "GBA ROM", Extensions = { "gba" } },
                    new FileDialogFilter { Name = "All Files", Extensions = { "*" } }
                }
            };

            var result = await dialog.ShowAsync(this);
            if (result != null && result.Length > 0)
            {
                StatusText.Text = $"ROM: {System.IO.Path.GetFileName(result[0])}";
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var msgBox = new Window
            {
                Title = "About",
                Width = 300,
                Height = 150,
                Content = new TextBlock
                {
                    Text = "FEBuilderGBA\nAvalonia Preview\nCopyright 2017- GPLv3",
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = global::Avalonia.Media.TextAlignment.Center
                }
            };
            await msgBox.ShowDialog(this);
        }
    }
}
