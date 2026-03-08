using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class HexEditorJumpViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _address = string.Empty;
        bool _isLittleEndian;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Address { get => _address; set => SetField(ref _address, value); }
        public bool IsLittleEndian { get => _isLittleEndian; set => SetField(ref _isLittleEndian, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
