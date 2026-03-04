using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorAddMapChangeDialogViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        uint _mapChangeId;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint MapChangeId { get => _mapChangeId; set => SetField(ref _mapChangeId, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["MapChangeId"] = $"0x{MapChangeId:X04}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
