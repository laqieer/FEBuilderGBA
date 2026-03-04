using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class RAMRewriteToolMAPViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _address = string.Empty;
        string _value = string.Empty;
        string _mapId = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Address { get => _address; set => SetField(ref _address, value); }
        public string Value { get => _value; set => SetField(ref _value, value); }
        public string MapId { get => _mapId; set => SetField(ref _mapId, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["Address"] = Address,
            ["MapId"] = MapId,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
