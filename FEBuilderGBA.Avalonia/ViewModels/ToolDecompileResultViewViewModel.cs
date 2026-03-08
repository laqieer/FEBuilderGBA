using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolDecompileResultViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _decompiledCode = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string DecompiledCode { get => _decompiledCode; set => SetField(ref _decompiledCode, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
