using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuCommandViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _jpNamePointer;
        uint _nameTextId, _helpTextId;
        uint _colorType, _menuCommandId, _b10, _b11;
        uint _colorAndIdDword;
        uint _usabilityRoutine, _drawRoutine, _effectRoutine;
        uint _perTurnCallback, _cursorSelectAction, _cancelAction;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint JpNamePointer { get => _jpNamePointer; set => SetField(ref _jpNamePointer, value); }
        public uint NameTextId { get => _nameTextId; set => SetField(ref _nameTextId, value); }
        public uint HelpTextId { get => _helpTextId; set => SetField(ref _helpTextId, value); }
        public uint ColorType { get => _colorType; set => SetField(ref _colorType, value); }
        public uint MenuCommandId { get => _menuCommandId; set => SetField(ref _menuCommandId, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint ColorAndIdDword { get => _colorAndIdDword; set => SetField(ref _colorAndIdDword, value); }
        public uint UsabilityRoutine { get => _usabilityRoutine; set => SetField(ref _usabilityRoutine, value); }
        public uint DrawRoutine { get => _drawRoutine; set => SetField(ref _drawRoutine, value); }
        public uint EffectRoutine { get => _effectRoutine; set => SetField(ref _effectRoutine, value); }
        public uint PerTurnCallback { get => _perTurnCallback; set => SetField(ref _perTurnCallback, value); }
        public uint CursorSelectAction { get => _cursorSelectAction; set => SetField(ref _cursorSelectAction, value); }
        public uint CancelAction { get => _cancelAction; set => SetField(ref _cancelAction, value); }

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
            if (addr + 36 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            JpNamePointer = rom.u32(addr + 0);
            NameTextId = rom.u16(addr + 4);
            HelpTextId = rom.u16(addr + 6);
            ColorType = rom.u8(addr + 8);
            MenuCommandId = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            ColorAndIdDword = rom.u32(addr + 8);
            UsabilityRoutine = rom.u32(addr + 12);
            DrawRoutine = rom.u32(addr + 16);
            EffectRoutine = rom.u32(addr + 20);
            PerTurnCallback = rom.u32(addr + 24);
            CursorSelectAction = rom.u32(addr + 28);
            CancelAction = rom.u32(addr + 32);
            CanWrite = true;
        }

        public void WriteMenuCommand()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 36 > (uint)rom.Data.Length) return;

            rom.write_u32(addr + 0, JpNamePointer);
            rom.write_u16(addr + 4, (ushort)NameTextId);
            rom.write_u16(addr + 6, (ushort)HelpTextId);
            rom.write_u32(addr + 8, ColorAndIdDword);
            rom.write_u32(addr + 12, UsabilityRoutine);
            rom.write_u32(addr + 16, DrawRoutine);
            rom.write_u32(addr + 20, EffectRoutine);
            rom.write_u32(addr + 24, PerTurnCallback);
            rom.write_u32(addr + 28, CursorSelectAction);
            rom.write_u32(addr + 32, CancelAction);
        }

        public int GetListCount() => LoadMenuCommandList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["JpNamePointer"] = $"0x{JpNamePointer:X08}",
                ["NameTextId"] = $"0x{NameTextId:X04}",
                ["HelpTextId"] = $"0x{HelpTextId:X04}",
                ["ColorType"] = $"0x{ColorType:X02}",
                ["MenuCommandId"] = $"0x{MenuCommandId:X02}",
                ["UsabilityRoutine"] = $"0x{UsabilityRoutine:X08}",
                ["DrawRoutine"] = $"0x{DrawRoutine:X08}",
                ["EffectRoutine"] = $"0x{EffectRoutine:X08}",
                ["PerTurnCallback"] = $"0x{PerTurnCallback:X08}",
                ["CursorSelectAction"] = $"0x{CursorSelectAction:X08}",
                ["CancelAction"] = $"0x{CancelAction:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00_JpName"] = $"0x{rom.u32(a + 0):X08}",
                ["u16@0x04_NameTextId"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06_HelpTextId"] = $"0x{rom.u16(a + 6):X04}",
                ["u8@0x08_ColorType"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09_MenuCommandId"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_B10"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_B11"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x08_ColorAndIdDword"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_UsabilityRoutine"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10_DrawRoutine"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14_EffectRoutine"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18_PerTurnCallback"] = $"0x{rom.u32(a + 24):X08}",
                ["u32@0x1C_CursorSelectAction"] = $"0x{rom.u32(a + 28):X08}",
                ["u32@0x20_CancelAction"] = $"0x{rom.u32(a + 32):X08}",
            };
        }
    }
}
