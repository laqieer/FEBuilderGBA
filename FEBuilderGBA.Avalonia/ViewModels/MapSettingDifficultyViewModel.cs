using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Batch editor for the W20 packed Difficulty Settings word stored at
    /// offset 20 of every FE7/FE8 map setting struct. List on the left
    /// enumerates all maps via <see cref="MapSettingCore.MakeMapIDList()"/>;
    /// selecting a map decodes the packed word into Hard/Normal/Easy nibbles,
    /// and <see cref="Write"/> repacks and writes back through the ambient
    /// undo scope.
    ///
    /// Excluded on FE6 because the 68/72-byte map struct has a completely
    /// different layout — see <see cref="DifficultyValueCore.IsSupported"/>.
    /// </summary>
    public class MapSettingDifficultyViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        bool _isSupported;
        ushort _originalPackedValue;
        int _hardBoost;
        int _normalPenalty;
        int _easyPenalty;
        ushort _difficultyValue;
        string _formattedText = "";

        /// <summary>ROM address of the currently selected map setting entry (start of the 148-byte struct).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>True once a map entry has been loaded successfully.</summary>
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// True when the current ROM supports the W20 difficulty word
        /// (FE7/FE8 family). False for FE6 / unloaded ROMs.
        /// </summary>
        public bool IsSupported { get => _isSupported; set => SetField(ref _isSupported, value); }

        /// <summary>Hard mode stat boost (0..15).</summary>
        public int HardBoost
        {
            get => _hardBoost;
            set
            {
                int clamped = Math.Clamp(value, 0, 15);
                if (SetField(ref _hardBoost, clamped)) Recalculate();
            }
        }

        /// <summary>Normal mode stat penalty (0..15).</summary>
        public int NormalPenalty
        {
            get => _normalPenalty;
            set
            {
                int clamped = Math.Clamp(value, 0, 15);
                if (SetField(ref _normalPenalty, clamped)) Recalculate();
            }
        }

        /// <summary>Easy mode stat penalty (0..15).</summary>
        public int EasyPenalty
        {
            get => _easyPenalty;
            set
            {
                int clamped = Math.Clamp(value, 0, 15);
                if (SetField(ref _easyPenalty, clamped)) Recalculate();
            }
        }

        /// <summary>Packed u16 difficulty value (read-only derived).</summary>
        public ushort DifficultyValue { get => _difficultyValue; private set => SetField(ref _difficultyValue, value); }

        /// <summary>Pretty-printed "Hard:+H Normal:-N Easy:-E" string.</summary>
        public string FormattedText { get => _formattedText; private set => SetField(ref _formattedText, value); }

        /// <summary>
        /// Enumerate maps for the currently loaded ROM. Returns empty for
        /// FE6 (handled by <see cref="DifficultyValueCore.IsSupported"/>)
        /// and unloaded ROMs.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (!DifficultyValueCore.IsSupported(rom))
            {
                return new List<AddrResult>();
            }

            try
            {
                return MapSettingCore.MakeMapIDList();
            }
            catch
            {
                return new List<AddrResult>();
            }
        }

        /// <summary>
        /// Read the W20 packed difficulty word at <paramref name="addr"/>
        /// (the map setting struct start) and unpack into Hard/Normal/Easy.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            CurrentAddr = addr;
            IsSupported = DifficultyValueCore.IsSupported(rom);

            if (!IsSupported || rom == null)
            {
                ResetFields();
                IsLoaded = false;
                return;
            }

            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (addr + 22 > (uint)rom.Data.Length || dataSize < 22)
            {
                ResetFields();
                IsLoaded = false;
                return;
            }

            ushort packed = (ushort)rom.u16(addr + 20);
            _originalPackedValue = packed;
            var (h, n, e) = DifficultyValueCore.Unpack(packed);

            // Set backing fields directly to avoid Recalculate() racing with each setter.
            var wasLoading = IsLoading;
            IsLoading = true;
            try
            {
                _hardBoost = h;
                _normalPenalty = n;
                _easyPenalty = e;
                OnPropertyChanged(nameof(HardBoost));
                OnPropertyChanged(nameof(NormalPenalty));
                OnPropertyChanged(nameof(EasyPenalty));
                DifficultyValue = packed;
                FormattedText = DifficultyValueCore.Format(packed);
            }
            finally
            {
                IsLoading = wasLoading;
            }

            IsLoaded = true;
        }

        /// <summary>
        /// Write the current Hard/Normal/Easy nibbles back to the W20 word
        /// at <see cref="CurrentAddr"/>. No-op when the current ROM is FE6
        /// (or any other unsupported layout). Caller is expected to wrap
        /// this in an <c>UndoService</c> scope so the underlying ROM write
        /// is recorded.
        /// </summary>
        /// <returns>true if a write occurred; false if guarded out.</returns>
        public bool Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return false;
            if (!DifficultyValueCore.IsSupported(rom)) return false;

            uint dataSize = rom.RomInfo.map_setting_datasize;
            if (CurrentAddr + 22 > (uint)rom.Data.Length || dataSize < 22) return false;

            ushort packed = DifficultyValueCore.PackPreservingReserved(
                _hardBoost, _normalPenalty, _easyPenalty, _originalPackedValue);

            rom.write_u16(CurrentAddr + 20, packed);

            // Update local cache so subsequent Write calls preserve any high bits we just observed.
            _originalPackedValue = packed;
            DifficultyValue = packed;
            FormattedText = DifficultyValueCore.Format(packed);
            return true;
        }

        void Recalculate()
        {
            ushort packed = DifficultyValueCore.PackPreservingReserved(
                _hardBoost, _normalPenalty, _easyPenalty, _originalPackedValue);
            DifficultyValue = packed;
            FormattedText = DifficultyValueCore.Format(packed);
        }

        void ResetFields()
        {
            var wasLoading = IsLoading;
            IsLoading = true;
            try
            {
                _hardBoost = 0;
                _normalPenalty = 0;
                _easyPenalty = 0;
                _originalPackedValue = 0;
                DifficultyValue = 0;
                FormattedText = "";
                OnPropertyChanged(nameof(HardBoost));
                OnPropertyChanged(nameof(NormalPenalty));
                OnPropertyChanged(nameof(EasyPenalty));
            }
            finally
            {
                IsLoading = wasLoading;
            }
        }
    }
}
