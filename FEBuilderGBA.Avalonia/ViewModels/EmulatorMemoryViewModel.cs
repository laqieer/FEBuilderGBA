using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EmulatorMemoryViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _noticeText = "Emulator memory reading requires Windows P/Invoke and is not available in the cross-platform Avalonia version.\n\nThis feature uses Windows-specific APIs to read the memory of a running GBA emulator process for live debugging. Please use the Windows (WinForms) version of FEBuilderGBA for this functionality.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string NoticeText { get => _noticeText; set => SetField(ref _noticeText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["notice"] = "cross-platform-unavailable",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
