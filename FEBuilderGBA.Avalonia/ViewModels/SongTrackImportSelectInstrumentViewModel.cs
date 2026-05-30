using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the standalone (non-modal) SongTrack instrument-set
    /// (voicegroup) browser. Populates its list from the cross-platform
    /// <see cref="InstrumentSetCore"/> — the same discovery routine WinForms
    /// <c>SongTrackImportSelectInstrumentForm.PickupInstrument()</c> uses.
    /// (#787)
    ///
    /// The WinForms MODAL pick-and-return flow (SongTrackForm → JumpFormLow →
    /// GetInstrumentAddr → SongUtil.ImportS) is intentionally NOT mirrored here
    /// — this Avalonia window is a read-only browser of the available
    /// instrument sets, not a picker wired into a MIDI/.s import.
    /// </summary>
    public class SongTrackImportSelectInstrumentViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        string _instrumentInfoText = "No instrument set selected";

        /// <summary>
        /// The song's "current" instrument-set pointer used to seed the list.
        /// For the standalone browser this defaults to 0 (no song selected);
        /// the WinForms modal path seeds it from the song being imported.
        /// </summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Formatted instrument set info for display.</summary>
        public string InstrumentInfoText { get => _instrumentInfoText; set => SetField(ref _instrumentInfoText, value); }

        /// <summary>
        /// Original (unfiltered) index of the first non-"Current" entry, or 0
        /// if the list only contains the seed. Mirrors WinForms
        /// <c>U.SelectedIndexSafety(InstrumentSelectComboBox, 1)</c> — the form
        /// defaults to the first discovered instrument set (NIMAP/NIMAP2/…) so
        /// the user lands on a usable voicegroup rather than the "Current"
        /// placeholder. (#787)
        /// </summary>
        public int DefaultSelectionIndex { get; private set; }

        /// <summary>
        /// Build the instrument-set list for the current ROM via
        /// <see cref="InstrumentSetCore.SearchInstrumentSet"/>. The list always
        /// begins with the "Current" seed, followed by every discovered
        /// instrument set (named "0x…=NatveInstrumentMap2(NIMAP2)",
        /// "0x…=AllInstrument", etc.). Mirrors WinForms <c>PickupInstrument()</c>.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            DefaultSelectionIndex = 0;

            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            List<AddrResult> iset = InstrumentSetCore.SearchInstrumentSet(rom, CurrentAddr);

            // WF PickupInstrument auto-selects index 1 (first non-Current entry)
            // when more than just the seed is present.
            if (iset.Count >= 2)
            {
                DefaultSelectionIndex = 1;
            }
            return iset;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
            InstrumentInfoText = BuildInstrumentInfo(rom, addr);
        }

        /// <summary>
        /// Build info text describing the selected instrument-set address.
        /// </summary>
        internal static string BuildInstrumentInfo(ROM rom, uint addr)
        {
            if (rom?.RomInfo == null)
                return "No ROM loaded";

            var sb = new StringBuilder();
            sb.AppendLine("ROM: " + rom.RomInfo.version);
            sb.AppendLine("Instrument set address: " + U.ToHexString(addr));
            return sb.ToString().TrimEnd();
        }
    }
}
