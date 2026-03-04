using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageViewerViewModel : ViewModelBase, IDataVerifiable
    {
        string _title = "Image Viewer";
        int _zoom = 2;

        public string Title { get => _title; set => SetField(ref _title, value); }
        public int Zoom { get => _zoom; set => SetField(ref _zoom, value); }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["Title"] = Title,
                ["Zoom"] = $"{Zoom}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            // ImageViewer is a display-only control with no ROM address
            return new Dictionary<string, string>();
        }
    }
}
