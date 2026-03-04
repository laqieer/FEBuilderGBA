using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolUndoPopupDialogViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public int GetListCount() => 0;
        public Dictionary<string, string> GetDataReport() => new();
        public Dictionary<string, string> GetRawRomReport() => new();
    }
}
