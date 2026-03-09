namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageViewerViewModel : ViewModelBase
    {
        string _title = "Image Viewer";
        int _zoom = 2;
        bool _isLoaded;
        int _imageWidth;
        int _imageHeight;
        string _imageInfo = string.Empty;

        /// <summary>Window title.</summary>
        public string Title { get => _title; set => SetField(ref _title, value); }
        /// <summary>Zoom level for the image display (1x, 2x, 4x, etc.).</summary>
        public int Zoom { get => _zoom; set => SetField(ref _zoom, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Width of the loaded image in pixels.</summary>
        public int ImageWidth { get => _imageWidth; set => SetField(ref _imageWidth, value); }
        /// <summary>Height of the loaded image in pixels.</summary>
        public int ImageHeight { get => _imageHeight; set => SetField(ref _imageHeight, value); }
        /// <summary>Descriptive info about the image (size, format, etc.).</summary>
        public string ImageInfo { get => _imageInfo; set => SetField(ref _imageInfo, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
