using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text dictionary browser ViewModel.</summary>
    public class TextDicViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _searchTerm = "";
        string _selectedEntry = "";
        ObservableCollection<string> _entries = new();

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SearchTerm { get => _searchTerm; set => SetField(ref _searchTerm, value); }
        public string SelectedEntry { get => _selectedEntry; set => SetField(ref _selectedEntry, value); }
        public ObservableCollection<string> Entries { get => _entries; set => SetField(ref _entries, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => Entries.Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["EntryCount"] = Entries.Count.ToString(),
                ["SearchTerm"] = SearchTerm,
            };
        }

        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
