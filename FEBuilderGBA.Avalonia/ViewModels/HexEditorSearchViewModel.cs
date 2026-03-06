using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorSearchViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _searchText = string.Empty;
        bool _isReverse;
        bool _isLittleEndian;
        bool _isAlign4;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SearchText { get => _searchText; set => SetField(ref _searchText, value); }
        public bool IsReverse { get => _isReverse; set => SetField(ref _isReverse, value); }
        public bool IsLittleEndian { get => _isLittleEndian; set => SetField(ref _isLittleEndian, value); }
        public bool IsAlign4 { get => _isAlign4; set => SetField(ref _isAlign4, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["SearchText"] = SearchText,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
