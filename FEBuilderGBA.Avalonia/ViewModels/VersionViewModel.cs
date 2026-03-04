using System;
using System.Collections.Generic;
using System.Reflection;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class VersionViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _versionText = "";
        string _buildDate = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string VersionText { get => _versionText; set => SetField(ref _versionText, value); }
        public string BuildDate { get => _buildDate; set => SetField(ref _buildDate, value); }

        public void Initialize()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                VersionText = asm.GetName().Version?.ToString() ?? "Unknown";
                BuildDate = System.IO.File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd");
            }
            catch { VersionText = "Unknown"; BuildDate = "Unknown"; }
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new() { ["version"] = VersionText };
        public Dictionary<string, string> GetRawRomReport() => new();
    }
}
