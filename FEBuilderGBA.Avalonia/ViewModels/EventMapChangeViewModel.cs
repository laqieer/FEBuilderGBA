using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventMapChangeViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;

        // The detail-panel field layout. WF Designer.cs declares P8 as a
        // pointer (NumericUpDown Hexadecimal=true + InputFormRef pointer
        // semantics) — using "P8" here routes the read/write through
        // `EditorFormRef.{ReadFields, WriteFields}` which dispatches to
        // `rom.p32` / `rom.write_p32` for pointer fields, preserving the
        // GBA 0x08000000 rebase contract. Switching from the previous
        // "D8" (raw u32) wiring was Copilot CLI v1 blocking concern #1
        // on the plan review for #423.
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "P8" });

        uint _currentAddr;
        bool _isLoaded;
        uint _b0, _b1, _b2, _b3, _b4, _b5, _b6, _b7;
        uint _p8;

        // Read-config bar (mirrors WF panel1 — read-only display).
        uint _readStartAddress;
        int _readCount;
        uint _blockSize = SIZE;
        uint _selectAddress;

        // Comment (mirrors WF `InputFormRef.OnComment_TextChanged` path).
        string _comment = string.Empty;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint B2 { get => _b2; set => SetField(ref _b2, value); }
        public uint B3 { get => _b3; set => SetField(ref _b3, value); }
        public uint B4 { get => _b4; set => SetField(ref _b4, value); }
        public uint B5 { get => _b5; set => SetField(ref _b5, value); }
        public uint B6 { get => _b6; set => SetField(ref _b6, value); }
        public uint B7 { get => _b7; set => SetField(ref _b7, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public int ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint BlockSize { get => _blockSize; set => SetField(ref _blockSize, value); }
        public uint SelectAddress { get => _selectAddress; set => SetField(ref _selectAddress, value); }

        public string Comment { get => _comment; set => SetField(ref _comment, value); }

        // ----------------------------------------------------------------
        // Map navigation.
        // ----------------------------------------------------------------

        /// <summary>
        /// Enumerate every valid map in the loaded ROM. Each AddrResult
        /// carries the map's settings-record address plus a display name
        /// for the View's left-hand list. Mirrors WF
        /// <c>MapSettingForm.MakeMapIDList</c>.
        /// </summary>
        public List<AddrResult> LoadMapList()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<AddrResult>();
            return MapSettingCore.MakeMapIDList(rom);
        }

        /// <summary>
        /// Resolve the change-data address for the given map ID and load
        /// the underlying 12-byte record into the VM. Mirrors WF
        /// <c>EventMapChangeForm.MAP_LISTBOX_SelectedIndexChanged</c>.
        /// </summary>
        /// <returns>true if the load succeeded; false if the map has no
        /// change-data (plist 0/0xFF) or resolution failed.</returns>
        public bool LoadEntryForMap(uint mapId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;

            uint addr = MapChangeCore.GetMapChangeAddrWhereMapID(rom, mapId, out _);
            if (!U.isSafetyOffset(addr, rom)) return false;
            if (addr + SIZE > (uint)rom.Data.Length) return false;

            LoadEventMapChange(addr);
            return true;
        }

        // ----------------------------------------------------------------
        // List-builder kept for backward compat with the existing
        // EventMapChange_Entry_List AutomationId. The new flow drives
        // navigation via LoadMapList → LoadEntryForMap.
        // ----------------------------------------------------------------

        public List<AddrResult> LoadList() => LoadEventMapChangeList();
        public void LoadEntry(uint addr) => LoadEventMapChange(addr);

        public List<AddrResult> LoadEventMapChangeList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint mapPtr = rom.RomInfo.map_setting_pointer;
            uint mapDataSize = rom.RomInfo.map_setting_datasize;
            uint mapChangePtr = rom.RomInfo.map_mapchange_pointer;
            if (mapPtr == 0 || mapDataSize == 0 || mapChangePtr == 0)
                return new List<AddrResult>();

            uint mapBase = rom.p32(mapPtr);
            if (!U.isSafetyOffset(mapBase, rom)) return new List<AddrResult>();

            uint plistBase = rom.p32(mapChangePtr);
            if (!U.isSafetyOffset(plistBase, rom)) return new List<AddrResult>();

            // Walk maps via the Core helper; return all valid change-data
            // entries (used by the legacy EntryList path).
            var maps = MapSettingCore.MakeMapIDList(rom);
            var result = new List<AddrResult>();
            foreach (var m in maps)
            {
                uint changeAddr = MapChangeCore.GetMapChangeAddrWhereMapID(rom, m.tag, out _);
                if (!U.isSafetyOffset(changeAddr, rom)) continue;
                if (changeAddr + SIZE > (uint)rom.Data.Length) continue;
                if (rom.u8(changeAddr) == 0xFF) continue;
                result.Add(new AddrResult(changeAddr, $"Map {m.tag} Change", m.tag));
            }

            ReadStartAddress = plistBase;
            ReadCount = result.Count;
            BlockSize = SIZE;
            return result;
        }

        public void LoadEventMapChange(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            SelectAddress = addr;
            BlockSize = SIZE;

            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            B0 = values["B0"];
            B1 = values["B1"];
            B2 = values["B2"];
            B3 = values["B3"];
            B4 = values["B4"];
            B5 = values["B5"];
            B6 = values["B6"];
            B7 = values["B7"];
            P8 = values["P8"];

            RefreshComment();
            IsLoaded = true;
        }

        /// <summary>
        /// Write the current B0-B7 + P8 fields back to ROM at
        /// <see cref="CurrentAddr"/>. The caller MUST have opened an
        /// ambient <see cref="UndoService"/> scope (the View's
        /// <c>Write_Click</c> handler does this). P8 is written via
        /// <c>rom.write_p32</c>, NOT raw <c>write_u32</c>.
        /// </summary>
        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["B0"] = B0, ["B1"] = B1, ["B2"] = B2, ["B3"] = B3,
                ["B4"] = B4, ["B5"] = B5, ["B6"] = B6, ["B7"] = B7,
                ["P8"] = P8,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        // ----------------------------------------------------------------
        // Comment cache (mirrors ImageBGViewModel — keyed on CurrentAddr).
        // ----------------------------------------------------------------

        public void RefreshComment()
        {
            var cache = CoreState.CommentCache;
            if (cache == null) { Comment = string.Empty; return; }
            Comment = cache.S_At(CurrentAddr) ?? string.Empty;
        }

        public void SaveComment(string text)
        {
            Comment = text ?? string.Empty;
            var cache = CoreState.CommentCache;
            if (cache == null) return;
            if (CurrentAddr == 0) return;
            cache.Update(CurrentAddr, Comment);
        }

        public int GetListCount() => IsLoaded && CurrentAddr != 0 ? 1 : 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0"] = $"0x{B0:X02}",
                ["B1"] = $"0x{B1:X02}",
                ["B2"] = $"0x{B2:X02}",
                ["B3"] = $"0x{B3:X02}",
                ["B4"] = $"0x{B4:X02}",
                ["B5"] = $"0x{B5:X02}",
                ["B6"] = $"0x{B6:X02}",
                ["B7"] = $"0x{B7:X02}",
                ["P8"] = $"0x{P8:X08}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["B0"] = "u8@0x00",
            ["B1"] = "u8@0x01",
            ["B2"] = "u8@0x02",
            ["B3"] = "u8@0x03",
            ["B4"] = "u8@0x04",
            ["B5"] = "u8@0x05",
            ["B6"] = "u8@0x06",
            ["B7"] = "u8@0x07",
            ["P8"] = "u32@0x08",
        };
    }
}
