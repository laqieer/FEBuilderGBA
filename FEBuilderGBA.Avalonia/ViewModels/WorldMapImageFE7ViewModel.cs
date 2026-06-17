// SPDX-License-Identifier: GPL-3.0-or-later
// World Map Image (FE7) editor ViewModel (#1184) — a port of WinForms
// WorldMapImageFE7Form. Exposes two read-only IImage previews (the 12-split
// big field map + the event image), the read-only big-map pointer values shown
// as display labels, and the FE7 big-map / event imports — all delegating to the
// pure ImageWorldMapCore helpers. There is NO pointer write-back: the imports
// write data RAW in-place (no repoint), so the pointer slots never change and the
// WF "Write Pointers" button is omitted (Copilot PR #1223 review). Mirrors the
// preview / import idiom of the base WorldMapImageViewModel.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class WorldMapImageFE7ViewModel : ViewModelBase
    {
        // FE7 big field map source dimensions (12-split 256×256 pieces).
        public const int BigWidth = 1024;
        public const int BigHeight = 688;
        // FE7 event image source dimensions (the 240×160 visible event map; the
        // reducer adds the 16-px right margin to reach the 256×160 canvas).
        public const int EventWidth = 240;
        public const int EventHeight = 160;

        uint _currentAddr;
        bool _isLoaded;
        bool _canImport;
        bool _canImportEvent;
        uint _bigImagePtr;
        uint _bigPalettePtr;
        uint _bigTsaPtr;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>True when the FE7 BIG-field-map import is allowed (FE7 ROM with
        /// resolvable big-map pointer tables + palette). FE6/FE8 → false.</summary>
        public bool CanImport { get => _canImport; set => SetField(ref _canImport, value); }

        /// <summary>True when the world-map EVENT-image import is allowed (FE7/FE8
        /// ROM with resolvable EVENT pointers — independent of the big-map pointers,
        /// Copilot PR #1223 review). FE6 → false.</summary>
        public bool CanImportEvent { get => _canImportEvent; set => SetField(ref _canImportEvent, value); }

        /// <summary>The raw <c>worldmap_big_image_pointer</c> slot value (pointer
        /// to the 12-entry image pointer array), shown read-only in the UI.</summary>
        public uint BigImagePointer { get => _bigImagePtr; set => SetField(ref _bigImagePtr, value); }

        /// <summary>The raw <c>worldmap_big_palette_pointer</c> slot value.</summary>
        public uint BigPalettePointer { get => _bigPalettePtr; set => SetField(ref _bigPalettePtr, value); }

        /// <summary>The raw <c>worldmap_big_palettemap_pointer</c> slot value
        /// (the FE7 "TSA 12-split" pointer array, NOT a palette-map).</summary>
        public uint BigTsaPointer { get => _bigTsaPtr; set => SetField(ref _bigTsaPtr, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "World Map Image (FE7)", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            CurrentAddr = addr;
            BigImagePointer = rom.u32(rom.RomInfo.worldmap_big_image_pointer);
            BigPalettePointer = rom.u32(rom.RomInfo.worldmap_big_palette_pointer);
            BigTsaPointer = rom.u32(rom.RomInfo.worldmap_big_palettemap_pointer);
            // Gate each import independently (NOT a full render — that allocates a
            // 1024×688 surface and would couple the button-enable to the render
            // pipeline). The big-map and event imports depend on DIFFERENT pointer
            // sets, so they have DIFFERENT gates (Copilot PR #1223 review).
            CanImport = ImageWorldMapCore.CanImportFE7BigFieldMap(rom);
            CanImportEvent = ImageWorldMapCore.CanImportEvent(rom);
            IsLoaded = true;
        }

        // ===================================================================
        // Live previews (READ-ONLY) — null-safe delegates to ImageWorldMapCore.
        // ===================================================================

        /// <summary>Render the FE7 BIG FIELD MAP preview (1024×688) via
        /// <see cref="ImageWorldMapCore.TryRenderFE7BigFieldMap"/>. FE7-only
        /// (FE6/FE8 → null). Null on any failure.</summary>
        public IImage TryRenderBigFieldMap() => ImageWorldMapCore.TryRenderFE7BigFieldMap(CoreState.ROM);

        /// <summary>Render the EVENT preview (256×160) via the version-agnostic
        /// <see cref="ImageWorldMapCore.TryRenderEvent"/>. Null on any failure.</summary>
        public IImage TryRenderEvent() => ImageWorldMapCore.TryRenderEvent(CoreState.ROM);

        // ===================================================================
        // Import (ROM-MUTATING — run under the View's ambient UndoService scope;
        // these methods take NO Undo.UndoData so no range is double-recorded
        // (Copilot #1184 plan review #3)).
        // ===================================================================

        /// <summary>Import a 1024×688 big-field-map from RGBA pixels via
        /// <see cref="ImageWorldMapCore.ImportFE7BigFieldMap"/>. Returns "" on
        /// success or a non-empty error (ZERO ROM mutation on failure).</summary>
        public string ImportBigFieldMap(byte[] rgba, int width, int height)
            => ImageWorldMapCore.ImportFE7BigFieldMap(CoreState.ROM, rgba, width, height);

        /// <summary>Import a 240×160 event image via the version-agnostic
        /// <see cref="ImageWorldMapCore.ImportEvent"/> (now FE7+FE8). Returns "" on
        /// success or a non-empty error (ZERO ROM mutation on failure).</summary>
        public string ImportEvent(byte[] rgba, int width, int height)
        {
            bool ok = ImageWorldMapCore.ImportEvent(CoreState.ROM, rgba, width, height, out string error);
            return ok ? "" : (string.IsNullOrEmpty(error) ? "Import failed." : error);
        }
    }
}
