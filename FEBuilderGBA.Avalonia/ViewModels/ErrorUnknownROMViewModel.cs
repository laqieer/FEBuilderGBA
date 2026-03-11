using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ErrorUnknownROMViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _selectedVersion = "NAZO";
        string _versionInfoText = "ROM version info:";
        string _detailMessage = "This ROM does not have a recognized version signature.\nIs this a valid GBA Fire Emblem ROM?\nIf so, please select the correct version below.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedVersion { get => _selectedVersion; set => SetField(ref _selectedVersion, value); }
        public string VersionInfoText { get => _versionInfoText; set => SetField(ref _versionInfoText, value); }
        public string DetailMessage { get => _detailMessage; set => SetField(ref _detailMessage, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void Init(string version)
        {
            VersionInfoText = "ROM version info: " + version;
        }

        /// <summary>
        /// Initialize with ROM file details for better diagnostics.
        /// </summary>
        public void InitFromFile(string filePath, byte[]? data)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ROM version info: Unknown");
            if (!string.IsNullOrEmpty(filePath))
                sb.AppendLine($"File: {System.IO.Path.GetFileName(filePath)}");
            if (data != null)
            {
                sb.AppendLine($"Size: {data.Length:N0} bytes (0x{data.Length:X})");
                // Show first 16 header bytes
                int headerLen = Math.Min(16, data.Length);
                var headerHex = new System.Text.StringBuilder();
                for (int i = 0; i < headerLen; i++)
                    headerHex.Append($"{data[i]:X2} ");
                sb.AppendLine($"Header: {headerHex.ToString().Trim()}");

                // Try to extract game title from GBA header (offset 0xA0, 12 bytes)
                if (data.Length >= 0xAC)
                {
                    string title = System.Text.Encoding.ASCII.GetString(data, 0xA0, 12).Trim('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(title))
                        sb.AppendLine($"Game Title: {title}");
                }
            }
            VersionInfoText = sb.ToString().TrimEnd();
        }
    }
}
