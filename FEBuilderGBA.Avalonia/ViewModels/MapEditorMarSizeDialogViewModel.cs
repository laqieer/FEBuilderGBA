using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorMarSizeDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        uint _width = 1;
        string _dialogResult = "";
        bool _hasWidthError;
        string _widthErrorText = "Data size mismatch.\n(DataCount/2) % Width != 0";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Map display width in tiles.</summary>
        public uint Width { get => _width; set => SetField(ref _width, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>True when width does not divide evenly into the data size.</summary>
        public bool HasWidthError { get => _hasWidthError; set => SetField(ref _hasWidthError, value); }
        /// <summary>Error message shown when width is invalid.</summary>
        public string WidthErrorText { get => _widthErrorText; set => SetField(ref _widthErrorText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
