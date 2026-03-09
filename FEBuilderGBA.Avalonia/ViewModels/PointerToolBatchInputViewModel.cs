using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolBatchInputViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _batchInput = string.Empty;
        string _batchOutput = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Multi-line text input with addresses to batch-convert.</summary>
        public string BatchInput { get => _batchInput; set => SetField(ref _batchInput, value); }
        /// <summary>Result of the batch address conversion.</summary>
        public string BatchOutput { get => _batchOutput; set => SetField(ref _batchOutput, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
