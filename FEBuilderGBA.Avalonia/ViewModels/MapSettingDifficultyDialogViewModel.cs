using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Dialog ViewModel for the per-map "Difficulty Settings" popup invoked
    /// from <c>MapSettingView</c>'s W20 field (parity with WinForms
    /// <c>MapSettingDifficultyForm</c>). Encodes/decodes the packed u16
    /// difficulty word via the shared <see cref="DifficultyValueCore"/>
    /// helper — same source of truth as the batch
    /// <c>MapSettingDifficultyViewModel</c>, which avoids the prior
    /// HardBoost/NormalPenalty nibble swap that existed when both call-sites
    /// did the bit shifts independently.
    /// </summary>
    public class MapSettingDifficultyDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        uint _difficultyValue;
        int _hardBoost;
        int _normalPenalty;
        int _easyPenalty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Combined difficulty value (packed from boost/penalty fields).</summary>
        public uint DifficultyValue { get => _difficultyValue; set => SetField(ref _difficultyValue, value); }
        /// <summary>Hard mode stat boost (0-15).</summary>
        public int HardBoost { get => _hardBoost; set { if (SetField(ref _hardBoost, value)) RecalculateDifficulty(); } }
        /// <summary>Normal mode stat penalty (0-15).</summary>
        public int NormalPenalty { get => _normalPenalty; set { if (SetField(ref _normalPenalty, value)) RecalculateDifficulty(); } }
        /// <summary>Easy mode stat penalty (0-15).</summary>
        public int EasyPenalty { get => _easyPenalty; set { if (SetField(ref _easyPenalty, value)) RecalculateDifficulty(); } }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        /// <summary>Load difficulty fields from a packed uint value.</summary>
        public void LoadFromValue(uint value)
        {
            _difficultyValue = value;
            var (h, n, e) = DifficultyValueCore.Unpack((ushort)(value & 0xFFFF));
            _hardBoost = h;
            _normalPenalty = n;
            _easyPenalty = e;
            OnPropertyChanged(nameof(DifficultyValue));
            OnPropertyChanged(nameof(HardBoost));
            OnPropertyChanged(nameof(NormalPenalty));
            OnPropertyChanged(nameof(EasyPenalty));
        }

        void RecalculateDifficulty()
        {
            // Preserve any reserved high-nibble bits from the original value
            // (parity with batch editor / WinForms behavior).
            ushort original = (ushort)(_difficultyValue & 0xFFFF);
            DifficultyValue = DifficultyValueCore.PackPreservingReserved(_hardBoost, _normalPenalty, _easyPenalty, original);
        }
    }
}
