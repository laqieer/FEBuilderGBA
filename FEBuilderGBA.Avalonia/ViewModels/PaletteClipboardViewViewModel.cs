using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PaletteClipboardViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _statusMessage = "Palette Clipboard Manager stores and retrieves palette data.\nCopy palettes between different graphics entries or save them for later use.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string> { ["status"] = "loaded" };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
