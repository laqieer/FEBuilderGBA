using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorImportImageOptionViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        int _selectedOption; // 0=Replace, 1=Append, 2=Insert

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int SelectedOption { get => _selectedOption; set => SetField(ref _selectedOption, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["SelectedOption"] = $"{SelectedOption}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
