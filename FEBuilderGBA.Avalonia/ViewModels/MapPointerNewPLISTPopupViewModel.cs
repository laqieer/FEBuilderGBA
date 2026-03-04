using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapPointerNewPLISTPopupViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        uint _plistId;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint PlistId { get => _plistId; set => SetField(ref _plistId, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["PlistId"] = $"0x{PlistId:X04}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
