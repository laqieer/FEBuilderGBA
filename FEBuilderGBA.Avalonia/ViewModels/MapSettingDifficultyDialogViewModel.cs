using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapSettingDifficultyDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        uint _difficultyValue;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint DifficultyValue { get => _difficultyValue; set => SetField(ref _difficultyValue, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
