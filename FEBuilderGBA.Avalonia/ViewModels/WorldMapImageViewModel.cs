// SPDX-License-Identifier: GPL-3.0-or-later
// WorldMapImage VM rebuild for #395 gap-sweep parity. Mirrors WF
// `WorldMapImageForm` 6-tab FE8 world-map editor.
//
// Per Copilot CLI plan-review v1 -> v2:
//   C1 - WriteAllPointers persists all 13 canonical pointer slots
//        (4 main + 3 event + 2 mini + 3 point/icon + 1 icon-palette)
//        mirroring the WF `WriteButton_Click` semantics.
//   C2 - Border / Icon list scans use raw `rom.u32(...)` + `U.isPointer(...)`;
//        `rom.p32(...)` is only used after the validity check (to compute
//        the encoded value for VM display).
//
// Read/write paths are intentionally minimal here -- the heavy graphics
// flows (LZ77 import/export, dark-map preview, decrease-color tool,
// border bitmap rendering, source-file resource cache) are KnownGap-marked
// at the AXAML layer (disabled button + tooltip). No follow-up issues
// per task scope discipline.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapImageViewModel : ViewModelBase
    {
        // Stride for the two pointer-backed tables (matches WF
        // WorldMapImageForm.Border_Init / ICON_Init record-size args).
        const int BorderRecordStride = 12;
        const int IconRecordStride = 16;

        // Safety cap so a corrupt RomInfo pointer can't loop until OOM.
        const int LoadListHardCap = 4096;

        // ---- 13 canonical pointer NUDs (the top-row "ポインタ" slots) ----
        uint _mainImagePtr;
        uint _mainPalettePtr;
        uint _mainDarkPalettePtr;
        uint _mainPaletteMapPtr;
        uint _eventImagePtr;
        uint _eventPalettePtr;
        uint _eventTsaPtr;
        uint _miniImagePtr;
        uint _miniPalettePtr;
        uint _point1ImagePtr;
        uint _point2ImagePtr;
        uint _roadImagePtr;
        uint _iconPalettePtr;

        public uint MainImagePtr { get => _mainImagePtr; set => SetField(ref _mainImagePtr, value); }
        public uint MainPalettePtr { get => _mainPalettePtr; set => SetField(ref _mainPalettePtr, value); }
        public uint MainDarkPalettePtr { get => _mainDarkPalettePtr; set => SetField(ref _mainDarkPalettePtr, value); }
        public uint MainPaletteMapPtr { get => _mainPaletteMapPtr; set => SetField(ref _mainPaletteMapPtr, value); }
        public uint EventImagePtr { get => _eventImagePtr; set => SetField(ref _eventImagePtr, value); }
        public uint EventPalettePtr { get => _eventPalettePtr; set => SetField(ref _eventPalettePtr, value); }
        public uint EventTsaPtr { get => _eventTsaPtr; set => SetField(ref _eventTsaPtr, value); }
        public uint MiniImagePtr { get => _miniImagePtr; set => SetField(ref _miniImagePtr, value); }
        public uint MiniPalettePtr { get => _miniPalettePtr; set => SetField(ref _miniPalettePtr, value); }
        public uint Point1ImagePtr { get => _point1ImagePtr; set => SetField(ref _point1ImagePtr, value); }
        public uint Point2ImagePtr { get => _point2ImagePtr; set => SetField(ref _point2ImagePtr, value); }
        public uint RoadImagePtr { get => _roadImagePtr; set => SetField(ref _roadImagePtr, value); }
        public uint IconPalettePtr { get => _iconPalettePtr; set => SetField(ref _iconPalettePtr, value); }

        // ---- Border record (12 bytes) ----
        uint _borderP0, _borderP4, _borderW8, _borderW10, _borderCurrentAddr;
        // Read-config indicators (populated by LoadBorderList; surface
        // BaseAddress + DataCount mirroring WF InputFormRef semantics).
        uint _borderReadStartAddress;
        int _borderReadCount;

        public uint BorderP0 { get => _borderP0; set => SetField(ref _borderP0, value); }
        public uint BorderP4 { get => _borderP4; set => SetField(ref _borderP4, value); }
        public uint BorderW8 { get => _borderW8; set => SetField(ref _borderW8, value); }
        public uint BorderW10 { get => _borderW10; set => SetField(ref _borderW10, value); }
        public uint BorderCurrentAddr { get => _borderCurrentAddr; set => SetField(ref _borderCurrentAddr, value); }
        public uint BorderReadStartAddress { get => _borderReadStartAddress; set => SetField(ref _borderReadStartAddress, value); }
        public int BorderReadCount { get => _borderReadCount; set => SetField(ref _borderReadCount, value); }

        // ---- Icon record (16 bytes) ----
        // B0..B3 (bytes at +0..+3), P4 (pointer at +4), B8..B13 (bytes), W14 (u16 at +14).
        byte _iconB0, _iconB1, _iconB2, _iconB3;
        uint _iconP4;
        byte _iconB8, _iconB9, _iconB10, _iconB11, _iconB12, _iconB13;
        ushort _iconW14;
        uint _iconCurrentAddr;
        // Read-config indicators (populated by LoadIconList; mirrors WF
        // InputFormRef BaseAddress + DataCount).
        uint _iconReadStartAddress;
        int _iconReadCount;

        public byte IconB0 { get => _iconB0; set => SetField(ref _iconB0, value); }
        public byte IconB1 { get => _iconB1; set => SetField(ref _iconB1, value); }
        public byte IconB2 { get => _iconB2; set => SetField(ref _iconB2, value); }
        public byte IconB3 { get => _iconB3; set => SetField(ref _iconB3, value); }
        public uint IconP4 { get => _iconP4; set => SetField(ref _iconP4, value); }
        public byte IconB8 { get => _iconB8; set => SetField(ref _iconB8, value); }
        public byte IconB9 { get => _iconB9; set => SetField(ref _iconB9, value); }
        public byte IconB10 { get => _iconB10; set => SetField(ref _iconB10, value); }
        public byte IconB11 { get => _iconB11; set => SetField(ref _iconB11, value); }
        public byte IconB12 { get => _iconB12; set => SetField(ref _iconB12, value); }
        public byte IconB13 { get => _iconB13; set => SetField(ref _iconB13, value); }
        public ushort IconW14 { get => _iconW14; set => SetField(ref _iconW14, value); }
        public uint IconCurrentAddr { get => _iconCurrentAddr; set => SetField(ref _iconCurrentAddr, value); }
        public uint IconReadStartAddress { get => _iconReadStartAddress; set => SetField(ref _iconReadStartAddress, value); }
        public int IconReadCount { get => _iconReadCount; set => SetField(ref _iconReadCount, value); }

        bool _isLoaded;
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        // ===================================================================
        // Canonical pointer slots (load + write all 13 in one undo scope).
        // ===================================================================

        /// <summary>
        /// Load all 13 canonical pointer slots from <see cref="ROMFEINFO"/>.
        /// VM stores them as encoded GBA pointers (matches what the NUDs
        /// display).
        /// </summary>
        public void LoadAll()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) { IsLoaded = false; return; }

            MainImagePtr = rom.u32(rom.RomInfo.worldmap_big_image_pointer);
            MainPalettePtr = rom.u32(rom.RomInfo.worldmap_big_palette_pointer);
            MainDarkPalettePtr = rom.u32(rom.RomInfo.worldmap_big_dpalette_pointer);
            MainPaletteMapPtr = rom.u32(rom.RomInfo.worldmap_big_palettemap_pointer);
            EventImagePtr = rom.u32(rom.RomInfo.worldmap_event_image_pointer);
            EventPalettePtr = rom.u32(rom.RomInfo.worldmap_event_palette_pointer);
            EventTsaPtr = rom.u32(rom.RomInfo.worldmap_event_tsa_pointer);
            MiniImagePtr = rom.u32(rom.RomInfo.worldmap_mini_image_pointer);
            MiniPalettePtr = rom.u32(rom.RomInfo.worldmap_mini_palette_pointer);
            Point1ImagePtr = rom.u32(rom.RomInfo.worldmap_icon1_pointer);
            Point2ImagePtr = rom.u32(rom.RomInfo.worldmap_icon2_pointer);
            RoadImagePtr = rom.u32(rom.RomInfo.worldmap_road_tile_pointer);
            IconPalettePtr = rom.u32(rom.RomInfo.worldmap_icon_palette_pointer);
            IsLoaded = true;
        }

        /// <summary>
        /// Per Copilot CLI plan-review v1 -&gt; v2 finding C1: write ALL 13
        /// canonical pointer slots back to ROM in one call. Caller wraps
        /// this in <c>UndoService.Begin/Commit/Rollback</c>.
        /// </summary>
        public bool WriteAllPointers()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;
            rom.write_u32(rom.RomInfo.worldmap_big_image_pointer, MainImagePtr);
            rom.write_u32(rom.RomInfo.worldmap_big_palette_pointer, MainPalettePtr);
            rom.write_u32(rom.RomInfo.worldmap_big_dpalette_pointer, MainDarkPalettePtr);
            rom.write_u32(rom.RomInfo.worldmap_big_palettemap_pointer, MainPaletteMapPtr);
            rom.write_u32(rom.RomInfo.worldmap_event_image_pointer, EventImagePtr);
            rom.write_u32(rom.RomInfo.worldmap_event_palette_pointer, EventPalettePtr);
            rom.write_u32(rom.RomInfo.worldmap_event_tsa_pointer, EventTsaPtr);
            rom.write_u32(rom.RomInfo.worldmap_mini_image_pointer, MiniImagePtr);
            rom.write_u32(rom.RomInfo.worldmap_mini_palette_pointer, MiniPalettePtr);
            rom.write_u32(rom.RomInfo.worldmap_icon1_pointer, Point1ImagePtr);
            rom.write_u32(rom.RomInfo.worldmap_icon2_pointer, Point2ImagePtr);
            rom.write_u32(rom.RomInfo.worldmap_road_tile_pointer, RoadImagePtr);
            rom.write_u32(rom.RomInfo.worldmap_icon_palette_pointer, IconPalettePtr);
            return true;
        }

        // ===================================================================
        // Border table (worldmap_county_border_pointer)
        // ===================================================================

        /// <summary>
        /// Enumerate the border table. Terminator predicate matches WF
        /// `WorldMapImageForm.Border_Init`:
        /// <c>U.isPointer(rom.u32(addr)) &amp;&amp; U.isPointer(rom.u32(addr + 4))</c>.
        /// Per Copilot CLI plan-review C2, the scan reads raw u32 (NOT p32)
        /// and only validates pointer-ness with <see cref="U.isPointer"/>.
        /// </summary>
        public List<AddrResult> LoadBorderList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                BorderReadStartAddress = 0;
                BorderReadCount = 0;
                return result;
            }

            uint basePtr = rom.RomInfo.worldmap_county_border_pointer;
            if (basePtr == 0)
            {
                BorderReadStartAddress = 0;
                BorderReadCount = 0;
                return result;
            }
            uint baseAddr = rom.p32(basePtr);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                BorderReadStartAddress = 0;
                BorderReadCount = 0;
                return result;
            }

            // Surface BaseAddress (as encoded GBA pointer for the X8
            // hex display) and DataCount to mirror WF InputFormRef
            // semantics (Copilot bot inline review on PR #592 round 2).
            BorderReadStartAddress = U.toPointer(baseAddr);

            uint addr = baseAddr;
            for (int i = 0; i < LoadListHardCap; i++, addr += BorderRecordStride)
            {
                if (addr + BorderRecordStride > (uint)rom.Data.Length) break;
                uint a = rom.u32(addr);
                uint b = rom.u32(addr + 4);
                if (!U.isPointer(a) || !U.isPointer(b)) break;
                result.Add(new AddrResult(addr, U.ToHexString(i), 0));
            }
            BorderReadCount = result.Count;
            return result;
        }

        /// <summary>Load one 12-byte border record into VM properties.</summary>
        public void LoadBorderEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            BorderCurrentAddr = addr;
            // P0 (pointer at +0) and P4 (pointer at +4) are stored as
            // encoded GBA pointers (matches WF NumericUpDown.Hexadecimal).
            BorderP0 = rom.u32(addr + 0);
            BorderP4 = rom.u32(addr + 4);
            // W8 / W10 are the two halves of u32 at +8 (little-endian):
            //   low half  -> W8 (X origin)
            //   high half -> W10 (Y origin)
            uint w = rom.u32(addr + 8);
            BorderW8 = w & 0xFFFFu;
            BorderW10 = (w >> 16) & 0xFFFFu;
        }

        /// <summary>Write the 4 NUDs back into the 12-byte record.</summary>
        public bool WriteBorder()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || BorderCurrentAddr == 0) return false;
            rom.write_u32(BorderCurrentAddr + 0, BorderP0);
            rom.write_u32(BorderCurrentAddr + 4, BorderP4);
            uint w = (BorderW8 & 0xFFFFu) | ((BorderW10 & 0xFFFFu) << 16);
            rom.write_u32(BorderCurrentAddr + 8, w);
            return true;
        }

        // ===================================================================
        // Icon-data table (worldmap_icon_data_pointer)
        // ===================================================================

        /// <summary>
        /// Enumerate the icon-data table. Terminator predicate matches WF
        /// `WorldMapImageForm.ICON_Init`:
        /// <c>U.isPointer(rom.u32(addr + 4))</c>.
        /// Per Copilot CLI plan-review C2, the scan reads raw u32 (NOT p32).
        /// </summary>
        public List<AddrResult> LoadIconList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                IconReadStartAddress = 0;
                IconReadCount = 0;
                return result;
            }

            uint basePtr = rom.RomInfo.worldmap_icon_data_pointer;
            if (basePtr == 0)
            {
                IconReadStartAddress = 0;
                IconReadCount = 0;
                return result;
            }
            uint baseAddr = rom.p32(basePtr);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                IconReadStartAddress = 0;
                IconReadCount = 0;
                return result;
            }

            // Surface BaseAddress (as encoded GBA pointer) and DataCount
            // to mirror WF InputFormRef semantics (Copilot bot inline
            // review on PR #592 round 2).
            IconReadStartAddress = U.toPointer(baseAddr);

            uint addr = baseAddr;
            for (int i = 0; i < LoadListHardCap; i++, addr += IconRecordStride)
            {
                if (addr + IconRecordStride > (uint)rom.Data.Length) break;
                uint p4 = rom.u32(addr + 4);
                if (!U.isPointer(p4)) break;
                result.Add(new AddrResult(addr, U.ToHexString(i), 0));
            }
            IconReadCount = result.Count;
            return result;
        }

        /// <summary>Load one 16-byte icon record into VM properties.</summary>
        public void LoadIconEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            IconCurrentAddr = addr;
            IconB0 = (byte)rom.u8(addr + 0);
            IconB1 = (byte)rom.u8(addr + 1);
            IconB2 = (byte)rom.u8(addr + 2);
            IconB3 = (byte)rom.u8(addr + 3);
            IconP4 = rom.u32(addr + 4);
            IconB8 = (byte)rom.u8(addr + 8);
            IconB9 = (byte)rom.u8(addr + 9);
            IconB10 = (byte)rom.u8(addr + 10);
            IconB11 = (byte)rom.u8(addr + 11);
            IconB12 = (byte)rom.u8(addr + 12);
            IconB13 = (byte)rom.u8(addr + 13);
            IconW14 = (ushort)rom.u16(addr + 14);
        }

        /// <summary>Write the 12 NUDs back into the 16-byte record.</summary>
        public bool WriteIcon()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || IconCurrentAddr == 0) return false;
            rom.write_u8(IconCurrentAddr + 0, IconB0);
            rom.write_u8(IconCurrentAddr + 1, IconB1);
            rom.write_u8(IconCurrentAddr + 2, IconB2);
            rom.write_u8(IconCurrentAddr + 3, IconB3);
            rom.write_u32(IconCurrentAddr + 4, IconP4);
            rom.write_u8(IconCurrentAddr + 8, IconB8);
            rom.write_u8(IconCurrentAddr + 9, IconB9);
            rom.write_u8(IconCurrentAddr + 10, IconB10);
            rom.write_u8(IconCurrentAddr + 11, IconB11);
            rom.write_u8(IconCurrentAddr + 12, IconB12);
            rom.write_u8(IconCurrentAddr + 13, IconB13);
            rom.write_u16(IconCurrentAddr + 14, IconW14);
            return true;
        }
    }
}
