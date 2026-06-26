using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class StatusRMenuViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8", "D12", "B16", "B17", "W18", "D20", "D24" });

        uint _currentAddr;
        bool _canWrite;
        int _selectedTableIndex;
        uint _upPtr;
        uint _downPtr;
        uint _leftPtr;
        uint _rightPtr;
        uint _posX, _posY;
        uint _textId;
        uint _loopRoutine, _getterRoutine;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>
        /// Which of the up-to-6 RMenu tables is shown (0=unit, 1=game/items,
        /// 2=weapon level, 3=battle forecast 1, 4=battle forecast 2,
        /// 5=FE8 status screen). Mirrors WinForms' FilterComboBox selection.
        /// </summary>
        public int SelectedTableIndex
        {
            get => _selectedTableIndex;
            set => SetField(ref _selectedTableIndex, value);
        }
        public uint UpPtr { get => _upPtr; set => SetField(ref _upPtr, value); }
        public uint DownPtr { get => _downPtr; set => SetField(ref _downPtr, value); }
        public uint LeftPtr { get => _leftPtr; set => SetField(ref _leftPtr, value); }
        public uint RightPtr { get => _rightPtr; set => SetField(ref _rightPtr, value); }
        public uint PosX { get => _posX; set => SetField(ref _posX, value); }
        public uint PosY { get => _posY; set => SetField(ref _posY, value); }
        public uint TextId { get => _textId; set => SetField(ref _textId, value); }
        public uint LoopRoutine { get => _loopRoutine; set => SetField(ref _loopRoutine, value); }
        public uint GetterRoutine { get => _getterRoutine; set => SetField(ref _getterRoutine, value); }

        public List<AddrResult> LoadStatusRMenuList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            // #1459: build the selected RMenu table via the WF directional
            // ListFounder traversal over the six status_rmenu*_pointer roots,
            // not a single-table linear scan.
            return StatusRMenuListCore.BuildTableList(rom, SelectedTableIndex);
        }

        /// <summary>Number of selectable RMenu tables (6 on FE8, 5 otherwise).</summary>
        public int GetTableCount()
        {
            ROM rom = CoreState.ROM;
            return rom == null ? 0 : StatusRMenuListCore.TableCount(rom);
        }

        public void LoadStatusRMenu(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 28 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            UpPtr = v["D0"];
            DownPtr = v["D4"];
            LeftPtr = v["D8"];
            RightPtr = v["D12"];
            PosX = v["B16"];
            PosY = v["B17"];
            TextId = v["W18"];
            LoopRoutine = v["D20"];
            GetterRoutine = v["D24"];
            CanWrite = true;
        }

        public void WriteStatusRMenu()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (addr + 28 > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = UpPtr, ["D4"] = DownPtr, ["D8"] = LeftPtr, ["D12"] = RightPtr,
                ["B16"] = PosX, ["B17"] = PosY, ["W18"] = TextId,
                ["D20"] = LoopRoutine, ["D24"] = GetterRoutine,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadStatusRMenuList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["UpPtr"] = $"0x{UpPtr:X08}",
                ["DownPtr"] = $"0x{DownPtr:X08}",
                ["LeftPtr"] = $"0x{LeftPtr:X08}",
                ["RightPtr"] = $"0x{RightPtr:X08}",
                ["PosX"] = $"0x{PosX:X02}",
                ["PosY"] = $"0x{PosY:X02}",
                ["TextId"] = $"0x{TextId:X04}",
                ["LoopRoutine"] = $"0x{LoopRoutine:X08}",
                ["GetterRoutine"] = $"0x{GetterRoutine:X08}",
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
                ["u32@0x00_UpPtr"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_DownPtr"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_LeftPtr"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C_RightPtr"] = $"0x{rom.u32(a + 12):X08}",
                ["u8@0x10_PosX"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_PosY"] = $"0x{rom.u8(a + 17):X02}",
                ["u16@0x12_TextId"] = $"0x{rom.u16(a + 18):X04}",
                ["u32@0x14_LoopRoutine"] = $"0x{rom.u32(a + 20):X08}",
                ["u32@0x18_GetterRoutine"] = $"0x{rom.u32(a + 24):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["UpPtr"] = "u32@0x00_UpPtr",
            ["DownPtr"] = "u32@0x04_DownPtr",
            ["LeftPtr"] = "u32@0x08_LeftPtr",
            ["RightPtr"] = "u32@0x0C_RightPtr",
            ["PosX"] = "u8@0x10_PosX",
            ["PosY"] = "u8@0x11_PosY",
            ["TextId"] = "u16@0x12_TextId",
            ["LoopRoutine"] = "u32@0x14_LoopRoutine",
            ["GetterRoutine"] = "u32@0x18_GetterRoutine",
        };
    }
}
