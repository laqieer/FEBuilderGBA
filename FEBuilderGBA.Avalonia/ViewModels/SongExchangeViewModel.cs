using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Song Exchange Tool view-model (#1002 Slice 3). Holds the CURRENT (loaded)
    /// ROM's song list + an optional OTHER (donor) ROM's song list, and drives the
    /// cross-ROM transplant via <see cref="SongExchangeCore.ConvertSong"/>. The
    /// View owns the UndoService scope (mirrors SongTrackChangeTrackView); the VM's
    /// <see cref="Convert"/> opens NO scope of its own.
    /// </summary>
    public class SongExchangeViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// True when the FE-Repo-Music submodule is checked out, so the
        /// "FE-Repo-Music" button is shown (#1383). Cached — submodule presence
        /// does not change at runtime, so the directory probe runs at most once.
        /// </summary>
        bool? _isFERepoMusicAvailable;
        public bool IsFERepoMusicAvailable
            => _isFERepoMusicAvailable ??= FERepoResourceBrowser.IsMusicRepoAvailable(
                CoreState.BaseDirectory ?? System.AppContext.BaseDirectory);

        // Parsed song lists (Core SongSt) for the current + other ROM.
        public List<SongExchangeCore.SongSt> MySongList { get; private set; } = new();
        public List<SongExchangeCore.SongSt> OtherSongList { get; private set; } = new();

        // Raw bytes + filename of the loaded OTHER (donor) ROM.
        public byte[] OtherRomData { get; private set; }
        public string OtherRomFilename { get; private set; } = "";

        // Display-string lists bound to the two ListBoxes (WF SongListToListBox format).
        public ObservableCollection<string> MySongDisplay { get; } = new();
        public ObservableCollection<string> OtherSongDisplay { get; } = new();

        /// <summary>True once an OTHER ROM has been loaded.</summary>
        public bool HasOtherRom => OtherRomData != null && OtherSongList.Count > 0;

        /// <summary>
        /// Set by the most recent successful <see cref="Convert"/> when the source
        /// instrument structure was partially corrupt (ConvertResult.HadStructureWarning).
        /// The View reads this to surface a warning instead of a plain success message
        /// (WF showed a force/warn dialog). Reset at the start of each Convert.
        /// </summary>
        public bool LastConvertHadStructureWarning { get; private set; }

        // ----- reachability + screenshot harness (kept) -----

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Song Exchange Tool", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }

        // ----- song-list loading -----

        // CURRENT (loaded) ROM: use its known sound_table_pointer.
        static uint FindSongTable(byte[] data)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || data == null) return 0;
            uint soundTablePtr = rom.RomInfo.sound_table_pointer;
            return SongExchangeCore.FindSongTablePointer(data, soundTablePtr);
        }

        // DONOR ROM: may be a DIFFERENT version, so pattern-scan its OWN sound-engine
        // signature first (version-independent), falling back to the loaded ROM's
        // sound_table_pointer address when the scan fails (donor==dest version).
        static uint FindDonorSongTable(byte[] data)
        {
            if (data == null) return 0;
            uint table = SongExchangeCore.FindSongTablePointerByScan(data);
            if (table != 0) return table;
            return FindSongTable(data);
        }

        static string FormatRow(SongExchangeCore.SongSt s)
        {
            // WF SongListToListBox: "<#> Table: 0x.. Header: 0x.. Voices: 0x.. Tracks: 0x.."
            return string.Format("{0} Table: 0x{1:X} Header: 0x{2:X} Voices: 0x{3:X} Tracks: 0x{4:X}",
                U.ToHexString(s.Number), s.Table, s.Header, s.Voices, s.Tracks);
        }

        /// <summary>Parse the CURRENT (loaded) ROM's song list and refresh the display.</summary>
        public void LoadCurrentSongs()
        {
            ROM rom = CoreState.ROM;
            MySongList = new List<SongExchangeCore.SongSt>();
            MySongDisplay.Clear();
            if (rom?.RomInfo == null) return;

            uint table = FindSongTable(rom.Data);
            if (table == 0) return;

            MySongList = SongExchangeCore.SongTableToSongList(rom.Data, table);
            foreach (var s in MySongList)
                MySongDisplay.Add(FormatRow(s));
            IsLoaded = true;
        }

        /// <summary>Load an OTHER (donor) ROM's bytes and parse its song list.</summary>
        public void LoadOtherRom(byte[] data, string filename)
        {
            OtherRomData = data;
            OtherRomFilename = filename ?? "";
            OtherSongList = new List<SongExchangeCore.SongSt>();
            OtherSongDisplay.Clear();
            if (data == null) return;

            uint table = FindDonorSongTable(data);
            if (table == 0) return;

            OtherSongList = SongExchangeCore.SongTableToSongList(data, table);
            foreach (var s in OtherSongList)
                OtherSongDisplay.Add(FormatRow(s));
        }

        /// <summary>
        /// Transplant OTHER[srcIndex] -> CURRENT[destIndex]. Opens NO undo scope
        /// (the View owns it). Returns "" on success, or a localized error string.
        /// The caller must have opened a UndoService scope and pass its active
        /// <see cref="Undo.UndoData"/>.
        /// </summary>
        public string Convert(int srcIndex, int destIndex, Undo.UndoData undo)
        {
            LastConvertHadStructureWarning = false;
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return R._("ROM is not loaded.");
            if (OtherRomData == null || OtherSongList.Count == 0)
                return R._("The other ROM has not been loaded.");
            if (srcIndex < 0 || srcIndex >= OtherSongList.Count)
                return R._("No song selected to export.");
            if (destIndex < 0 || destIndex >= MySongList.Count)
                return R._("No song selected to import.");
            if (destIndex == 0)
                return R._("Cannot write to SongID 0x0.");

            var result = SongExchangeCore.ConvertSong(rom, MySongList[destIndex],
                                                      OtherRomData, OtherSongList[srcIndex], undo);
            if (!result.Success)
                return result.ErrorMessage != "" ? result.ErrorMessage : R._("Song conversion failed.");
            // Surface a partial-corrupt source so the View can warn instead of a plain success.
            LastConvertHadStructureWarning = result.HadStructureWarning;
            return "";
        }
    }
}
