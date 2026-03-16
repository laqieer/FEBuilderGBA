using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class SongTrackImportSelectInstrumentViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        string _instrumentInfoText = "No song selected";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Formatted instrument set info for display.</summary>
        public string InstrumentInfoText { get => _instrumentInfoText; set => SetField(ref _instrumentInfoText, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Instrument Selection", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
            InstrumentInfoText = BuildInstrumentInfo(rom);
        }

        /// <summary>
        /// Build info text about the instrument set for the current ROM.
        /// </summary>
        internal static string BuildInstrumentInfo(ROM rom)
        {
            if (rom?.RomInfo == null)
                return "No ROM loaded";

            var sb = new StringBuilder();
            string version = rom.RomInfo.version.ToString();
            sb.AppendLine($"ROM: {version}");
            sb.AppendLine();
            sb.AppendLine("Instrument set selection is not yet available in Avalonia.");
            sb.AppendLine("Use the WinForms UI for full instrument set browsing.");

            return sb.ToString().TrimEnd();
        }
    }
}
