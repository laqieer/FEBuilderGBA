// SPDX-License-Identifier: GPL-3.0-or-later
// #1032 — Cross-platform Core renderer for the Class OP Demo editor's N1
// JP-name font-glyph image preview. Port of WinForms
// OPClassFontForm.DrawFontByID + DrawFont (READ-ONLY, never mutates the ROM).
//
// The OP-class-font table at p32(op_class_font_pointer) is an array of 4-byte
// glyph-image pointers (WF InputFormRef BlockSize 4); each glyph image is
// LZ77-compressed 4bpp tile data rendered 32x32 (4x4 tiles) with the 16-color
// op_class_font_palette.
using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// READ-ONLY cross-platform renderer for the OP-class JP-name font glyphs
    /// (port of WinForms <c>OPClassFontForm.DrawFontByID</c> + <c>DrawFont</c>).
    /// </summary>
    public static class ClassOPDemoFontRenderCore
    {
        /// <summary>Generous upper bound on the OP-class-font table size (the DataCount scan).</summary>
        const uint ScanCap = 0x4000;

        /// <summary>
        /// Render the OP-class JP-name font glyph for table index <paramref name="id"/>.
        /// Cross-platform port of WF OPClassFontForm.DrawFontByID + DrawFont: the table
        /// at p32(op_class_font_pointer) is an array of 4-byte glyph-image pointers
        /// (WF InputFormRef BlockSize 4); IDToAddr(id) = base + id*4; the glyph image is
        /// LZ77-compressed 4bpp tile data rendered 32x32 (4x4 tiles) with the
        /// op_class_font_palette. Returns null (caller clears the preview) for an id past
        /// the contiguous pointer run / a non-pointer (blank) slot / unsafe addresses /
        /// failed decompress. Never throws.
        /// </summary>
        public static IImage RenderGlyphById(ROM rom, uint id)
        {
            if (rom?.RomInfo == null || CoreState.ImageService == null) return null;
            uint tablePtr = rom.RomInfo.op_class_font_pointer;
            if (!U.isSafetyOffset(tablePtr, rom)) return null;
            uint tableBase = rom.p32(tablePtr);
            if (!U.isSafetyOffset(tableBase, rom)) return null;

            // WF IDToAddr returns NOT_FOUND for id >= DataCount, where DataCount is the
            // contiguous run of pointer entries from the base. Scan (bounded) and reject
            // an id at/after the first non-pointer terminator. No base+id*4 multiply
            // before this bound, so a huge uint id can't wrap.
            uint glyphCount = 0;
            for (uint i = 0; i < ScanCap; i++)
            {
                uint slot = tableBase + i * 4;
                if (slot + 4 > (uint)rom.Data.Length) break;     // EOF / overflow-safe
                if (!U.isPointer(rom.u32(slot))) break;          // terminator
                glyphCount = i + 1;
            }
            if (id >= glyphCount) return null;

            uint image = rom.u32(tableBase + id * 4);
            return RenderGlyphImage(rom, image);
        }

        /// <summary>
        /// Render a single OP-class font glyph from its image pointer (WF DrawFont):
        /// LZ77-decompress the 4bpp tile data and render 32x32 (4x4 tiles) with the
        /// 16-color op_class_font_palette read straight from rom.Data. Returns null for a
        /// non-pointer/unsafe image or a failed decompress. Exposed for reuse by the OP
        /// Class Font viewer (parity follow-up). Never throws.
        /// </summary>
        public static IImage RenderGlyphImage(ROM rom, uint image)
        {
            if (rom?.RomInfo == null || CoreState.ImageService == null) return null;
            if (!U.isPointer(image)) return null;                // WF BlankDummy → null preview
            uint imageOff = U.toOffset(image);
            if (!U.isSafetyOffset(imageOff, rom)) return null;
            byte[] tileData = LZ77.decompress(rom.Data, imageOff);
            if (tileData == null || tileData.Length == 0) return null;
            uint palette = rom.p32(rom.RomInfo.op_class_font_palette_pointer);
            // 4x4 tiles = 32x32, single 16-color palette read straight from rom.Data
            // (mirrors WF DrawFont's ByteToImage16Tile(32,32, imageUZ,0, rom.Data, palOff)).
            return ImageUtilCore.ByteToImage16Tile(tileData, 0, rom.Data, (int)U.toOffset(palette), 32, 32);
        }
    }
}
