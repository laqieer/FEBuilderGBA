using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorResizeDialogViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        uint _newWidth;
        uint _newHeight;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint NewWidth { get => _newWidth; set => SetField(ref _newWidth, value); }
        public uint NewHeight { get => _newHeight; set => SetField(ref _newHeight, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["NewWidth"] = $"{NewWidth}",
            ["NewHeight"] = $"{NewHeight}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
