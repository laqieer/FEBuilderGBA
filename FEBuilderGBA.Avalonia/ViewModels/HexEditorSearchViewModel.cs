using System;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorSearchViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _searchText = string.Empty;
        bool _isReverse;
        bool _isLittleEndian;
        bool _isAlign4;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Hex byte pattern to search for (e.g. "FF ?? 00 01").</summary>
        public string SearchText { get => _searchText; set => SetField(ref _searchText, value); }
        /// <summary>Search backwards from the current position.</summary>
        public bool IsReverse { get => _isReverse; set => SetField(ref _isReverse, value); }
        /// <summary>Interpret multi-byte values as little-endian.</summary>
        public bool IsLittleEndian { get => _isLittleEndian; set => SetField(ref _isLittleEndian, value); }
        /// <summary>Only match on 4-byte aligned addresses.</summary>
        public bool IsAlign4 { get => _isAlign4; set => SetField(ref _isAlign4, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>History of previous search patterns.</summary>
        public ObservableCollection<string> SearchHistory { get; } = new();

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
