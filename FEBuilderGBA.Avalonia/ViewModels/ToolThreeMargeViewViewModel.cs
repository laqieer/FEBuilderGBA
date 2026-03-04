using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolThreeMargeViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _baseText = string.Empty;
        string _mineText = string.Empty;
        string _theirsText = string.Empty;
        string _resultText = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string BaseText { get => _baseText; set => SetField(ref _baseText, value); }
        public string MineText { get => _mineText; set => SetField(ref _mineText, value); }
        public string TheirsText { get => _theirsText; set => SetField(ref _theirsText, value); }
        public string ResultText { get => _resultText; set => SetField(ref _resultText, value); }

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
