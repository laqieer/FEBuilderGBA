using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EventMapChangeViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;

        // ----------------------------------------------------------------
        // Preview state (issue #857, NV6-PR2).
        // ----------------------------------------------------------------
        bool _canExportChange;
        /// <summary>
        /// True when a change-map preview image has been successfully rendered.
        /// Gates the read-only "Export PNG" button in the View.
        /// </summary>
        public bool CanExportChange { get => _canExportChange; set => SetField(ref _canExportChange, value); }

        // Currently selected map ID — stored so RenderChangePreview can resolve
        // the map_setting fields (obj_plist, palette_plist, config_plist).
        uint _currentMapId = uint.MaxValue;

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
        ///
        /// When the map has no change-data (plist 0/0xFF) or resolution
        /// fails, this method calls <see cref="ClearEntry"/> to reset
        /// the VM state — preventing a subsequent <see cref="WriteEntry"/>
        /// from writing zeros to the previously selected entry's address
        /// (Copilot CLI re-review on issue #423).
        /// </summary>
        /// <returns>true if the load succeeded; false if the map has no
        /// change-data (plist 0/0xFF) or resolution failed.</returns>
        public bool LoadEntryForMap(uint mapId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) { ClearEntry(); return false; }

            uint addr = MapChangeCore.GetMapChangeAddrWhereMapID(rom, mapId, out _);
            if (!U.isSafetyOffset(addr, rom)) { ClearEntry(); return false; }
            if (addr + SIZE > (uint)rom.Data.Length) { ClearEntry(); return false; }

            _currentMapId = mapId;
            LoadEventMapChange(addr);
            return true;
        }

        /// <summary>
        /// Reset the VM to the "no entry loaded" state. Crucially clears
        /// <see cref="CurrentAddr"/> + <see cref="IsLoaded"/> so a stray
        /// <see cref="WriteEntry"/> call after a failed
        /// <see cref="LoadEntryForMap"/> short-circuits via the
        /// <c>CurrentAddr == 0</c> guard. Also resets the read-config
        /// surface (<see cref="ReadStartAddress"/>, <see cref="ReadCount"/>,
        /// <see cref="BlockSize"/>) so any UI bound to those properties
        /// reflects "no entry loaded" consistently (Copilot bot 3rd-pass
        /// review on issue #423). The View should mirror this reset by
        /// clearing its detail controls when LoadEntryForMap returns
        /// false.
        /// </summary>
        public void ClearEntry()
        {
            CurrentAddr = 0;
            SelectAddress = 0;
            IsLoaded = false;
            B0 = 0;
            B1 = 0;
            B2 = 0;
            B3 = 0;
            B4 = 0;
            B5 = 0;
            B6 = 0;
            B7 = 0;
            P8 = 0;
            Comment = string.Empty;
            // Reset the read-config surface so the View doesn't display
            // the previous selection's ReadStartAddress / ReadCount.
            ReadStartAddress = 0;
            ReadCount = 0;
            BlockSize = SIZE;
            // Clear preview state (#857, NV6-PR2).
            _currentMapId = uint.MaxValue;
            CanExportChange = false;
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

            // Note: ReadStartAddress / ReadCount are NOT set here. WF
            // sets them per-selected-map (in `MAP_LISTBOX_SelectedIndexChanged`
            // and `InputFormRef.ReInit(addr)`). The Avalonia per-map values
            // are set in `LoadEventMapChange` instead — see Copilot bot
            // review on issue #423.
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

            // WF `InputFormRef.ReInit(addr)` re-bases the read-config bar
            // on the SELECTED change-data address and counts records
            // forward from there until the 0xFF terminator. Mirror that
            // here so `ReadStartAddress` / `ReadCount` reflect the
            // currently loaded entry rather than the global plist table
            // (Copilot bot review on issue #423).
            ReadStartAddress = addr;
            ReadCount = CountChangeRecordsFrom(rom, addr);

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
        /// Count map-change records starting at <paramref name="startAddr"/>
        /// until the 0xFF terminator byte. Each record is <see cref="SIZE"/>
        /// (12) bytes. Capped at 256 to mirror WF's plist-byte upper bound.
        /// </summary>
        static int CountChangeRecordsFrom(ROM rom, uint startAddr)
        {
            if (rom == null) return 0;
            uint romLen = (uint)rom.Data.Length;
            int count = 0;
            for (uint i = 0; i < 256; i++)
            {
                uint a = startAddr + i * SIZE;
                if (a + SIZE > romLen) break;
                if (rom.u8(a) == 0xFF) break;
                count++;
            }
            return count;
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

        // ----------------------------------------------------------------
        // Change-map overlay preview (#857, NV6-PR2).
        // ----------------------------------------------------------------

        /// <summary>
        /// Render the change-map overlay preview for the currently loaded
        /// change record and currently selected map. Returns the rendered
        /// <see cref="IImage"/> (may be <c>null</c> on any failure), and
        /// updates <see cref="CanExportChange"/> accordingly.
        ///
        /// <para>Null-safe: returns <c>null</c> and clears
        /// <see cref="CanExportChange"/> when no ROM, no entry, no valid
        /// map_setting, or the Core render fails.</para>
        /// </summary>
        public IImage RenderChangePreview()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom == null || rom.RomInfo == null || !IsLoaded || CurrentAddr == 0
                    || _currentMapId == uint.MaxValue)
                {
                    CanExportChange = false;
                    return null;
                }

                // Resolve map_setting address for the selected map.
                uint mapSettingAddr = MapSettingCore.GetMapAddr(rom, _currentMapId);
                if (!U.isSafetyOffset(mapSettingAddr, rom))
                {
                    CanExportChange = false;
                    return null;
                }
                // Verify the map setting has the required fields at the expected offsets.
                // map_setting layout (verified in plan review):
                //   +4 (u16): obj_plist   — OBJ tileset PLIST index (take low byte: &0xFF)
                //   +6 (u8):  palette_plist
                //   +7 (u8):  config_plist
                if (mapSettingAddr + 8u > (uint)rom.Data.Length)
                {
                    CanExportChange = false;
                    return null;
                }
                uint objPlistRaw    = rom.u16(mapSettingAddr + 4);
                uint palettePlist   = rom.u8(mapSettingAddr + 6);
                uint configPlist    = rom.u8(mapSettingAddr + 7);

                // The high byte of obj_plist is the FE7 obj2 index (DEFERRED, MR4).
                // Use only the low byte for the primary OBJ tileset.
                uint objPlist = objPlistRaw & 0xFFu;

                // Resolve each plist to a ROM data offset.
                uint objOffset     = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.OBJECT,  objPlist,     out _);
                uint paletteOffset = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.PALETTE, palettePlist, out _);
                uint configOffset  = MapChangeCore.PlistToOffsetAddr(rom, MapChangeCore.PlistType.CONFIG,  configPlist,  out _);

                if (objOffset == U.NOT_FOUND || paletteOffset == U.NOT_FOUND || configOffset == U.NOT_FOUND)
                {
                    CanExportChange = false;
                    return null;
                }

                // P8 is read via EditorFormRef.ReadFields → rom.p32, which already calls
                // U.toOffset internally and returns a ROM offset.  The U.toOffset call here
                // is an idempotent safety-normalize: it is a no-op on a valid ROM offset
                // (< 0x08000000), but correctly converts a raw GBA pointer (≥ 0x08000000)
                // in case the field was hand-edited to a raw pointer value before saving.
                uint changeDataOffset = U.toOffset(P8);
                if (!U.isSafetyOffset(changeDataOffset, rom))
                {
                    CanExportChange = false;
                    return null;
                }

                // Width (B3) and height (B4) from the loaded change record.
                int width  = (int)B3;
                int height = (int)B4;

                IImage img = MapRenderCore.RenderChangeMap(
                    rom, objOffset, paletteOffset, configOffset,
                    changeDataOffset, width, height);

                CanExportChange = img != null;
                return img;
            }
            catch (Exception ex)
            {
                Log.Error($"EventMapChangeViewModel.RenderChangePreview failed: {ex}");
                CanExportChange = false;
                return null;
            }
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
