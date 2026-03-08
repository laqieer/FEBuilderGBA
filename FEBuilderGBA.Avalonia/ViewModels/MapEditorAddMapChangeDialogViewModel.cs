using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorAddMapChangeDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
