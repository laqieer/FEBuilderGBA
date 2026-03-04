using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolExportEAEventViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _eventScript = string.Empty;
        string _outputPath = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string EventScript { get => _eventScript; set => SetField(ref _eventScript, value); }
        public string OutputPath { get => _outputPath; set => SetField(ref _outputPath, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["OutputPath"] = OutputPath,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
