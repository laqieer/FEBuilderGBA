using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MenuDefinitionViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "D4", "D8", "D12", "D16", "D20", "D24", "D28", "D32" });

        uint _currentAddr;
        bool _canWrite;
        uint _posX, _posY, _width, _height;
        uint _styleData;
        uint _menuCommandPtr;
        uint _onInitRoutine, _onEndRoutine, _unknownRoutine;
        uint _onBPressRoutine, _onRPressRoutine, _onHelpBoxRoutine;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint PosX { get => _posX; set => SetField(ref _posX, value); }
        public uint PosY { get => _posY; set => SetField(ref _posY, value); }
        public uint Width { get => _width; set => SetField(ref _width, value); }
        public uint Height { get => _height; set => SetField(ref _height, value); }
        public uint StyleData { get => _styleData; set => SetField(ref _styleData, value); }
        public uint MenuCommandPtr { get => _menuCommandPtr; set => SetField(ref _menuCommandPtr, value); }
        public uint OnInitRoutine { get => _onInitRoutine; set => SetField(ref _onInitRoutine, value); }
        public uint OnEndRoutine { get => _onEndRoutine; set => SetField(ref _onEndRoutine, value); }
        public uint UnknownRoutine { get => _unknownRoutine; set => SetField(ref _unknownRoutine, value); }
        public uint OnBPressRoutine { get => _onBPressRoutine; set => SetField(ref _onBPressRoutine, value); }
        public uint OnRPressRoutine { get => _onRPressRoutine; set => SetField(ref _onRPressRoutine, value); }
        public uint OnHelpBoxRoutine { get => _onHelpBoxRoutine; set => SetField(ref _onHelpBoxRoutine, value); }

        public List<AddrResult> LoadMenuDefinitionList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.menu_definiton_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 36);
                if (addr + 36 > (uint)rom.Data.Length) break;

                // Termination: offset+8 must be a pointer
                if (!U.isPointer(rom.u32(addr + 8))) break;

                string name = U.ToHexString(i) + " Menu Definition";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMenuDefinition(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 36 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            PosX = values["B0"];
            PosY = values["B1"];
            Width = values["B2"];
            Height = values["B3"];
            StyleData = values["D4"];
            MenuCommandPtr = values["D8"];
            OnInitRoutine = values["D12"];
            OnEndRoutine = values["D16"];
            UnknownRoutine = values["D20"];
            OnBPressRoutine = values["D24"];
            OnRPressRoutine = values["D28"];
            OnHelpBoxRoutine = values["D32"];
            CanWrite = true;
        }

        public void WriteMenuDefinition()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 36 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = PosX, ["B1"] = PosY, ["B2"] = Width, ["B3"] = Height,
                ["D4"] = StyleData, ["D8"] = MenuCommandPtr,
                ["D12"] = OnInitRoutine, ["D16"] = OnEndRoutine, ["D20"] = UnknownRoutine,
                ["D24"] = OnBPressRoutine, ["D28"] = OnRPressRoutine, ["D32"] = OnHelpBoxRoutine,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadMenuDefinitionList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["PosX"] = $"0x{PosX:X02}",
                ["PosY"] = $"0x{PosY:X02}",
                ["Width"] = $"0x{Width:X02}",
                ["Height"] = $"0x{Height:X02}",
                ["StyleData"] = $"0x{StyleData:X08}",
                ["MenuCommandPtr"] = $"0x{MenuCommandPtr:X08}",
                ["OnInitRoutine"] = $"0x{OnInitRoutine:X08}",
                ["OnEndRoutine"] = $"0x{OnEndRoutine:X08}",
                ["UnknownRoutine"] = $"0x{UnknownRoutine:X08}",
                ["OnBPressRoutine"] = $"0x{OnBPressRoutine:X08}",
                ["OnRPressRoutine"] = $"0x{OnRPressRoutine:X08}",
                ["OnHelpBoxRoutine"] = $"0x{OnHelpBoxRoutine:X08}",
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
                ["u8@0x00_PosX"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_PosY"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Width"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Height"] = $"0x{rom.u8(a + 3):X02}",
                ["u32@0x04_StyleData"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_MenuCommandPtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_OnInitRoutine"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10_OnEndRoutine"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14_UnknownRoutine"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18_OnBPressRoutine"] = $"0x{rom.u32(a + 24):X08}",
                ["u32@0x1C_OnRPressRoutine"] = $"0x{rom.u32(a + 28):X08}",
                ["u32@0x20_OnHelpBoxRoutine"] = $"0x{rom.u32(a + 32):X08}",
            };
        }
    }
}
