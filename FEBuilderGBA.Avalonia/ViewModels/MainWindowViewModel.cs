namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        bool _isRomLoaded;
        string _romVersion = "";
        string _romFilename = "";
        string _statusText = "No ROM loaded";
        string _filterText = "";
        long _romSize;
        long _estimatedFreeSpace;

        public bool IsRomLoaded
        {
            get => _isRomLoaded;
            set { SetField(ref _isRomLoaded, value); OnPropertyChanged(nameof(IsNotRomLoaded)); }
        }

        public bool IsNotRomLoaded => !_isRomLoaded;

        public string RomVersion
        {
            get => _romVersion;
            set => SetField(ref _romVersion, value);
        }

        public string RomFilename
        {
            get => _romFilename;
            set => SetField(ref _romFilename, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public string FilterText
        {
            get => _filterText;
            set => SetField(ref _filterText, value);
        }

        public long RomSize
        {
            get => _romSize;
            set => SetField(ref _romSize, value);
        }

        public long EstimatedFreeSpace
        {
            get => _estimatedFreeSpace;
            set => SetField(ref _estimatedFreeSpace, value);
        }

        public void UpdateFromRom()
        {
            if (CoreState.ROM != null)
            {
                IsRomLoaded = true;
                RomVersion = CoreState.ROM.RomInfo?.VersionToFilename ?? "Unknown";
                RomFilename = System.IO.Path.GetFileName(CoreState.ROM.Filename ?? "");
                RomSize = CoreState.ROM.Data?.Length ?? 0;
                EstimatedFreeSpace = EstimateFreeSpace(CoreState.ROM);
                StatusText = $"{RomFilename} | {RomVersion} | {RomSize:N0} bytes | Free: ~{EstimatedFreeSpace:N0} bytes";
            }
            else
            {
                IsRomLoaded = false;
                RomVersion = "";
                RomFilename = "";
                RomSize = 0;
                EstimatedFreeSpace = 0;
                StatusText = "No ROM loaded";
            }
        }

        /// <summary>
        /// Estimate free space by counting trailing 0x00 or 0xFF bytes from end of ROM.
        /// This is a rough estimate -- real free space analysis is more complex.
        /// </summary>
        static long EstimateFreeSpace(ROM rom)
        {
            if (rom?.Data == null || rom.Data.Length == 0) return 0;
            byte[] data = rom.Data;
            long count = 0;
            // Count trailing bytes that are 0x00 or 0xFF (typical padding)
            for (int i = data.Length - 1; i >= 0; i--)
            {
                if (data[i] == 0x00 || data[i] == 0xFF)
                    count++;
                else
                    break;
            }
            return count;
        }
    }
}
