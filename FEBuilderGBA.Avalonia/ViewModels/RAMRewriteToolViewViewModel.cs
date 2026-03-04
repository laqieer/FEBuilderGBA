using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class RAMRewriteToolViewViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _noticeText = "RAM rewriting requires Windows P/Invoke to access emulator process memory and is not available in the cross-platform Avalonia version.\n\nPlease use the Windows (WinForms) version of FEBuilderGBA for this functionality.";
        string _address = string.Empty;
        string _value = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string NoticeText { get => _noticeText; set => SetField(ref _noticeText, value); }
        public string Address { get => _address; set => SetField(ref _address, value); }
        public string Value { get => _value; set => SetField(ref _value, value); }

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
