using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorImportImageOptionViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _selectedOption; // 0=Replace, 1=Append, 2=Insert
        string _dialogResult = "";
        string _instructionText = "Select how the imported image should be applied to the map tileset.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Import option: 0=Replace existing tiles, 1=Append new tiles, 2=Insert at position.</summary>
        public int SelectedOption { get => _selectedOption; set => SetField(ref _selectedOption, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>Instruction text describing the import options.</summary>
        public string InstructionText { get => _instructionText; set => SetField(ref _instructionText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
