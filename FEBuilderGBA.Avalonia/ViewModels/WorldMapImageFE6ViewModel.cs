// SPDX-License-Identifier: GPL-3.0-or-later
// World Map Image (FE6) editor ViewModel (#1183) — a port of WinForms
// WorldMapImageFE6Form. The FE6 world map is a FLAT 256-color LINEAR raster (NOT
// the FE7/FE8 header-TSA), with FIVE zoom views (full + 4 quadrants NW/NE/SW/SE),
// each reading a CONSECUTIVE pointer slot: image at worldmap_big_image_pointer +
// {0,8,16,24,32} and palette at worldmap_big_palette_pointer + {0,8,16,24,32}.
//
// Exposes: five read-only 240x160 IImage zoom previews; per-zoom 256-linear
// import (LZ77 image + palette, both repointed — WF ZLINER256IMAGE+ZPALETTE
// parity) gated by per-zoom CanImport; the five editable image + five editable
// palette pointer OFFSET values + WritePointers (the WF "AllWriteButton"). All
// ROM-mutating work delegates to the pure ImageWorldMapCore helpers.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapImageFE6ViewModel : ViewModelBase
    {
        // FE6 big field map render dimensions (one GBA screen).
        public const int ZoomWidth = 240;
        public const int ZoomHeight = 160;

        // The five consecutive pointer-slot byte offsets: full + 4 quadrants.
        public const uint SlotFull = 0;
        public const uint SlotNW = 8;
        public const uint SlotNE = 16;
        public const uint SlotSW = 24;
        public const uint SlotSE = 32;
        public static readonly uint[] AllSlots = { SlotFull, SlotNW, SlotNE, SlotSW, SlotSE };

        uint _currentAddr;
        bool _isLoaded;
        bool _canRender;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>True when the FE6 zoom previews can render: a FE6 ROM whose
        /// slot-0 (full map) BOTH image AND palette pointers resolve to in-bounds
        /// offsets (rendering needs both LZ77 streams). FE7/FE8 → false. Mirrors
        /// <see cref="ImageWorldMapCore.CanRenderFE6BigFieldMap"/>.</summary>
        public bool CanRender { get => _canRender; set => SetField(ref _canRender, value); }

        // Per-zoom import gates: FE6 ROM with the image + palette pointer SLOTS at
        // that zoom WRITABLE (in-bounds for the write_p32 repoint, via IsRegionSafe)
        // — NOT the currently-pointed targets resolving, so the corrupt-pointer
        // repair case stays importable (Copilot #1183 review). Mirrors
        // ImageWorldMapCore.CanImportFE6BigFieldMap.
        bool _canImportFull, _canImportNW, _canImportNE, _canImportSW, _canImportSE;
        public bool CanImportFull { get => _canImportFull; set => SetField(ref _canImportFull, value); }
        public bool CanImportNW { get => _canImportNW; set => SetField(ref _canImportNW, value); }
        public bool CanImportNE { get => _canImportNE; set => SetField(ref _canImportNE, value); }
        public bool CanImportSW { get => _canImportSW; set => SetField(ref _canImportSW, value); }
        public bool CanImportSE { get => _canImportSE; set => SetField(ref _canImportSE, value); }

        // The five editable IMAGE pointer OFFSET values (rom.p32(slot) returns an
        // OFFSET; WritePointers writes them back via write_p32 which re-encodes to
        // a GBA pointer — matching WF ImageFormRef's NUD/WriteOnePointer semantics).
        uint _imgFull, _imgNW, _imgNE, _imgSW, _imgSE;
        public uint ImagePtrFull { get => _imgFull; set => SetField(ref _imgFull, value); }
        public uint ImagePtrNW { get => _imgNW; set => SetField(ref _imgNW, value); }
        public uint ImagePtrNE { get => _imgNE; set => SetField(ref _imgNE, value); }
        public uint ImagePtrSW { get => _imgSW; set => SetField(ref _imgSW, value); }
        public uint ImagePtrSE { get => _imgSE; set => SetField(ref _imgSE, value); }

        // The five editable PALETTE pointer OFFSET values.
        uint _palFull, _palNW, _palNE, _palSW, _palSE;
        public uint PalettePtrFull { get => _palFull; set => SetField(ref _palFull, value); }
        public uint PalettePtrNW { get => _palNW; set => SetField(ref _palNW, value); }
        public uint PalettePtrNE { get => _palNE; set => SetField(ref _palNE, value); }
        public uint PalettePtrSW { get => _palSW; set => SetField(ref _palSW, value); }
        public uint PalettePtrSE { get => _palSE; set => SetField(ref _palSE, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "World Map Image (FE6)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) { IsLoaded = false; return; }

            CurrentAddr = addr;

            // Seed the editable pointer fields from the current slot values. p32
            // returns an OFFSET (not the encoded GBA pointer); WritePointers writes
            // it back through write_p32, which re-encodes (WF WriteOnePointer parity).
            uint imgBase = rom.RomInfo.worldmap_big_image_pointer;
            uint palBase = rom.RomInfo.worldmap_big_palette_pointer;
            ImagePtrFull = SafeP32(rom, imgBase + SlotFull);
            ImagePtrNW = SafeP32(rom, imgBase + SlotNW);
            ImagePtrNE = SafeP32(rom, imgBase + SlotNE);
            ImagePtrSW = SafeP32(rom, imgBase + SlotSW);
            ImagePtrSE = SafeP32(rom, imgBase + SlotSE);
            PalettePtrFull = SafeP32(rom, palBase + SlotFull);
            PalettePtrNW = SafeP32(rom, palBase + SlotNW);
            PalettePtrNE = SafeP32(rom, palBase + SlotNE);
            PalettePtrSW = SafeP32(rom, palBase + SlotSW);
            PalettePtrSE = SafeP32(rom, palBase + SlotSE);

            CanRender = ImageWorldMapCore.CanRenderFE6BigFieldMap(rom);
            CanImportFull = ImageWorldMapCore.CanImportFE6BigFieldMap(rom, SlotFull);
            CanImportNW = ImageWorldMapCore.CanImportFE6BigFieldMap(rom, SlotNW);
            CanImportNE = ImageWorldMapCore.CanImportFE6BigFieldMap(rom, SlotNE);
            CanImportSW = ImageWorldMapCore.CanImportFE6BigFieldMap(rom, SlotSW);
            CanImportSE = ImageWorldMapCore.CanImportFE6BigFieldMap(rom, SlotSE);
            IsLoaded = true;
        }

        /// <summary>Bounds-safe p32: returns 0 when the slot is out of range
        /// (never throws) so LoadEntry can't crash on a truncated/corrupt ROM.</summary>
        static uint SafeP32(ROM rom, uint slot)
        {
            if (rom?.Data == null) return 0;
            if ((long)slot + 4 > rom.Data.Length) return 0;
            return rom.p32(slot);
        }

        // ===================================================================
        // Live previews (READ-ONLY) — null-safe delegates to ImageWorldMapCore.
        // ===================================================================

        public IImage TryRenderZoomOut() => ImageWorldMapCore.TryRenderFE6BigFieldMap(CoreState.ROM, SlotFull);
        public IImage TryRenderZoomNW() => ImageWorldMapCore.TryRenderFE6BigFieldMap(CoreState.ROM, SlotNW);
        public IImage TryRenderZoomNE() => ImageWorldMapCore.TryRenderFE6BigFieldMap(CoreState.ROM, SlotNE);
        public IImage TryRenderZoomSW() => ImageWorldMapCore.TryRenderFE6BigFieldMap(CoreState.ROM, SlotSW);
        public IImage TryRenderZoomSE() => ImageWorldMapCore.TryRenderFE6BigFieldMap(CoreState.ROM, SlotSE);

        /// <summary>Render the zoom view for a specific slot byte offset.</summary>
        public IImage TryRenderZoom(uint slotByteOffset)
            => ImageWorldMapCore.TryRenderFE6BigFieldMap(CoreState.ROM, slotByteOffset);

        // ===================================================================
        // Import (ROM-MUTATING — run under the View's ambient UndoService scope;
        // takes NO Undo.UndoData so no range is double-recorded — Copilot #1184
        // plan review #3). Returns "" on success or a non-empty error (ZERO ROM
        // mutation on failure).
        // ===================================================================

        public string ImportZoom(uint slotByteOffset, byte[] rgba, int width, int height)
            => ImageWorldMapCore.ImportFE6BigFieldMap(CoreState.ROM, slotByteOffset, rgba, width, height);

        // ===================================================================
        // Write the five image + five palette pointer OFFSET values back to their
        // slots (the WF "AllWriteButton" → WMZoom*.WritePointer). Writes the EDITED
        // value via write_p32 (which re-encodes the offset to a GBA pointer),
        // mirroring WF InputFormRef.WriteOnePointer — NOT a re-write of p32(slot)
        // (that was the #1184 no-op bug). The writes land in the caller's ambient
        // undo scope; the explicit undoData overload is also passed so the range is
        // recorded when an ambient scope is NOT active.
        // ===================================================================

        public void WritePointers(Undo.UndoData undoData)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            uint imgBase = rom.RomInfo.worldmap_big_image_pointer;
            uint palBase = rom.RomInfo.worldmap_big_palette_pointer;

            WriteOnePointer(rom, imgBase + SlotFull, ImagePtrFull, undoData);
            WriteOnePointer(rom, imgBase + SlotNW, ImagePtrNW, undoData);
            WriteOnePointer(rom, imgBase + SlotNE, ImagePtrNE, undoData);
            WriteOnePointer(rom, imgBase + SlotSW, ImagePtrSW, undoData);
            WriteOnePointer(rom, imgBase + SlotSE, ImagePtrSE, undoData);
            WriteOnePointer(rom, palBase + SlotFull, PalettePtrFull, undoData);
            WriteOnePointer(rom, palBase + SlotNW, PalettePtrNW, undoData);
            WriteOnePointer(rom, palBase + SlotNE, PalettePtrNE, undoData);
            WriteOnePointer(rom, palBase + SlotSW, PalettePtrSW, undoData);
            WriteOnePointer(rom, palBase + SlotSE, PalettePtrSE, undoData);
        }

        /// <summary>Write one edited pointer OFFSET to its slot, bounds-checked.
        /// Uses the undoData overload when provided, else the ambient overload
        /// (the UndoService scope captures it either way).</summary>
        static void WriteOnePointer(ROM rom, uint slot, uint editedOffset, Undo.UndoData undoData)
        {
            if (rom?.Data == null) return;
            if ((long)slot + 4 > rom.Data.Length) return;
            if (undoData != null)
                rom.write_p32(slot, editedOffset, undoData);
            else
                rom.write_p32(slot, editedOffset);
        }
    }
}
