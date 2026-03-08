using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorImportImageOptionViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _selectedOption; // 0=Replace, 1=Append, 2=Insert

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int SelectedOption { get => _selectedOption; set => SetField(ref _selectedOption, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
