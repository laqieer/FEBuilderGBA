using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuCommandViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _usabilityPtr;
        uint _effectPtr;
        uint _menuCommandId;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint UsabilityPtr { get => _usabilityPtr; set => SetField(ref _usabilityPtr, value); }
        public uint EffectPtr { get => _effectPtr; set => SetField(ref _effectPtr, value); }
        public uint MenuCommandId { get => _menuCommandId; set => SetField(ref _menuCommandId, value); }

        public List<AddrResult> LoadMenuCommandList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // MenuCommand entries are accessed from MenuDefinition handler pointers.
            // List the well-known MenuCommand function addresses first.
            var result = new List<AddrResult>();

            uint always = rom.RomInfo.MenuCommand_UsabilityAlways;
            if (always != 0)
                result.Add(new AddrResult(always, "0 UsabilityAlways", 0));

            uint never = rom.RomInfo.MenuCommand_UsabilityNever;
            if (never != 0)
                result.Add(new AddrResult(never, "1 UsabilityNever", 1));

            // Also enumerate entries from the primary menu definition table
            uint ptr = rom.RomInfo.menu_definiton_pointer;
            if (ptr != 0)
            {
                uint defBase = rom.p32(ptr);
                if (U.isSafetyOffset(defBase))
                {
                    uint idx = 2;
                    for (uint i = 0; i < 0x100; i++)
                    {
                        uint defAddr = (uint)(defBase + i * 36);
                        if (defAddr + 36 > (uint)rom.Data.Length) break;
                        if (!U.isPointer(rom.u32(defAddr + 8))) break;

                        uint menuCmdPtr = rom.p32(defAddr + 8);
                        if (!U.isSafetyOffset(menuCmdPtr)) continue;

                        // Each menu command entry is 36 bytes, check for pointer at +0xC
                        for (uint j = 0; j < 0x40; j++)
                        {
                            uint cmdAddr = (uint)(menuCmdPtr + j * 36);
                            if (cmdAddr + 36 > (uint)rom.Data.Length) break;
                            if (!U.isPointer(rom.u32(cmdAddr + 0xC))) break;

                            string name = U.ToHexString(idx) + " MenuCmd Def" + i.ToString() + "_" + j.ToString();
                            result.Add(new AddrResult(cmdAddr, name, idx));
                            idx++;
                        }
                    }
                }
            }

            return result;
        }

        public void LoadMenuCommand(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            UsabilityPtr = rom.u32(addr + 0xC);
            EffectPtr = rom.u32(addr + 0x10);
            MenuCommandId = rom.u16(addr + 0x12);
            IsLoaded = true;
        }
    }
}
