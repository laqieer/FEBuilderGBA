using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Character code table viewer ViewModel.</summary>
    public class TextCharCodeViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _selectedCode = "";
        ObservableCollection<string> _charCodes = new();

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedCode { get => _selectedCode; set => SetField(ref _selectedCode, value); }
        public ObservableCollection<string> CharCodes { get => _charCodes; set => SetField(ref _charCodes, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => CharCodes.Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["CharCodeCount"] = CharCodes.Count.ToString(),
                ["SelectedCode"] = SelectedCode,
            };
        }

        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
