using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageViewerView : Window
    {
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
