using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorAppendPopupViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        bool _confirmed;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool Confirmed { get => _confirmed; set => SetField(ref _confirmed, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["Confirmed"] = $"{Confirmed}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
