using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DisASMDumpAllArgGrepViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _searchPattern = string.Empty;
        string _results = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SearchPattern { get => _searchPattern; set => SetField(ref _searchPattern, value); }
        public string Results { get => _results; set => SetField(ref _results, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["SearchPattern"] = SearchPattern,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
