using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class GraphicsToolPatchMakerViewViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _statusMessage = "Graphics Patch Maker creates patches from graphics changes.\nWorkflow: Select modified graphics, compare against original ROM, generate patch file.";
        string _patchText = string.Empty;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        /// <summary>Generated patch text content for saving.</summary>
        public string PatchText { get => _patchText; set => SetField(ref _patchText, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
