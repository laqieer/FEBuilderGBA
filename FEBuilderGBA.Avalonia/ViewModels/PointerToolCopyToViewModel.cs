using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolCopyToViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _sourceAddress = string.Empty;
        string _destAddress = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SourceAddress { get => _sourceAddress; set => SetField(ref _sourceAddress, value); }
        public string DestAddress { get => _destAddress; set => SetField(ref _destAddress, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["SourceAddress"] = SourceAddress,
            ["DestAddress"] = DestAddress,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
