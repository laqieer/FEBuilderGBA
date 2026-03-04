using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Minimal stub viewer for system hover color data.
    /// The systemhover_gradation_palette_pointer is defined only in WinForms ROMFEINFO,
    /// not in Core, so this viewer shows a placeholder message.
    /// </summary>
    public class SystemHoverColorViewerViewModel : ViewModelBase, IDataVerifiable
    {
        bool _isLoaded;
        string _statusMessage = "System hover color data is not available in the cross-platform Core library.";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public List<AddrResult> LoadHoverColorList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // The systemhover_gradation_palette_pointer property is not available in Core.
            // Return an empty list with a placeholder.
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Hover color data (WinForms only)", 0));
            return result;
        }

        public void LoadHoverColor(uint addr)
        {
            StatusMessage = "System hover color/gradation palette data requires the WinForms version.\n"
                + "This property (systemhover_gradation_palette_pointer) has not been migrated to Core.";
            IsLoaded = true;
        }

        public int GetListCount() => LoadHoverColorList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["StatusMessage"] = StatusMessage,
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            // No ROM data available for this stub viewer
            return new Dictionary<string, string>();
        }
    }
}
