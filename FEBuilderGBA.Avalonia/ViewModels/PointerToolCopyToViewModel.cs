using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolCopyToViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _sourceAddress = string.Empty;
        string _copyMode = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SourceAddress { get => _sourceAddress; set => SetField(ref _sourceAddress, value); }
        public string CopyMode { get => _copyMode; set => SetField(ref _copyMode, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
