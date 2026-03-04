using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapSettingDifficultyDialogViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        uint _difficultyLevel;
        uint _enemyLevelBonus;
        bool _hardModeEnabled;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DifficultyLevel { get => _difficultyLevel; set => SetField(ref _difficultyLevel, value); }
        public uint EnemyLevelBonus { get => _enemyLevelBonus; set => SetField(ref _enemyLevelBonus, value); }
        public bool HardModeEnabled { get => _hardModeEnabled; set => SetField(ref _hardModeEnabled, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new Dictionary<string, string>
        {
            ["status"] = "loaded",
            ["DifficultyLevel"] = $"{DifficultyLevel}",
            ["EnemyLevelBonus"] = $"{EnemyLevelBonus}",
            ["HardModeEnabled"] = $"{HardModeEnabled}",
        };
        public Dictionary<string, string> GetRawRomReport() => new Dictionary<string, string>();
    }
}
