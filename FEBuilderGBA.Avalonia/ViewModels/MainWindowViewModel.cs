namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        bool _isRomLoaded;
        string _romVersion = "";
        string _romFilename = "";
        string _statusText = "No ROM loaded";
        long _romSize;

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

        public long RomSize
        {
            get => _romSize;
            set => SetField(ref _romSize, value);
        }

        public void UpdateFromRom()
        {
            if (CoreState.ROM != null)
            {
                IsRomLoaded = true;
                RomVersion = CoreState.ROM.RomInfo?.VersionToFilename ?? "Unknown";
                RomFilename = System.IO.Path.GetFileName(CoreState.ROM.Filename ?? "");
                RomSize = CoreState.ROM.Data?.Length ?? 0;
                StatusText = $"{RomFilename} | {RomVersion} | {RomSize:N0} bytes";
            }
            else
            {
                IsRomLoaded = false;
                RomVersion = "";
                RomFilename = "";
                RomSize = 0;
                StatusText = "No ROM loaded";
            }
        }
    }
}
