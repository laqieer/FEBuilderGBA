using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class UnitsShortTextViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded = true;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["info"] = "JumpTo editor - requires target address from another editor",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            return new Dictionary<string, string>();
        }
    }
}
