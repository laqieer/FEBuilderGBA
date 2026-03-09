using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageViewerView : Window, IDataVerifiableView
    {
        readonly ImageViewerViewModel _vm = new();

        public ViewModelBase? DataViewModel => _vm;

        public ImageViewerView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
        }

        /// <summary>Display an IImage.</summary>
        public void ShowImage(IImage? image, string? info = null)
        {
            ImageControl.SetImage(image);
            _vm.ImageInfo = info ?? "";
        }

        async void ExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await ImageControl.ExportPng(this, "image.png");
        }

        void ZoomBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (ZoomBox.Value.HasValue)
            {
                int zoom = (int)ZoomBox.Value.Value;
                _vm.Zoom = zoom;
                ImageControl.Zoom = zoom;
            }
        }
    }
}
