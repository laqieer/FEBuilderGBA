using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class DecreaseColorTSAToolViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        int _method;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>
        /// Color-reduce method index — mirrors WinForms
        /// `DecreaseColorTSAToolForm.Method` combo. 0 = portrait,
        /// 1 = generic 4bpp, 2 = battle BG, etc. Persisted so the
        /// caller's `InitMethod(...)` choice survives a Window.Show()
        /// round-trip.
        /// </summary>
        public int Method { get => _method; set => SetField(ref _method, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "Color Reduction Tool", 0));
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
