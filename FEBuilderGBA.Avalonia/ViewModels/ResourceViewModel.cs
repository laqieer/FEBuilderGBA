namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ResourceViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _romInfoText = "";
        string _configInfoText = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string RomInfoText { get => _romInfoText; set => SetField(ref _romInfoText, value); }
        public string ConfigInfoText { get => _configInfoText; set => SetField(ref _configInfoText, value); }

        public void Initialize()
        {
            UpdateRomInfo();
            UpdateConfigInfo();
            IsLoaded = true;
        }

        void UpdateRomInfo()
        {
            var rom = CoreState.ROM;
            if (rom == null)
            {
                RomInfoText = "No ROM loaded.";
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"File: {rom.Filename ?? "(none)"}");
            sb.AppendLine($"Version: {rom.RomInfo?.VersionToFilename ?? "Unknown"}");
            sb.AppendLine($"Size: {rom.Data.Length:N0} bytes (0x{rom.Data.Length:X})");

            // Estimate free space (trailing 0x00/0xFF bytes)
            long free = 0;
            for (int i = rom.Data.Length - 1; i >= 0; i--)
            {
                if (rom.Data[i] == 0x00 || rom.Data[i] == 0xFF)
                    free++;
                else
                    break;
            }
            sb.AppendLine($"Estimated Free Space: ~{free:N0} bytes ({(double)free / rom.Data.Length:P1})");

            RomInfoText = sb.ToString().TrimEnd();
        }

        void UpdateConfigInfo()
        {
            var sb = new System.Text.StringBuilder();
            string configDir = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "config");
            sb.AppendLine($"Config Directory: {configDir}");
            sb.AppendLine($"Config Exists: {System.IO.Directory.Exists(configDir)}");

            if (CoreState.ROM?.RomInfo != null)
            {
                string patchDir = System.IO.Path.Combine(configDir, "patch2", CoreState.ROM.RomInfo.VersionToFilename);
                sb.AppendLine($"Patch Directory: {patchDir}");
                sb.AppendLine($"Patches Available: {(System.IO.Directory.Exists(patchDir) ? "Yes" : "No")}");
            }

            ConfigInfoText = sb.ToString().TrimEnd();
        }
    }
}
