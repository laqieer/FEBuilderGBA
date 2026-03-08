using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DisASMDumpAllArgGrepViewModel : ViewModelBase
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
    }
}
