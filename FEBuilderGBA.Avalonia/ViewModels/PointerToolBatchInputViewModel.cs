using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolBatchInputViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _batchInput = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string BatchInput { get => _batchInput; set => SetField(ref _batchInput, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["BatchInput"] = BatchInput,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
