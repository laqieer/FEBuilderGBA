using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolBatchInputViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _batchInput = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string BatchInput { get => _batchInput; set => SetField(ref _batchInput, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
