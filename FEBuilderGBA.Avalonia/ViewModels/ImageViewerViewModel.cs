namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageViewerViewModel : ViewModelBase
    {
        string _title = "Image Viewer";
        int _zoom = 2;

        public string Title { get => _title; set => SetField(ref _title, value); }
        public int Zoom { get => _zoom; set => SetField(ref _zoom, value); }
    }
}
