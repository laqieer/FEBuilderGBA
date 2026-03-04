using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolAutomaticRecoveryROMHeaderViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _recoveryStatus = "Ready to scan ROM header for issues.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string RecoveryStatus { get => _recoveryStatus; set => SetField(ref _recoveryStatus, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["RecoveryStatus"] = RecoveryStatus,
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
