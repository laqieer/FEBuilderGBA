using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ToolUpdateDialogViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // #1817: honest placeholder — this view is not an app update-checker (that is tracked in
            // #1804). Point users to the real in-app patch2 Initialize/Update in the Patch Manager /
            // Options rather than implying update functionality this stub does not have.
            result.Add(new AddrResult(0, "Patch database: use Patch Manager or Options", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }
    }
}
