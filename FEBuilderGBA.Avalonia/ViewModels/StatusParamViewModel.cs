using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusParamViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _canWrite;
        uint _menuTextStruct;
        uint _bitmap;
        uint _colorType;
        uint _indent, _b10, _b11;
        uint _stringPointer;
        string _stringText = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint MenuTextStruct { get => _menuTextStruct; set => SetField(ref _menuTextStruct, value); }
        public uint Bitmap { get => _bitmap; set => SetField(ref _bitmap, value); }
        public uint ColorType { get => _colorType; set => SetField(ref _colorType, value); }
        public uint Indent { get => _indent; set => SetField(ref _indent, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }
        public uint StringPointer { get => _stringPointer; set => SetField(ref _stringPointer, value); }
        public string StringText { get => _stringText; set => SetField(ref _stringText, value); }

        public List<AddrResult> LoadStatusParamList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.status_param1_pointer;
            if (ptr == 0) return new List<AddrResult>();

            if (!U.isSafetyOffset(ptr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(ptr + i * 16);
                if (addr + 16 > (uint)rom.Data.Length) break;

                // Validate: offset +12 should be a pointer
                uint strPtr = rom.u32(addr + 12);
                if (!U.isPointer(strPtr)) break;

                string name = U.ToHexString(i) + " Status Param";
                // Try to resolve name from pointer
                try
                {
                    uint strAddr = U.toOffset(strPtr);
                    if (U.isSafetyOffset(strAddr))
                    {
                        string resolved = rom.getString(strAddr, 32);
                        if (!string.IsNullOrEmpty(resolved))
                            name = U.ToHexString(i) + " " + resolved;
                    }
                }
                catch { }

                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadStatusParam(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 16 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            MenuTextStruct = rom.u32(addr + 0);
            Bitmap = rom.u32(addr + 4);
            ColorType = rom.u8(addr + 8);
            Indent = rom.u8(addr + 9);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);
            StringPointer = rom.u32(addr + 12);

            // Resolve string text
            StringText = "";
            try
            {
                if (U.isPointer(StringPointer))
                {
                    uint strAddr = U.toOffset(StringPointer);
                    if (U.isSafetyOffset(strAddr))
                        StringText = rom.getString(strAddr, 32);
                }
            }
            catch { }

            CanWrite = true;
        }

        public void WriteStatusParam()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 16 > (uint)rom.Data.Length) return;

            rom.write_u32(addr + 0, MenuTextStruct);
            rom.write_u32(addr + 4, Bitmap);
            rom.write_u8(addr + 8, (byte)ColorType);
            rom.write_u8(addr + 9, (byte)Indent);
            rom.write_u8(addr + 10, (byte)B10);
            rom.write_u8(addr + 11, (byte)B11);
            rom.write_u32(addr + 12, StringPointer);
        }

        public int GetListCount() => LoadStatusParamList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["MenuTextStruct"] = $"0x{MenuTextStruct:X08}",
                ["Bitmap"] = $"0x{Bitmap:X08}",
                ["ColorType"] = $"0x{ColorType:X02}",
                ["Indent"] = $"0x{Indent:X02}",
                ["StringPointer"] = $"0x{StringPointer:X08}",
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
                ["u32@0x00_MenuTextStruct"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_Bitmap"] = $"0x{rom.u32(a + 4):X08}",
                ["u8@0x08_ColorType"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09_Indent"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A_B10"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B_B11"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@0x0C_StringPointer"] = $"0x{rom.u32(a + 12):X08}",
            };
        }
    }
}
