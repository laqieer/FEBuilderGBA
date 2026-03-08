using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolASMEditViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _asmCode = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string AsmCode { get => _asmCode; set => SetField(ref _asmCode, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
