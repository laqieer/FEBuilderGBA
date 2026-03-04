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
        }

        /// <summary>Display an IImage.</summary>
        public void ShowImage(IImage? image, string? info = null)
        {
            ImageControl.SetImage(image);
            InfoLabel.Text = info ?? "";
        }

        void ZoomBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (ZoomBox.Value.HasValue)
                ImageControl.Zoom = (int)ZoomBox.Value.Value;
        }
    }
}
