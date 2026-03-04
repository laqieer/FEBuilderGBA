using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorMarSizeDialogViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        uint _width;
        uint _height;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Width { get => _width; set => SetField(ref _width, value); }
        public uint Height { get => _height; set => SetField(ref _height, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["Width"] = $"{Width}",
            ["Height"] = $"{Height}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
