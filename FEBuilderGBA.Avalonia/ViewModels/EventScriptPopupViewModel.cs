using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Script command quick reference popup.</summary>
    public class EventScriptPopupViewModel : ViewModelBase, IDataVerifiable
    {
        string _infoText = "";
        bool _isLoaded;

        public string InfoText { get => _infoText; set => SetField(ref _infoText, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public void Load()
        {
            InfoText =
                "Event Script Command Reference\n" +
                "==============================\n\n" +
                "Event scripts control story progression, map events, and gameplay triggers.\n\n" +
                "Common commands:\n" +
                "  LOAD1/LOAD2  - Load unit groups onto the map\n" +
                "  MOVE         - Move a unit on the map\n" +
                "  FIGHT        - Trigger a battle between units\n" +
                "  TEXT/TEXTSHOW - Display text dialogue\n" +
                "  GOTO/CALL    - Jump to another script\n" +
                "  IFEF/IFAT    - Conditional branching\n" +
                "  MUSC/MUSI    - Play/change music\n" +
                "  CAMERA       - Move camera view\n" +
                "  FADU/FADI    - Fade screen in/out\n" +
                "  ENDA         - End event script\n\n" +
                "Use the Event Script editor to browse and edit full command definitions.\n" +
                "Command definitions are loaded from config/data/ script files.";
            IsLoaded = true;
        }

        public int GetListCount() => 1;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["InfoTextLength"] = InfoText.Length.ToString(),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            return new Dictionary<string, string>();
        }
    }
}
