using System;
using System.IO;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolWorkSupportViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _name = "";
        string _author = "";
        string _version = "";
        string _communityUrl = "";
        string _infoText = "";
        string _autoFeedbackStatus = "";
        bool _hasUpdateInfo;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Name { get => _name; set => SetField(ref _name, value); }
        public string Author { get => _author; set => SetField(ref _author, value); }
        public string Version { get => _version; set => SetField(ref _version, value); }
        public string CommunityUrl { get => _communityUrl; set => SetField(ref _communityUrl, value); }
        public string InfoText { get => _infoText; set => SetField(ref _infoText, value); }
        public string AutoFeedbackStatus { get => _autoFeedbackStatus; set => SetField(ref _autoFeedbackStatus, value); }
        public bool HasUpdateInfo { get => _hasUpdateInfo; set => SetField(ref _hasUpdateInfo, value); }

        public void Initialize()
        {
            try
            {
                LoadUpdateInfo();
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportViewModel", ex.ToString());
            }
            IsLoaded = true;
        }

        void LoadUpdateInfo()
        {
            if (CoreState.ROM == null)
            {
                InfoText = "No ROM loaded.";
                return;
            }

            string romFilename = CoreState.ROM.Filename;
            if (string.IsNullOrEmpty(romFilename))
            {
                InfoText = "ROM filename is empty.";
                return;
            }

            string dir = Path.GetDirectoryName(romFilename) ?? "";
            string updateInfoPath = Path.Combine(dir, ".updateinfo.txt");

            if (!File.Exists(updateInfoPath))
            {
                InfoText = "No .updateinfo.txt found in ROM directory.";
                HasUpdateInfo = false;
                return;
            }

            HasUpdateInfo = true;
            InfoText = updateInfoPath;

            try
            {
                string[] lines = File.ReadAllLines(updateInfoPath);
                foreach (string line in lines)
                {
                    if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                        Name = line.Substring(5).Trim();
                    else if (line.StartsWith("author=", StringComparison.OrdinalIgnoreCase))
                        Author = line.Substring(7).Trim();
                    else if (line.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                        Version = line.Substring(8).Trim();
                    else if (line.StartsWith("community=", StringComparison.OrdinalIgnoreCase))
                        CommunityUrl = line.Substring(10).Trim();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolWorkSupportViewModel.LoadUpdateInfo", ex.ToString());
                InfoText = $"Error reading updateinfo: {ex.Message}";
            }
        }
    }
}
