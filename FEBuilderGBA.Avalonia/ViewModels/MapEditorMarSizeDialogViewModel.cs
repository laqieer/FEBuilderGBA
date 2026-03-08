using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorMarSizeDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        uint _width;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint Width { get => _width; set => SetField(ref _width, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
