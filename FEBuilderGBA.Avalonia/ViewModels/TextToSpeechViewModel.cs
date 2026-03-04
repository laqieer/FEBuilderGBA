using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text-to-speech tool ViewModel.</summary>
    public class TextToSpeechViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _inputText = "";
        string _status = "Ready";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string InputText { get => _inputText; set => SetField(ref _inputText, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["InputTextLength"] = InputText.Length.ToString(),
                ["Status"] = Status,
            };
        }

        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
