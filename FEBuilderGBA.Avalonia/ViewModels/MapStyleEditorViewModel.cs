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
        public uint ObjAddress2 { get => _objAddress2; set => SetField(ref _objAddress2, value); }
        public uint ChipsetConfigAddress { get => _chipsetConfigAddress; set => SetField(ref _chipsetConfigAddress, value); }
        public int PaletteIndex { get => _paletteIndex; set => SetField(ref _paletteIndex, value); }
        public bool IsFogPalette { get => _isFogPalette; set => SetField(ref _isFogPalette, value); }
        public string ConfigNo { get => _configNo; set => SetField(ref _configNo, value); }

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
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            ObjPointer = rom.u32(addr);
            ObjAddress = U.toOffset(ObjPointer);

            // Also load config pointer from the parallel config table
            uint configTablePointer = rom.RomInfo.map_config_pointer;
            uint configTableBase = 0;
            uint index = 0;
            if (configTablePointer != 0)
            {
                uint objTableBase = rom.p32(rom.RomInfo.map_obj_pointer);
                if (U.isSafetyOffset(objTableBase, rom) && addr >= objTableBase)
                {
                    index = (addr - objTableBase) / 4;
                    configTableBase = rom.p32(configTablePointer);
                    if (U.isSafetyOffset(configTableBase, rom))
                    {
                        uint configEntryAddr = configTableBase + index * 4;
                        if (configEntryAddr + 4 <= (uint)rom.Data.Length)
                        {
                            ConfigPointer = rom.u32(configEntryAddr);
                            ChipsetConfigAddress = U.toOffset(ConfigPointer);
                        }
                    }
                }
            }

            // Resolve palette pointer from the parallel palette table.
            // The dereferenced pointer is the BASE of the 5+5 palette
            // block — stored in PaletteBaseAddress (NOT PaletteAddress)
            // so subsequent PaletteCombo/PaletteTypeCombo changes can
            // re-index from this stable origin (Copilot bot v2 inline
            // review). PaletteAddress holds the slice address per
            // LoadPalette's contract.
            uint palettePointer = rom.RomInfo.map_pal_pointer;
            if (palettePointer != 0)
            {
                uint paletteTableBase = rom.p32(palettePointer);
                if (U.isSafetyOffset(paletteTableBase, rom))
                {
                    uint paletteEntryAddr = paletteTableBase + index * 4;
                    if (paletteEntryAddr + 4 <= (uint)rom.Data.Length)
                    {
                        uint palPtr = rom.u32(paletteEntryAddr);
                        if (palPtr != 0 && U.isPointer(palPtr))
                            PaletteBaseAddress = U.toOffset(palPtr);
                    }
                }
            }

            // Populate the 16-color palette for the currently-selected
            // PaletteIndex / IsFogPalette (defaults to 0 / false on first load).
            // Safe-no-op when PaletteBaseAddress is 0 or out-of-bounds.
            LoadPalette(PaletteBaseAddress, PaletteIndex, IsFogPalette);

            ConfigNo = $"0x{index:X2}";
            IsLoaded = true;
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
