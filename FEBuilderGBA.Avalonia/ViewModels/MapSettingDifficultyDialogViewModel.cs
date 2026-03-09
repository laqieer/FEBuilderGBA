using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
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
            _hardBoost = (int)((value >> 8) & 0xF);
            _normalPenalty = (int)((value >> 4) & 0xF);
            _easyPenalty = (int)(value & 0xF);
            OnPropertyChanged(nameof(DifficultyValue));
            OnPropertyChanged(nameof(HardBoost));
            OnPropertyChanged(nameof(NormalPenalty));
            OnPropertyChanged(nameof(EasyPenalty));
        }

        void RecalculateDifficulty()
        {
            DifficultyValue = (uint)((_hardBoost & 0xF) << 8 | (_normalPenalty & 0xF) << 4 | (_easyPenalty & 0xF));
        }
    }
}
