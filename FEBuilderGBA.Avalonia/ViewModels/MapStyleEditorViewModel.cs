using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        uint _objPointer;
        uint _configPointer;
        uint _paletteBaseAddress;
        uint _paletteAddress;
        uint _objAddress;
        uint _objAddress2;
        uint _chipsetConfigAddress;
        int _paletteIndex;
        bool _isFogPalette;
        string _configNo = "";

        // Chip-preview render caches (#670). Populated at the end of LoadEntry,
        // cleared at the start of every load (and on every early-return path).
        // _cachedObjData = LZ77-decompressed primary OBJ tileset.
        // _cachedPaletteBytes = full 16-palette block (16 * 16 colors * 2 bytes
        // = 512 bytes) read from PaletteBaseAddress.
        byte[] _cachedObjData;
        byte[] _cachedPaletteBytes;

        // Chipset tab state (#671). _cachedConfigData = the LZ77-decompressed
        // CONFIG buffer for the currently-loaded map style (used as the
        // staging source for chipset edits + the destination for terrain
        // reads). _currentConfigPlist = the PLIST byte resolved from the
        // matching map_setting entry's +7 field; explicitly retained so
        // WriteChipsetConfig can re-resolve the slot without re-running the
        // map-setting scan.
        byte[] _cachedConfigData;
        uint _currentConfigPlist;
        // OBJ Image Import (#710): plist byte for the primary OBJ tileset.
        // Resolved from the entry index inside the map_obj_pointer table at
        // LoadEntry time so TryImportObjImage can rewrite the OBJECT PLIST
        // slot without re-walking the map_setting list. 0 / >= 0xFF mean
        // "no valid OBJ plist" (mirrors WF's reserved-sentinel semantics
        // for plist 0 and the no-PLIST marker 0xFF).
        uint _currentObjPlist;
        int _currentChipsetNo;
        int _currentTerrain;

        // Whole-chipset clipboard (mirrors WF Copy Tile / Copy Type / Paste).
        // Both _copiedChipset and _copiedTerrain are set on the corresponding
        // Copy operation; Paste() applies whichever values are populated and
        // returns false when both are null (the WF "no usable clipboard"
        // path). See PR #690 / v6 plan WU4 — the clipboard is whole-chipset,
        // never per-slot.
        (ushort W0, ushort W1, ushort W2, ushort W3)? _copiedChipset;
        byte? _copiedTerrain;

        // 16 RGB rows × 3 channels — flat backing store keeps the helper
        // surface (`GetColorR/G/B`, `SetColorR/G/B`) compact and avoids
        // a `Color` struct dependency that some Avalonia themes ban.
        readonly ushort[] _r = new ushort[16];
        readonly ushort[] _g = new ushort[16];
        readonly ushort[] _b = new ushort[16];

        // 4 tile slots (0/2/4/6) — each has split (X, Y, palette, flip) +
        // raw word. The split-to-raw setters keep both surfaces synchronized
        // so the editable view never diverges from the encoded word.
        readonly int[] _slotX = new int[4];   // index 0..3 for slot 0/2/4/6
        readonly int[] _slotY = new int[4];
        readonly int[] _slotPal = new int[4];
        readonly int[] _slotFlip = new int[4];
        readonly ushort[] _slotW = new ushort[4];

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint ObjPointer { get => _objPointer; set => SetField(ref _objPointer, value); }
        public uint ConfigPointer { get => _configPointer; set => SetField(ref _configPointer, value); }

        /// <summary>
        /// Stable base address of the 5+5 palette table for the current
        /// tileset. This is NOT mutated by <see cref="LoadPalette"/> —
        /// the slice address is stored in <see cref="PaletteAddress"/>
        /// separately so re-loading with a different paletteIndex/fog
        /// flag uses the correct base (Copilot bot v2 inline review).
        /// </summary>
        public uint PaletteBaseAddress { get => _paletteBaseAddress; set => SetField(ref _paletteBaseAddress, value); }

        /// <summary>
        /// Slice address of the currently-loaded 16-color palette block.
        /// Equals <c>PaletteBaseAddress + (paletteIndex + (isFog ? 5 : 0)) * 0x20</c>.
        /// Computed by <see cref="LoadPalette"/>; do NOT pass this back
        /// into <see cref="LoadPalette"/> — pass <see cref="PaletteBaseAddress"/>.
        /// </summary>
        public uint PaletteAddress { get => _paletteAddress; set => SetField(ref _paletteAddress, value); }
        public uint ObjAddress { get => _objAddress; set => SetField(ref _objAddress, value); }
        public uint ObjAddress2
        {
            get => _objAddress2;
            set { if (SetField(ref _objAddress2, value)) OnPropertyChanged(nameof(CanImportObj)); }
        }
        public uint ChipsetConfigAddress { get => _chipsetConfigAddress; set => SetField(ref _chipsetConfigAddress, value); }
        public int PaletteIndex { get => _paletteIndex; set => SetField(ref _paletteIndex, value); }
        public bool IsFogPalette { get => _isFogPalette; set => SetField(ref _isFogPalette, value); }
        public string ConfigNo { get => _configNo; set => SetField(ref _configNo, value); }

        /// <summary>
        /// PLIST byte resolved from the matching map_setting entry's +7
        /// field at <see cref="LoadEntry"/> time. 0 / 0xFF mean "no usable
        /// CONFIG plist"; <see cref="CanEditChipsetConfig"/> false in that
        /// case so the view disables the edit surface and the Config Write
        /// button can never call <see cref="WriteChipsetConfig"/> with an
        /// invalid slot.
        /// </summary>
        public uint CurrentConfigPlist
        {
            get => _currentConfigPlist;
            set { _currentConfigPlist = value; OnPropertyChanged(nameof(CurrentConfigPlist)); OnPropertyChanged(nameof(CanEditChipsetConfig)); }
        }

        /// <summary>
        /// OBJ Image Import (#710): plist byte for the primary OBJ tileset.
        /// Derived from the position of the loaded entry inside
        /// <c>map_obj_pointer</c>. <see cref="CanImportObj"/> rejects
        /// 0 (reserved sentinel) / >= 0xFF (no-PLIST marker).
        /// </summary>
        public uint CurrentObjPlist
        {
            get => _currentObjPlist;
            set { _currentObjPlist = value; OnPropertyChanged(nameof(CurrentObjPlist)); OnPropertyChanged(nameof(CanImportObj)); }
        }

        /// <summary>
        /// The currently-selected chipset (0..1023). Driven by the
        /// <c>ChipsetNoInput</c> NumericUpDown; <see cref="TryLoadChipsetTSA"/>
        /// reads from this and writes back to it on success.
        /// </summary>
        public int CurrentChipsetNo
        {
            get => _currentChipsetNo;
            set => SetField(ref _currentChipsetNo, value);
        }

        /// <summary>Currently-selected terrain byte (0..255).</summary>
        public int CurrentTerrain
        {
            get => _currentTerrain;
            set => SetField(ref _currentTerrain, value);
        }

        /// <summary>
        /// True when the Chipset tab edit surface should be enabled: a CONFIG
        /// buffer is decompressed in memory, a valid CONFIG slot exists, and
        /// the resolved <see cref="ChipsetConfigAddress"/> is non-zero. The
        /// view binds this to <c>IsEnabled</c> on the 20 slot NUDs, terrain
        /// combo, and 4 buttons so an unresolved map style cannot reach the
        /// write path.
        /// </summary>
        public bool CanEditChipsetConfig =>
            _cachedConfigData != null
            && _currentConfigPlist != 0
            && _currentConfigPlist != 0xFF
            && _chipsetConfigAddress != 0;

        /// <summary>
        /// True when the Map Style Editor OBJ Image Import button should
        /// be enabled (#710 ImageOnly slice):
        ///   - a ROM is loaded
        ///   - the resolved OBJ plist is non-zero (the reserved sentinel)
        ///   - the plist is below the per-version PLIST limit returned by
        ///     <see cref="MapChangeCore.GetPlistLimit"/> (Copilot bot #2
        ///     on PR #716: 0xFF is too loose — FE8U vanilla limit is
        ///     0xEC so an index of 0xED..0xFE would enable the button
        ///     but <c>WritePlistData</c> would still reject it).
        ///   - the entry does NOT carry a secondary FE7 obj2 tileset
        ///     (<see cref="ObjAddress2"/> == 0). The obj2-bearing case
        ///     requires the dual-tileset split path which is intentionally
        ///     deferred — TryImportObjImage produces a clear error message
        ///     when called on such a style so the View can route the
        ///     user to the follow-up tracking issue.
        ///
        /// <para>Notably <see cref="ObjPointer"/> is NOT gated — a slot
        /// that's currently null is a valid write target (WF
        /// <c>InputFormRef.WriteBinaryData</c> + Core
        /// <c>MapChangeCore.ResolvePlistSlotAddr</c> both treat address 0
        /// as "append new data"). Copilot bot #1 on PR #716.</para>
        /// </summary>
        public bool CanImportObj
        {
            get
            {
                var rom = CoreState.ROM;
                if (rom == null) return false;
                if (_currentObjPlist == 0) return false;
                if (_objAddress2 != 0) return false;
                uint limit = MapChangeCore.GetPlistLimit(rom);
                if (limit == 0 || _currentObjPlist >= limit) return false;
                return true;
            }
        }

        /// <summary>
        /// Returns a defensive clone of the cached decompressed CONFIG
        /// buffer for export (#692 partial slice — MapChip Export). Returns
        /// null when no entry is loaded or the LZ77 decompression at
        /// <see cref="LoadEntry"/> time produced no usable buffer. The
        /// clone protects the VM's internal cache from caller mutation
        /// (Copilot CLI v2 review on the v3 plan).
        /// </summary>
        public byte[]? GetCachedConfigClone() =>
            _cachedConfigData == null ? null : (byte[])_cachedConfigData.Clone();

        // ----- Palette helpers (5-5-5 RGB, 16 colors per palette) -----
        // colorIndex is 1-based to match the WF "PALETTE_P_1..16" labels.

        public ushort GetColorR(int colorIndex) => _r[colorIndex - 1];
        public ushort GetColorG(int colorIndex) => _g[colorIndex - 1];
        public ushort GetColorB(int colorIndex) => _b[colorIndex - 1];
        public void SetColorR(int colorIndex, ushort v) { _r[colorIndex - 1] = (ushort)(v & 0x1F); OnPropertyChanged($"Color{colorIndex}_R"); }
        public void SetColorG(int colorIndex, ushort v) { _g[colorIndex - 1] = (ushort)(v & 0x1F); OnPropertyChanged($"Color{colorIndex}_G"); }
        public void SetColorB(int colorIndex, ushort v) { _b[colorIndex - 1] = (ushort)(v & 0x1F); OnPropertyChanged($"Color{colorIndex}_B"); }

        // ----- Slot helpers (TSA word + split fields synchronized) -----

        static int SlotKeyToIndex(int slot) => slot switch
        {
            0 => 0,
            2 => 1,
            4 => 2,
            6 => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), $"Unknown slot key: {slot} (expected 0/2/4/6)"),
        };

        public ushort GetSlotW(int slot) => _slotW[SlotKeyToIndex(slot)];

        /// <summary>
        /// Set the raw TSA word for a slot. Repopulates the split fields
        /// via DecodeTsaWord so the editable surface always reflects the
        /// raw word (Copilot v2 review item 2 setter synchronization).
        /// </summary>
        public void SetSlotW(int slot, ushort w)
        {
            int i = SlotKeyToIndex(slot);
            _slotW[i] = w;
            var (x, y, p, f) = DecodeTsaWord(w);
            _slotX[i] = x;
            _slotY[i] = y;
            _slotPal[i] = p;
            _slotFlip[i] = f;
            OnPropertyChanged($"Slot{slot}_W");
        }

        public int GetSlotSplitField(int slot, string field)
        {
            int i = SlotKeyToIndex(slot);
            return field switch
            {
                "X" => _slotX[i],
                "Y" => _slotY[i],
                "PALETTE" => _slotPal[i],
                "FLIP" => _slotFlip[i],
                _ => throw new ArgumentException($"Unknown split field: {field}", nameof(field)),
            };
        }

        /// <summary>
        /// Set one split field and re-encode the raw word so the two surfaces
        /// stay synchronized (Copilot v2 review item 2 setter synchronization).
        /// </summary>
        public void SetSlotSplitField(int slot, string field, int value)
        {
            int i = SlotKeyToIndex(slot);
            switch (field)
            {
                case "X": _slotX[i] = value; break;
                case "Y": _slotY[i] = value; break;
                case "PALETTE": _slotPal[i] = value; break;
                case "FLIP": _slotFlip[i] = value; break;
                default: throw new ArgumentException($"Unknown split field: {field}", nameof(field));
            }
            _slotW[i] = EncodeTsaWord(_slotX[i], _slotY[i], _slotPal[i], _slotFlip[i]);
            OnPropertyChanged($"Slot{slot}_{field}");
            OnPropertyChanged($"Slot{slot}_W");
        }

        /// <summary>Set all four split fields at once and re-encode the raw word.</summary>
        public void SetSlotSplit(int slot, int x, int y, int palette, int flip)
        {
            int i = SlotKeyToIndex(slot);
            _slotX[i] = x;
            _slotY[i] = y;
            _slotPal[i] = palette;
            _slotFlip[i] = flip;
            _slotW[i] = EncodeTsaWord(x, y, palette, flip);
            OnPropertyChanged($"Slot{slot}_W");
        }

        /// <summary>
        /// Pack the split fields into the GBA TSA word format. Matches the
        /// GBA hardware BG-map word layout: tile in bits 0-9, flip in
        /// bits 10-11, palette in bits 12-15. WF's `MapEditorForm.DecodeTsaWord`
        /// uses this same packing implicitly via `tile = x + y * 32`.
        /// </summary>
        public static ushort EncodeTsaWord(int x, int y, int palette, int flip)
        {
            int tile = (x & 0x1F) + (y & 0x1F) * 32; // 5 bits of x + 5 bits of y => bits 0..9
            int word = (tile & 0x3FF) | ((flip & 0x3) << 10) | ((palette & 0xF) << 12);
            return (ushort)word;
        }

        /// <summary>The inverse of EncodeTsaWord. Round-trips exactly for in-range inputs.</summary>
        public static (int x, int y, int palette, int flip) DecodeTsaWord(ushort word)
        {
            int tile = word & 0x3FF;
            int x = tile & 0x1F;
            int y = (tile >> 5) & 0x1F;
            int flip = (word >> 10) & 0x3;
            int palette = (word >> 12) & 0xF;
            return (x, y, palette, flip);
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // Enumerate unique tileset indices from the map_obj_pointer table
            uint objPointer = rom.RomInfo.map_obj_pointer;
            if (objPointer == 0) return new List<AddrResult>();

            uint tableBase = rom.p32(objPointer);
            if (!U.isSafetyOffset(tableBase, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            // Enumerate entries (PLIST indices 0..255)
            for (int i = 0; i < 256; i++)
            {
                uint entryAddr = (uint)(tableBase + i * 4);
                if (entryAddr + 4 > (uint)rom.Data.Length) break;

                uint ptr = rom.u32(entryAddr);
                if (ptr == 0 || !U.isPointer(ptr))
                    continue;

                string label = $"0x{i:X2} Tileset";
                result.Add(new AddrResult(entryAddr, label, (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            // Always clear the chip-preview caches first, including on the
            // early-return paths below — otherwise a stale preview can leak
            // from a previous selection into a row that fails to load.
            _cachedObjData = null;
            _cachedPaletteBytes = null;
            // Same intent for the Chipset tab: every reload starts from a
            // blank cache + cleared slot/terrain state. ClearCacheAndChipsetState
            // wraps both so the early-return paths below don't drift.
            ClearCacheAndChipsetState();

            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            // Reset all dependent fields up-front so a failed read on
            // the parallel Config / Palette tables does not leave stale
            // state from the previous entry visible in the UI
            // (Copilot bot v3 inline review).
            ConfigPointer = 0;
            ChipsetConfigAddress = 0;
            ObjAddress2 = 0;
            PaletteBaseAddress = 0;
            PaletteAddress = 0;
            Array.Clear(_r, 0, 16);
            Array.Clear(_g, 0, 16);
            Array.Clear(_b, 0, 16);

            CurrentAddr = addr;
            ObjPointer = rom.u32(addr);
            ObjAddress = U.toOffset(ObjPointer);

            // FE7's obj_plist can carry a secondary tileset PLIST in the
            // high byte (WF MapStyleEditorForm.Display_Plist line 245:
            // `(obj_plist >> 8) & 0xFF`). The ObjPointer we read from
            // map_obj_pointer is a resolved 32-bit GBA pointer, so the
            // PLIST byte is not directly recoverable from here without
            // calling the PLIST writer. Surface "(none)" until Core
            // extracts that writer (KnownGap #374). ObjAddress2 stays 0.

            // Resolve the obj_plist ID from the addr position inside the
            // map_obj_pointer table. The user-facing palette_plist and
            // config_plist for THIS obj_plist do NOT live in parallel
            // tables under the same index — they are recorded as separate
            // u8 fields on every map_setting entry (palette_plist at +6,
            // config_plist at +7). To resolve them, find a map_setting
            // entry whose obj_plist low byte equals our index, then look
            // up the palette/config PLISTs in their respective pointer
            // tables. (Copilot CLI v1 PR review — without this lookup the
            // palette/config addresses share the obj index and the chip
            // preview renders OBJ bytes as palette data.)
            uint index = 0;
            uint objTableBase = rom.p32(rom.RomInfo.map_obj_pointer);
            if (U.isSafetyOffset(objTableBase, rom) && addr >= objTableBase)
                index = (addr - objTableBase) / 4;
            // OBJ Image Import (#710): the OBJ PLIST byte == the entry
            // index inside the map_obj_pointer table. CanImportObj rejects
            // 0 / 0xFF so the sentinel/no-plist slots can't reach the
            // write path.
            _currentObjPlist = index;

            byte palettePlist = 0;
            byte configPlist = 0;
            byte obj2Plist = 0;
            bool resolvedFromMapSetting = false;
            foreach (var map in MapSettingCore.MakeMapIDList(rom))
            {
                if (map.addr + 8 > (uint)rom.Data.Length) continue;
                ushort objPlistWord = (ushort)rom.u16(map.addr + 4);
                if ((uint)(objPlistWord & 0xFF) != index) continue;
                palettePlist = (byte)rom.u8(map.addr + 6);
                configPlist = (byte)rom.u8(map.addr + 7);
                obj2Plist = (byte)((objPlistWord >> 8) & 0xFF);
                resolvedFromMapSetting = true;
                break;
            }

            // Config pointer via PlistToOffsetAddr (configTableBase + configPlist*4).
            // Also retain the resolved configPlist itself so WriteChipsetConfig
            // can rewrite the correct PLIST slot without re-scanning map_setting.
            if (resolvedFromMapSetting && rom.RomInfo.map_config_pointer != 0)
            {
                _currentConfigPlist = configPlist;
                uint configTableBase = rom.p32(rom.RomInfo.map_config_pointer);
                if (U.isSafetyOffset(configTableBase, rom))
                {
                    uint configEntryAddr = configTableBase + (uint)configPlist * 4;
                    if (configEntryAddr + 4 <= (uint)rom.Data.Length)
                    {
                        ConfigPointer = rom.u32(configEntryAddr);
                        if (ConfigPointer != 0 && U.isPointer(ConfigPointer))
                            ChipsetConfigAddress = U.toOffset(ConfigPointer);
                    }
                }
            }

            // Palette base address via PlistToOffsetAddr
            // (palTableBase + palettePlist*4). Stored in PaletteBaseAddress
            // so subsequent PaletteCombo/PaletteTypeCombo changes can
            // re-index from this stable origin.
            if (resolvedFromMapSetting && rom.RomInfo.map_pal_pointer != 0)
            {
                uint paletteTableBase = rom.p32(rom.RomInfo.map_pal_pointer);
                if (U.isSafetyOffset(paletteTableBase, rom))
                {
                    uint paletteEntryAddr = paletteTableBase + (uint)palettePlist * 4;
                    if (paletteEntryAddr + 4 <= (uint)rom.Data.Length)
                    {
                        uint palPtr = rom.u32(paletteEntryAddr);
                        if (palPtr != 0 && U.isPointer(palPtr))
                            PaletteBaseAddress = U.toOffset(palPtr);
                    }
                }
            }

            // FE7 obj_plist high byte = secondary OBJ tileset. Surface its
            // resolved address only; the actual tileset append into the
            // preview cache is tracked separately in #689.
            if (obj2Plist > 0 && rom.RomInfo.map_obj_pointer != 0)
            {
                uint objTableBase2 = rom.p32(rom.RomInfo.map_obj_pointer);
                if (U.isSafetyOffset(objTableBase2, rom))
                {
                    uint obj2EntryAddr = objTableBase2 + (uint)obj2Plist * 4;
                    if (obj2EntryAddr + 4 <= (uint)rom.Data.Length)
                    {
                        uint obj2Ptr = rom.u32(obj2EntryAddr);
                        if (obj2Ptr != 0 && U.isPointer(obj2Ptr))
                            ObjAddress2 = U.toOffset(obj2Ptr);
                    }
                }
            }

            // Populate the 16-color palette for the currently-selected
            // PaletteIndex / IsFogPalette (defaults to 0 / false on first load).
            // Safe-no-op when PaletteBaseAddress is 0 or out-of-bounds.
            LoadPalette(PaletteBaseAddress, PaletteIndex, IsFogPalette);

            // Cache the LZ77-decompressed OBJ tile data for the chip preview
            // (#670). FE7 obj2 secondary tileset is appended after the primary
            // (#689) if `ObjAddress2 != 0`.
            if (ObjPointer != 0 && U.isSafetyOffset(U.toOffset(ObjPointer), rom))
            {
                try
                {
                    var objUz = LZ77.decompress(rom.Data, U.toOffset(ObjPointer));
                    if (objUz != null && objUz.Length > 0)
                        _cachedObjData = objUz;
                }
                catch { _cachedObjData = null; }
            }

            // Append FE7 secondary OBJ tileset (#689) if present.
            // WinForms ImageUtilMap.DrawMapChipOnly does `objUZ = U.ArrayAppend(objUZ, obj2UZ)` for FE7 styles
            // that pack two tilesets together. Without this, FE7 styles using obj2 would render only the
            // primary tile sheet.
            // Avalonia behavior difference: if obj2 decompression fails, we keep the primary-only cache
            // rather than dropping the entire preview (WF returns null). This degrades gracefully.
            if (_cachedObjData != null && ObjAddress2 != 0 && U.isSafetyOffset(ObjAddress2, rom))
            {
                try
                {
                    var obj2Uz = LZ77.decompress(rom.Data, ObjAddress2);
                    if (obj2Uz != null && obj2Uz.Length > 0)
                    {
                        var combined = new byte[_cachedObjData.Length + obj2Uz.Length];
                        System.Buffer.BlockCopy(_cachedObjData, 0, combined, 0, _cachedObjData.Length);
                        System.Buffer.BlockCopy(obj2Uz, 0, combined, _cachedObjData.Length, obj2Uz.Length);
                        _cachedObjData = combined;
                    }
                }
                catch { /* leave primary-only cache on decompress failure */ }
            }

            // Cache full 16-palette block (512 bytes) starting at PaletteBaseAddress.
            RefreshCachedPaletteBytes();

            // CONFIG decompress (#671). Cached buffer drives both TSA reads
            // for TryLoadChipsetTSA and the staging clone for WriteChipsetConfig.
            // Wrapped in try/catch so a malformed compressed block in a synthetic
            // or corrupted ROM does not abort LoadEntry halfway through (the
            // chip preview path uses the same pattern).
            if (ChipsetConfigAddress != 0 && U.isSafetyOffset(ChipsetConfigAddress, rom))
            {
                try
                {
                    var configUz = LZ77.decompress(rom.Data, ChipsetConfigAddress);
                    if (configUz != null && configUz.Length > 0)
                        _cachedConfigData = configUz;
                }
                catch { _cachedConfigData = null; }
            }
            OnPropertyChanged(nameof(CanEditChipsetConfig));
            // OBJ Image Import (#710): notify the View that the import
            // surface enablement may have changed (ObjPointer / ObjAddress2
            // / _currentObjPlist all just got updated).
            OnPropertyChanged(nameof(CanImportObj));

            ConfigNo = $"0x{index:X2}";
            IsLoaded = true;
        }

        /// <summary>
        /// Wipe both the decompressed CONFIG cache + the current slot state
        /// (chipset/terrain/slot Ws). Used by <see cref="LoadEntry"/> when the
        /// CONFIG path fails or the entry resolves to no map_setting record,
        /// and by every early-return path so a previous-style cache cannot
        /// leak into the next selection.
        /// </summary>
        public void ClearCacheAndChipsetState()
        {
            _cachedConfigData = null;
            _currentConfigPlist = 0;
            // OBJ Image Import (#710): clear the resolved OBJ PLIST on the
            // same early-return paths so a previous style's plist can't
            // leak into a row that fails to load.
            _currentObjPlist = 0;
            ClearChipsetSlotState();
            OnPropertyChanged(nameof(CanEditChipsetConfig));
            OnPropertyChanged(nameof(CanImportObj));
        }

        /// <summary>
        /// Reset only the per-chipset slot state (slot Ws, split fields,
        /// chipset number, terrain). Preserves <c>_cachedConfigData</c> /
        /// <see cref="CurrentConfigPlist"/> so the user can correct the
        /// ChipsetNo input without losing the decompressed CONFIG buffer.
        /// </summary>
        public void ClearChipsetSlotState()
        {
            for (int i = 0; i < 4; i++)
            {
                _slotW[i] = 0;
                _slotX[i] = 0;
                _slotY[i] = 0;
                _slotPal[i] = 0;
                _slotFlip[i] = 0;
            }
            _currentChipsetNo = 0;
            _currentTerrain = 0;
            OnPropertyChanged(nameof(CurrentChipsetNo));
            OnPropertyChanged(nameof(CurrentTerrain));
            // Fire property change for all slot-W bindings so the view's
            // ReadSlotsFromVM() pass picks up the zeroed values.
            int[] suffixes = { 0, 2, 4, 6 };
            foreach (int s in suffixes) OnPropertyChanged($"Slot{s}_W");
        }

        /// <summary>
        /// Decode the per-chipset TSA block at <paramref name="chipsetNo"/>
        /// (4 u16 entries, 8 bytes) plus its terrain byte from the cached
        /// CONFIG buffer into the slot state. Returns false (and calls
        /// <see cref="ClearChipsetSlotState"/>) when no cache is loaded,
        /// the index is outside the chipset range
        /// [0, <see cref="MapEditorTilesetCore.CHIPSET_COUNT"/>), or the
        /// buffer is too small to hold either the 8-byte TSA block or the
        /// terrain byte at <c>CHIPSET_SEP_BYTE + chipsetNo</c>.
        /// </summary>
        public bool TryLoadChipsetTSA(int chipsetNo)
        {
            if (_cachedConfigData == null) { ClearChipsetSlotState(); return false; }
            if (chipsetNo < 0 || chipsetNo >= MapEditorTilesetCore.CHIPSET_COUNT)
            { ClearChipsetSlotState(); return false; }

            int tsaBase = chipsetNo * 8;
            if (tsaBase + 7 >= _cachedConfigData.Length) { ClearChipsetSlotState(); return false; }
            int terrainOffset = MapEditorTilesetCore.CHIPSET_SEP_BYTE + chipsetNo;
            if (terrainOffset >= _cachedConfigData.Length) { ClearChipsetSlotState(); return false; }

            for (int sub = 0; sub < 4; sub++)
            {
                int off = tsaBase + sub * 2;
                ushort w = (ushort)(_cachedConfigData[off] | (_cachedConfigData[off + 1] << 8));
                _slotW[sub] = w;
                var (x, y, p, f) = DecodeTsaWord(w);
                _slotX[sub] = x;
                _slotY[sub] = y;
                _slotPal[sub] = p;
                _slotFlip[sub] = f;
            }
            _currentChipsetNo = chipsetNo;
            _currentTerrain = _cachedConfigData[terrainOffset];

            OnPropertyChanged(nameof(CurrentChipsetNo));
            OnPropertyChanged(nameof(CurrentTerrain));
            int[] suffixes = { 0, 2, 4, 6 };
            foreach (int s in suffixes) OnPropertyChanged($"Slot{s}_W");
            return true;
        }

        /// <summary>
        /// Logical-index helper for the slot W setters. Slot suffix 0 / 2 / 4 / 6
        /// (used by the existing AXAML control names) maps to logical index
        /// 0 / 1 / 2 / 3 via <c>suffix / 2</c>.
        /// </summary>
        public void SetSlotWByLogicalIndex(int logicalIndex, ushort w)
        {
            if (logicalIndex < 0 || logicalIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(logicalIndex),
                    $"Slot logical index must be 0..3, got {logicalIndex}");
            SetSlotW(logicalIndex * 2, w);
        }

        /// <summary>
        /// Convenience wrapper for the view: take the X/Y/Palette/Flip values
        /// the user entered into a slot's split NUDs, encode them, and call
        /// the byte-offset SetSlotW so the raw-W field updates in lockstep.
        /// </summary>
        public void SetSlotSplitByLogicalIndex(int logicalIndex, int x, int y, int palette, int flip)
        {
            if (logicalIndex < 0 || logicalIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(logicalIndex),
                    $"Slot logical index must be 0..3, got {logicalIndex}");
            SetSlotSplit(logicalIndex * 2, x, y, palette, flip);
        }

        /// <summary>
        /// Whole-chipset copy (mirrors WF <c>CopyTileButton_Click</c>): captures
        /// all four staged W values AND the current terrain so a subsequent
        /// <see cref="Paste"/> restores the chipset's full state (Copilot CLI
        /// PR #691 review item 1 — WF serializes terrain into the Tile copy too).
        /// <see cref="CopyTerrain"/> remains independent for callers that want
        /// to paste type-only.
        /// </summary>
        public void CopyChipset()
        {
            _copiedChipset = (_slotW[0], _slotW[1], _slotW[2], _slotW[3]);
            _copiedTerrain = (byte)(CurrentTerrain & 0xFF);
        }

        /// <summary>
        /// Capture just the terrain byte for paste-as-type-only.
        ///
        /// <para>Clears any previously-copied chipset W values so a
        /// subsequent <see cref="Paste"/> applies ONLY the terrain. Without
        /// this, a Copy Tile -> Copy Type -> Paste sequence would silently
        /// reapply the old tile's W values on top of the user's intended
        /// terrain-only paste (Copilot v3 inline review on PR #691).</para>
        /// </summary>
        public void CopyTerrain()
        {
            _copiedChipset = null;
            _copiedTerrain = (byte)(CurrentTerrain & 0xFF);
        }

        /// <summary>
        /// Apply the current clipboard contents to the staged slot state.
        /// Returns false when the clipboard is empty (neither tile nor
        /// terrain captured) — the view uses this signal to suppress the
        /// auto-write that follows a successful paste.
        /// </summary>
        public bool Paste()
        {
            if (_copiedChipset == null && _copiedTerrain == null) return false;
            if (_copiedChipset is { } c)
            {
                SetSlotWByLogicalIndex(0, c.W0);
                SetSlotWByLogicalIndex(1, c.W1);
                SetSlotWByLogicalIndex(2, c.W2);
                SetSlotWByLogicalIndex(3, c.W3);
            }
            if (_copiedTerrain is { } t) CurrentTerrain = t;
            return true;
        }

        /// <summary>
        /// Persist the staged chipset state back to ROM. Clones the cached
        /// CONFIG buffer, writes the 4 TSA words for <see cref="CurrentChipsetNo"/>
        /// + the terrain byte, LZ77-compresses the result, appends it to
        /// free space via <see cref="MapChangeCore.WritePlistData"/>, and
        /// updates the CONFIG PLIST slot to point at the new offset.
        ///
        /// <para>Requires an ambient undo scope (opened by the view via
        /// <c>UndoService.Begin</c>) so the append + p32 write are tracked.
        /// On success, swaps <see cref="ChipsetConfigAddress"/> and the
        /// cached buffer to the new location/contents.</para>
        ///
        /// <para>Returns false (without ROM mutation) when no cache is
        /// loaded, the configPlist is 0 / 0xFF, the chipset/terrain bounds
        /// reject the in-memory edits, or the LZ77 compress / WritePlistData
        /// step fails. <paramref name="error"/> carries a short reason.</para>
        /// </summary>
        public bool WriteChipsetConfig(out string error)
        {
            error = "";
            ROM rom = CoreState.ROM;
            if (rom == null) { error = "no ROM"; return false; }
            if (_cachedConfigData == null) { error = "CONFIG buffer not loaded"; return false; }
            if (_currentConfigPlist == 0 || _currentConfigPlist == 0xFF)
            { error = "invalid CONFIG plist"; return false; }
            if (_currentChipsetNo < 0 || _currentChipsetNo >= MapEditorTilesetCore.CHIPSET_COUNT)
            { error = "chipset index out of range"; return false; }

            // Stage on a clone — the source cache stays intact until ROM
            // write succeeds.
            byte[] staged = (byte[])_cachedConfigData.Clone();
            int tsaBase = _currentChipsetNo * 8;
            if (tsaBase + 7 >= staged.Length) { error = "TSA offset past buffer end"; return false; }
            for (int sub = 0; sub < 4; sub++)
            {
                int off = tsaBase + sub * 2;
                ushort w = _slotW[sub];
                staged[off + 0] = (byte)(w & 0xFF);
                staged[off + 1] = (byte)((w >> 8) & 0xFF);
            }
            if (!MapEditorTilesetCore.SetTerrainForChipset(_currentChipsetNo, (byte)(CurrentTerrain & 0xFF), staged))
            { error = "terrain offset past buffer end"; return false; }

            byte[] compressed = LZ77.compress(staged);
            if (compressed == null || compressed.Length == 0) { error = "LZ77 compress failed"; return false; }

            uint newAddr = MapChangeCore.WritePlistData(rom, MapChangeCore.PlistType.CONFIG, _currentConfigPlist, compressed, out error);
            if (newAddr == U.NOT_FOUND) return false;

            // Swap in the new cache + address only after the write succeeded.
            _cachedConfigData = staged;
            ChipsetConfigAddress = newAddr;
            return true;
        }

        /// <summary>
        /// Replace the cached CONFIG buffer with raw bytes from a
        /// <c>.MAPCHIP_CONFIG</c> file (WF parity: no magic header, the file
        /// is the raw decompressed buffer). Validates the WF-parity minimum
        /// of <c>9216</c> bytes (<see cref="MapEditorTilesetCore.CHIPSET_SEP_BYTE"/>
        /// for TSA + <see cref="MapEditorTilesetCore.CHIPSET_COUNT"/> terrain
        /// bytes), then LZ77-compresses the result and persists it via the
        /// CONFIG PLIST write path (#671). Palette-index bits in TSA words
        /// (bits 12-15 of each u16) are preserved verbatim because the buffer
        /// is written byte-for-byte to ROM — there is no slot-by-slot decode
        /// path that could strip them (#704 acceptance).
        ///
        /// <para>Requires an ambient undo scope (opened by the view via
        /// <c>UndoService.Begin</c>) so the LZ77 append + p32 write are
        /// tracked. On success, swaps the cached buffer and the
        /// <see cref="ChipsetConfigAddress"/> to the new location.</para>
        ///
        /// <para>Returns false (without ROM mutation) when the supplied bytes
        /// are null or too small, no ROM is loaded, or the resolved CONFIG
        /// plist is invalid (0 / 0xFF / no entry). <paramref name="error"/>
        /// carries a short human-readable reason.</para>
        /// </summary>
        public bool TryWriteConfigBuffer(byte[] newConfigData, out string error)
        {
            error = "";
            // WF parity: MapStyleEditorForm.MapChipImportButton_Click rejects
            // anything < 9216 bytes (0x2000 TSA + 0x400 terrain).
            const int MinConfigSize = MapEditorTilesetCore.CHIPSET_SEP_BYTE + MapEditorTilesetCore.CHIPSET_COUNT;
            if (newConfigData == null || newConfigData.Length < MinConfigSize)
            {
                error = $"Config file too small ({(newConfigData?.Length ?? 0)} bytes); must be at least {MinConfigSize} bytes.";
                return false;
            }
            // Copilot bot v2 inline review: validate only the CONFIG plist id,
            // NOT the full CanEditChipsetConfig predicate. CanEditChipsetConfig
            // also requires `_cachedConfigData != null` and `ChipsetConfigAddress
            // != 0`, which would block import-as-recovery when the existing
            // CONFIG block is corrupt or missing but the plist id itself is
            // still valid. Import only needs the plist id (and ROM with a
            // populated map_config_pointer — WritePlistData enforces the
            // latter).
            if (_currentConfigPlist == 0 || _currentConfigPlist == 0xFF)
            {
                error = "Map Style entry does not have a valid CONFIG PLIST.";
                return false;
            }
            ROM rom = CoreState.ROM;
            if (rom == null) { error = "no ROM"; return false; }

            byte[] compressed = LZ77.compress(newConfigData);
            if (compressed == null || compressed.Length == 0)
            {
                error = "LZ77 compress failed";
                return false;
            }

            uint newAddr = MapChangeCore.WritePlistData(rom, MapChangeCore.PlistType.CONFIG, _currentConfigPlist, compressed, out error);
            if (newAddr == U.NOT_FOUND) return false;

            // Swap cache + address only after the write succeeded so a
            // failed write leaves the previous in-memory state untouched.
            // Copilot bot v1 inline review: store a defensive clone so the
            // caller cannot mutate our cache after we accept the bytes (and
            // matches the WriteChipsetConfig "stage on a clone, then swap"
            // ownership pattern).
            _cachedConfigData = (byte[])newConfigData.Clone();
            ChipsetConfigAddress = newAddr;
            return true;
        }

        /// <summary>
        /// OBJ Image Import — ImageOnly slice (#710): remap a source
        /// RGBA image against the currently-loaded OBJ palette (palette
        /// row 0), pack it into 4bpp tile data, LZ77-compress, and write
        /// the result to a free-space slot via the OBJECT PLIST table.
        /// The OBJ palette in ROM is NOT modified — this slice covers
        /// only the tile-sheet path.
        ///
        /// <para>WinForms parity contract: width MUST be exactly 256
        /// pixels, height MUST be >= 128, and height MUST be a multiple
        /// of 8. These bounds mirror the WF
        /// <c>MapStyleEditorForm.ImportObj</c> validation. The remap
        /// step uses the existing <c>_cachedPaletteBytes</c> palette
        /// row 0 (32 bytes) so quantization color drift is avoided —
        /// see Copilot CLI v1 review item 1 on the v2 plan.</para>
        ///
        /// <para>FE7 styles that carry a secondary OBJ tileset
        /// (<see cref="ObjAddress2"/> != 0) are rejected with an
        /// actionable error: this slice writes a single tile sheet
        /// only. Tracked separately as a follow-up.</para>
        ///
        /// <para>Requires an ambient undo scope (opened by the view via
        /// <c>UndoService.Begin</c>) so the LZ77 append + p32 write are
        /// undoable atomically. On success swaps the cached OBJ tile
        /// data + <see cref="ObjAddress"/> / <see cref="ObjPointer"/>
        /// to the new offset so the chip preview reflects the new
        /// import on the next refresh.</para>
        ///
        /// <para>Returns false (without ROM mutation past the ambient
        /// undo scope's rollback range) when any validation fails;
        /// <paramref name="error"/> carries a short human-readable
        /// reason.</para>
        /// </summary>
        public bool TryImportObjImage(byte[] sourceRgba, int width, int height, out string error)
        {
            error = "";

            // WF parity dimension contract (Copilot CLI v1 review item 2).
            // Width MUST be 256, height MUST be >= 128 and a multiple of 8.
            if (width != 256)
            {
                error = $"OBJ image must be exactly 256 pixels wide (got {width}).";
                return false;
            }
            if (height < 128)
            {
                error = $"OBJ image must be at least 128 pixels tall (got {height}).";
                return false;
            }
            if ((height % 8) != 0)
            {
                error = $"OBJ image height must be a multiple of 8 (got {height}).";
                return false;
            }
            // Copilot bot #3 on PR #716: width * height * 4 uses int
            // multiplication and overflows around height ≈ 2.1M. Use long
            // arithmetic + LongLength to keep the bounds check honest even
            // for large user inputs.
            if (sourceRgba == null)
            {
                error = "Invalid source pixel data.";
                return false;
            }
            long requiredBytes = (long)width * (long)height * 4L;
            if (sourceRgba.LongLength < requiredBytes)
            {
                error = "Invalid source pixel data.";
                return false;
            }

            // Reject FE7 obj2-bearing styles before any encoding work.
            // Distinguish "obj2 present" from "no plist resolved" so the
            // error message guides the user to the tracking issue rather
            // than the wrong follow-up.
            if (_objAddress2 != 0)
            {
                error = "OBJ image import does not yet support FE7 styles with a secondary obj2 tileset. Tracked separately.";
                return false;
            }
            if (!CanImportObj)
            {
                // Provide a more specific error when the plist exceeds
                // the per-version limit so the user knows the data is
                // technically valid but the slot index is out of range.
                var rom2 = CoreState.ROM;
                if (rom2 != null)
                {
                    uint limit = MapChangeCore.GetPlistLimit(rom2);
                    if (limit > 0 && _currentObjPlist >= limit)
                    {
                        error = $"OBJ PLIST 0x{_currentObjPlist:X2} is past the per-version limit (0x{limit:X2}).";
                        return false;
                    }
                }
                error = "Cannot import OBJ image — no valid OBJ PLIST is selected.";
                return false;
            }

            if (_cachedPaletteBytes == null || _cachedPaletteBytes.Length < 32)
            {
                error = "OBJ palette is not loaded.";
                return false;
            }

            var rom = CoreState.ROM;
            if (rom == null) { error = "No ROM loaded."; return false; }

            // Remap pixels against existing palette row 0 (32 bytes / 16
            // colors). Using the loaded palette avoids re-quantization and
            // preserves the on-disk palette per the ImageOnly slice
            // contract (Copilot CLI v1 review item 1).
            byte[] palette32 = new byte[32];
            System.Array.Copy(_cachedPaletteBytes, 0, palette32, 0, 32);
            byte[] indexed;
            try
            {
                indexed = ImageImportCore.RemapToExistingPalette(sourceRgba, width, height, palette32, 16);
            }
            catch (System.Exception ex)
            {
                error = $"Palette remap failed: {ex.Message}";
                return false;
            }
            if (indexed == null)
            {
                error = "Palette remap returned no data (image service unavailable?).";
                return false;
            }

            byte[] tileData;
            try
            {
                tileData = ImageImportCore.EncodeDirectTiles4bpp(indexed, width, height);
            }
            catch (System.Exception ex)
            {
                error = $"Tile encoding failed: {ex.Message}";
                return false;
            }
            if (tileData == null || tileData.Length == 0)
            {
                error = "Tile encoding produced no data.";
                return false;
            }

            byte[] compressed;
            try
            {
                compressed = LZ77.compress(tileData);
            }
            catch (System.Exception ex)
            {
                error = $"LZ77 compression failed: {ex.Message}";
                return false;
            }
            if (compressed == null || compressed.Length == 0)
            {
                error = "LZ77 compression produced empty output.";
                return false;
            }

            uint newAddr = MapChangeCore.WritePlistData(rom, MapChangeCore.PlistType.OBJECT,
                                                       _currentObjPlist, compressed, out error);
            if (newAddr == U.NOT_FOUND) return false;

            // Swap in the new cache + address only after the write
            // succeeded so a failed write leaves preview state untouched.
            _cachedObjData = tileData;
            ObjAddress = newAddr;
            ObjPointer = U.toPointer(newAddr);
            return true;
        }

        // -----------------------------------------------------------------
        // Test seams for #710. These are explicitly internal so unit tests
        // can stage the in-memory caches that the LoadEntry path normally
        // populates from ROM — without forcing the test to plant a full
        // map_setting / map_obj_pointer / map_pal_pointer triple just to
        // exercise the import write path.
        // -----------------------------------------------------------------

        /// <summary>
        /// Internal test seam: stage the cached 16-color OBJ palette
        /// (32 bytes) so <see cref="TryImportObjImage"/> has palette data
        /// to remap against without a full LoadEntry / LoadPalette call.
        /// </summary>
        internal void SetCachedPaletteBytesForTest(byte[] palette)
        {
            if (palette == null) { _cachedPaletteBytes = null; return; }
            _cachedPaletteBytes = (byte[])palette.Clone();
        }

        /// <summary>
        /// Internal test seam: stage the resolved OBJ PLIST byte so
        /// <see cref="CanImportObj"/> reaches its happy-path branch
        /// without needing a planted map_setting / map_obj_pointer pair.
        /// </summary>
        internal void SetCurrentObjPlistForTest(uint plist)
        {
            _currentObjPlist = plist;
            OnPropertyChanged(nameof(CurrentObjPlist));
            OnPropertyChanged(nameof(CanImportObj));
        }

        /// <summary>
        /// Re-read the full 16-palette block (16 palettes × 16 colors × 2 bytes
        /// = 512 bytes) from <see cref="PaletteBaseAddress"/> into the local
        /// cache used by the chip preview (#670). Called from <see cref="LoadEntry"/>
        /// and from the view after a successful PaletteWrite so the preview
        /// reflects edits.
        /// </summary>
        public void RefreshCachedPaletteBytes()
        {
            var rom = CoreState.ROM;
            if (rom == null) { _cachedPaletteBytes = null; return; }
            if (PaletteBaseAddress == 0) { _cachedPaletteBytes = null; return; }
            int needed = 16 * 16 * 2;
            if ((ulong)PaletteBaseAddress + (ulong)needed > (ulong)rom.Data.Length)
            {
                _cachedPaletteBytes = null;
                return;
            }
            _cachedPaletteBytes = rom.getBinaryData(PaletteBaseAddress, (uint)needed);
        }

        /// <summary>
        /// Render the cached OBJ tile sheet using the currently-selected palette
        /// (with the fog offset applied) into an RGBA8888 buffer. Returns false
        /// if either cache is missing or the underlying render fails.
        /// </summary>
        public bool TryRenderObjTileSheet(out byte[] rgba, out int width, out int height)
        {
            rgba = null;
            width = 0;
            height = 0;
            if (_cachedObjData == null || _cachedPaletteBytes == null) return false;

            // Effective palette index applies the fog offset like WF's CalcPatelleIndex().
            int effectiveIndex = PaletteIndex + (IsFogPalette ? 5 : 0);
            var result = MapEditorTilesetCore.RenderTileSheet4bpp(
                _cachedObjData, _cachedPaletteBytes, effectiveIndex,
                columns: 32, out width, out height);
            if (result == null || width == 0 || height == 0)
            {
                width = 0;
                height = 0;
                return false;
            }
            rgba = result;
            return true;
        }

        /// <summary>
        /// Load a 16-color palette from a flat RGB-555 region. Effective
        /// offset is <c>paletteBase + (paletteIndex + (isFog ? 5 : 0)) * 0x20</c>
        /// per WF's MapStyle palette indexing (5 normal + 5 fog palettes).
        /// Returns false (without writing properties) when the address
        /// is unsafe; this is a pure read — no allocation, no PLIST update.
        /// </summary>
        public bool LoadPalette(uint paletteBase, int paletteIndex, bool isFog)
        {
            if (paletteBase == 0) return false;
            ROM rom = CoreState.ROM;
            if (rom == null) return false;

            // Validate `paletteIndex` is in the WF-expected 0..4 range so the
            // effective index (after the fog offset) stays in 0..9. Negative
            // or out-of-range inputs would otherwise wrap when cast to uint
            // and bypass the in-bounds check (Copilot bot inline review).
            if (paletteIndex < 0 || paletteIndex >= 5) return false;

            int effectiveIndex = paletteIndex + (isFog ? 5 : 0);
            // Compute the target address in ulong so out-of-range inputs
            // can't wrap a uint addition past 2^32. The final uint cast is
            // safe once we've bounds-checked against rom.Data.Length.
            ulong target = (ulong)paletteBase + (ulong)effectiveIndex * 0x20UL;
            if (target + 0x20UL > (ulong)rom.Data.Length) return false;
            uint addr = (uint)target;

            for (int i = 0; i < 16; i++)
            {
                ushort packed = (ushort)rom.u16(addr + (uint)(i * 2));
                _r[i] = (ushort)(packed & 0x1F);
                _g[i] = (ushort)((packed >> 5) & 0x1F);
                _b[i] = (ushort)((packed >> 10) & 0x1F);
            }
            // CRITICAL: Preserve the base in PaletteBaseAddress so the
            // next ReloadPalette call (PaletteCombo/PaletteTypeCombo
            // change) re-indexes from the same base rather than drifting
            // by idx*0x20 on each selection (Copilot bot v2 inline review).
            PaletteBaseAddress = paletteBase;
            PaletteAddress = addr;
            PaletteIndex = paletteIndex;
            IsFogPalette = isFog;
            return true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            rom.write_u32(CurrentAddr, ObjPointer);
        }

        /// <summary>
        /// Clear the in-memory palette slice state. Called when a reload
        /// fails (out-of-bounds or invalid base) so a subsequent
        /// <see cref="WritePalette"/> cannot accidentally write the
        /// previous palette's RGB values to the previous slice address.
        /// (Copilot CLI v2 PR review — stale-state regression.)
        /// </summary>
        public void ClearPaletteState()
        {
            PaletteAddress = 0;
            Array.Clear(_r, 0, 16);
            Array.Clear(_g, 0, 16);
            Array.Clear(_b, 0, 16);
        }

        /// <summary>
        /// Write the in-memory 16-color palette block back to the resolved
        /// <see cref="PaletteAddress"/> slice using 5-5-5 little-endian
        /// halfwords (matches the on-disk format read by <see cref="LoadPalette"/>).
        /// Returns true when the write succeeded; false when no ROM is loaded,
        /// PaletteAddress is unresolved/zero, or the target region would go
        /// past the ROM end. The write is a pure overwrite of the existing
        /// 32-byte slice — no allocation, no PLIST relocation. Caller is
        /// responsible for opening an undo scope before calling this so the
        /// ambient <c>rom.write_u16</c> calls are tracked.
        /// </summary>
        public bool WritePalette()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (PaletteAddress == 0) return false;

            // Bounds-check the full 32-byte block before any write so a
            // partial-overwrite cannot leave the palette slice corrupted.
            ulong end = (ulong)PaletteAddress + 0x20UL;
            if (end > (ulong)rom.Data.Length) return false;

            for (int i = 0; i < 16; i++)
            {
                ushort packed = (ushort)(
                    (_r[i] & 0x1F)
                    | ((_g[i] & 0x1F) << 5)
                    | ((_b[i] & 0x1F) << 10));
                rom.write_u16(PaletteAddress + (uint)(i * 2), packed);
            }
            return true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ObjPointer"] = $"0x{ObjPointer:X08}",
                ["ConfigPointer"] = $"0x{ConfigPointer:X08}",
                ["PaletteAddress"] = $"0x{PaletteAddress:X08}",
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
                ["ObjPointer@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["ObjPointer"] = "ObjPointer@0x00",
            };
        }
    }
}
