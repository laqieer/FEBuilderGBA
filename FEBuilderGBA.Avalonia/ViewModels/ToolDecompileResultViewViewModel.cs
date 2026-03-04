using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolDecompileResultViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _decompiledCode = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string DecompiledCode { get => _decompiledCode; set => SetField(ref _decompiledCode, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
