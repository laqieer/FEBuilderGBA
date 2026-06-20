// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform Chinese-font glyph editor seam (#1166) — the Avalonia ZH Font
// editor's list/render/PNG-import half of WinForms FontZHForm.cs.
//
// The Chinese ROMs (FE6/FE7/FE8 multibyte) store fonts in a DIFFERENT structure
// than the main game font (FontGlyphRenderCore / #1165): instead of a hash-table
// of next-pointer chains, glyphs are a flat, directly-referenced array keyed by a
// computed "codeB" offset. This seam ports the GUI-free math:
//
//   * GetFontPointerZH(version,isItemFont) — the hardcoded item/serif table heads
//     (FE6 0x58ef28/0x58ef54, FE7 0xbc0698/0xbc06c4, FE8 0x577ff4/0x578020).
//   * CalcCodeB(sjis) — the 0x54-stride offset math (FontZHForm.CalcCodeB):
//     codeA=sjis&0xff, codeB=(sjis>>8)&0xff;
//     ((codeA-0x81)*0x80 + (codeB-0x80)) * 0x54.
//   * FindGlyphZH(rom,isItemFont,moji) — addr = topaddress + codeB (DIRECT
//     reference, no chain walk).
//   * EnumerateGlyphsZH(rom,isItemFont) — iterate the codeB map built from the ZH
//     TBL encoder's GetTBLEncodeDicLow (FontZHForm.MakeCodeBMap/MakeAllDataLengthInner).
//   * RenderGlyphZH(rom,addr,isItemFont) — port of ImageUtil.ByteToImage4ZH: decode
//     the 40-byte 2bpp bitmap at addr+4 to a 16x13 RGBA IImage. NOTE the ZH palette
//     order (0=bg, 1=white, 2=gray, 3=black) DIFFERS from the main font, the row
//     height is 13 (0xD) and x advances by 1 per pixel (width is not a multiple of 4).
//   * PackGlyphZHBytes(indexedPixels,width) — port of ImageUtil.Image4ToByteZH
//     (16-wide 2bpp pack capped at 40 bytes).
//   * ImportGlyphZH(...) — ROM-MUTATING port of FontZHForm.WriteButton_Click:
//     validate-all-before-mutate, then update the directly-referenced slot at
//     topaddress+codeB IN PLACE (the ZH table is pre-sized; an out-of-range slot is
//     an error, never an append — every glyph has a fixed slot). Byte-identical fault
//     restore (#885/#923) + ambient undo threading, mirroring FontGlyphRenderCore.ImportGlyph.
//
// The ZH glyph struct is 44 bytes:
//   byte0 unk1 (0xD) | byte1 width | byte2 height (0xD) | byte3 0x00 | 40-byte bitmap
// The bitmap is 2 bits/pixel (4 colors), 16 pixels wide; 40 bytes = 160 px = the
// first 10 rows of a 16x13 glyph (the remaining rows are blank).
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// One enumerated Chinese-font glyph. <see cref="Moji"/> is the engine
    /// character code (little-endian, as stored in the ZH TBL encoder), needed to
    /// re-import a glyph (see <see cref="FontGlyphZHCore.ImportGlyphZH"/>).
    /// </summary>
    public sealed class FontGlyphZHEntry
    {
        /// <summary>ROM offset of the 44-byte glyph struct (width @ +1, 40-byte bitmap @ +4).</summary>
        public uint Addr;
        /// <summary>Engine character code (little-endian SJIS-style code from the ZH TBL).</summary>
        public uint Moji;
        /// <summary>Glyph advance width (byte @ Addr+1).</summary>
        public int Width;
        /// <summary>Decoded character (from the ZH TBL) — the display label.</summary>
        public string Name = "";
    }

    /// <summary>
    /// GUI-free Chinese-font glyph helpers (#1166). READ-ONLY enumeration + render,
    /// plus one ROM-MUTATING <see cref="ImportGlyphZH"/>. Every ROM access is
    /// bounds-guarded; nothing throws on a bad ROM.
    /// </summary>
    public static class FontGlyphZHCore
    {
        // The 40-byte 2bpp bitmap is 16 wide; 40 bytes = 160 px = 10 rows. The glyph
        // box is 16x13 (the bottom 3 rows stay blank).
        public const int GLYPH_W = 16;
        public const int GLYPH_H = 13;            // 0xD
        public const int GLYPH_BITMAP_BYTES = 40; // 2bpp, capped at 40 (Image4ToByteZH)
        public const int GLYPH_STRUCT_BYTES = 4 + GLYPH_BITMAP_BYTES; // 44

        // Struct field offsets.
        const uint OFF_WIDTH = 1;   // byte1 = width
        const uint OFF_BITMAP = 4;  // 40-byte bitmap

        // ---- Fixed ZH font palette (matches WF ImageUtil.ByteToImage4ZH) ----
        // Index 0 = background (item vs serif differ), 1 = WHITE, 2 = GRAY, 3 = BLACK.
        // NOTE: this order (white before gray) DIFFERS from the main font palette.
        static readonly (byte R, byte G, byte B) ItemBg  = (0x68, 0x88, 0xA8);
        static readonly (byte R, byte G, byte B) SerifBg = (0xE0, 0xE0, 0xE0);
        static readonly (byte R, byte G, byte B) White   = (0xF8, 0xF8, 0xF8);
        static readonly (byte R, byte G, byte B) Gray    = (0xA8, 0xA8, 0xA7);
        static readonly (byte R, byte G, byte B) Black    = (0x28, 0x28, 0x28);

        static (byte R, byte G, byte B) BgColor(bool isItemFont) => isItemFont ? ItemBg : SerifBg;

        /// <summary>
        /// The 4 ZH font colors as a 4-entry GBA palette (2 bytes/color, BGR555 LE).
        /// Used by the Avalonia importer to remap a PNG onto the ZH font palette with
        /// colorCount=4 (so quantized indices stay 0..3 — same trick as #1165).
        /// </summary>
        public static byte[] GetFontPaletteGBA(bool isItemFont)
        {
            var colors = new[] { BgColor(isItemFont), White, Gray, Black };
            byte[] pal = new byte[4 * 2];
            for (int i = 0; i < 4; i++)
            {
                ushort gba = RgbToGba555(colors[i].R, colors[i].G, colors[i].B);
                pal[i * 2 + 0] = (byte)(gba & 0xFF);
                pal[i * 2 + 1] = (byte)((gba >> 8) & 0xFF);
            }
            return pal;
        }

        static ushort RgbToGba555(byte r, byte g, byte b)
        {
            return (ushort)(((r >> 3) & 0x1F) | (((g >> 3) & 0x1F) << 5) | (((b >> 3) & 0x1F) << 10));
        }

        // ====================================================================
        // ROM detection + font pointer table heads
        // ====================================================================

        /// <summary>
        /// True when this ROM is a Chinese build the ZH font editor can edit:
        /// multibyte FE6/FE7/FE8. (The Avalonia app keeps TextEncoding=Auto, so the
        /// editor self-detects from the ROM rather than the encoding option.)
        /// </summary>
        public static bool IsZHRom(ROM rom)
        {
            if (rom?.RomInfo == null) return false;
            if (!rom.RomInfo.is_multibyte) return false;
            int v = rom.RomInfo.version;
            return v == 6 || v == 7 || v == 8;
        }

        /// <summary>
        /// The hardcoded head of the item/serif ZH font table for the ROM version.
        /// Mirrors FontZHForm.GetFontPointer. Returns 0 for unsupported versions.
        /// </summary>
        public static uint GetFontPointerZH(int version, bool isItemFont)
        {
            switch (version)
            {
                case 6: return isItemFont ? 0x58ef28u : 0x58ef54u;
                case 7: return isItemFont ? 0xbc0698u : 0xbc06c4u;
                case 8: return isItemFont ? 0x577ff4u : 0x578020u;
                default: return 0;
            }
        }

        static uint GetFontPointerZH(ROM rom, bool isItemFont)
        {
            if (rom?.RomInfo == null) return 0;
            return GetFontPointerZH(rom.RomInfo.version, isItemFont);
        }

        // ====================================================================
        // codeB offset math — port of FontZHForm.FindFontDataZH / CalcCodeB
        // ====================================================================

        /// <summary>
        /// Compute the codeB byte-offset for an engine char code, or
        /// <see cref="U.NOT_FOUND"/> when the char can't have a glyph (control code
        /// or out of range). The address is <c>topaddress + codeB</c> (direct ref).
        /// Faithful port of FontZHForm.FindFontDataZH's offset math.
        /// </summary>
        public static uint CalcCodeB(uint sjis)
        {
            uint sjis1 = (sjis >> 8) & 0xff;
            uint sjis2 = sjis & 0xff;

            if (sjis1 == 0)
            {
                // Extended half-width alphabet font lives under 0x40.
                sjis1 = 0x40;
            }
            else if (sjis1 < 0x1f)
            {
                // Control codes (1st byte < 0x1F) have no font.
                return U.NOT_FOUND;
            }

            // codeA = 2nd byte, codeB = 1st byte (matches the WF variable naming).
            uint codeA = sjis2;
            uint codeB = sjis1;

            // WF range guard (kept verbatim, including its quirky &&-chain).
            if (codeA < 0x81 && codeA > 0x98 && codeB < 0x80)
            {
                return U.NOT_FOUND;
            }

            codeA -= 0x81;
            codeA *= 0x80;
            codeB -= 0x80;
            codeB += codeA;   // offset word
            codeB *= 0x54;    // glyph-struct stride (44 rounded to the ROM's 0x54)
            return codeB;
        }

        /// <summary>
        /// The RAW 2-arg codeB stride math — a VERBATIM port of <c>FontZHForm.CalcCodeB(codeA, codeB)</c>
        /// (no control-code / range guards; just the arithmetic). Used by the ROM-rebuild producer's
        /// <c>EmitFontZH</c> (which mirrors <c>FontZHForm.MakeCodeBMap</c> -&gt; <c>CalcCodeB(codeA, codeB)</c>
        /// over the TBL map exactly, including this raw form). Distinct from the guarded single-arg
        /// <see cref="CalcCodeB(uint)"/>, which the EDITOR uses to reject control codes / out-of-range chars
        /// (its <c>NOT_FOUND</c> returns would WRONGLY drop entries the producer must emit).
        /// </summary>
        public static uint CalcCodeBRaw(uint codeA, uint codeB)
        {
            codeA -= 0x81;
            codeA *= 0x80;
            codeB -= 0x80;
            codeB += codeA; // 偏移字数 オフセット語
            codeB *= 0x54;  // 字体大小 文字サイズ
            return codeB;
        }

        /// <summary>
        /// Direct-reference glyph address for <paramref name="moji"/> in the
        /// item/serif ZH font, or <see cref="U.NOT_FOUND"/> when the char can't have
        /// a glyph or the resulting address is out of range.
        /// </summary>
        public static uint FindGlyphZH(ROM rom, bool isItemFont, uint moji)
        {
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            uint topaddress = GetFontPointerZH(rom, isItemFont);
            if (!U.isSafetyOffset(topaddress, rom)) return U.NOT_FOUND;

            uint codeB = CalcCodeB(moji);
            if (codeB == U.NOT_FOUND) return U.NOT_FOUND;

            uint addr = topaddress + codeB;
            if (!GlyphStructFits(rom, addr)) return U.NOT_FOUND;
            return addr;
        }

        /// <summary>
        /// True when the full 44-byte glyph struct at <paramref name="p"/> is
        /// in-bounds (so the subsequent rom.u8 reads can't throw on EOF).
        /// </summary>
        static bool GlyphStructFits(ROM rom, uint p)
        {
            return U.isSafetyOffset(p, rom)
                && (ulong)p + GLYPH_STRUCT_BYTES <= (ulong)rom.Data.Length;
        }

        // ====================================================================
        // Enumeration — port of FontZHForm.MakeAllDataLengthInner / MakeCodeBMap
        // ====================================================================

        /// <summary>
        /// Enumerate every glyph in the item (true) or serif (false) ZH font. The
        /// codeB map is built on demand from a ZH_TBL encoder (the Avalonia app
        /// keeps TextEncoding=Auto, so we can't rely on CoreState.SystemTextEncoder
        /// being the ZH encoder). Returns an empty list on a bad / non-ZH ROM; never
        /// throws.
        /// </summary>
        public static List<FontGlyphZHEntry> EnumerateGlyphsZH(ROM rom, bool isItemFont)
        {
            var list = new List<FontGlyphZHEntry>();
            if (!IsZHRom(rom)) return list;

            uint topaddress = GetFontPointerZH(rom, isItemFont);
            if (!U.isSafetyOffset(topaddress, rom)) return list;

            var codeBMap = BuildCodeBMap(rom);
            foreach (var p in codeBMap)
            {
                uint addr = topaddress + p.Key;
                if (!GlyphStructFits(rom, addr)) continue;
                list.Add(new FontGlyphZHEntry
                {
                    Addr = addr,
                    // Moji comes straight from the TBL map (it is the engine code
                    // CalcCodeB already consumed) — no per-glyph re-encode.
                    Moji = p.Value.Moji,
                    Width = (int)rom.u8(addr + OFF_WIDTH),
                    Name = p.Value.Name,
                });
            }
            return list;
        }

        /// <summary>
        /// Build the { codeB -> (char, moji) } map from the ZH TBL encoder, mirroring
        /// FontZHForm.MakeCodeBMap. Builds the ZH_TBL encoder ONCE (the TBL is parsed a
        /// single time per enumeration — on a CN ROM the map has thousands of entries,
        /// so re-parsing per glyph was the #1166 hot path). The moji is carried in the
        /// value so the enumeration never re-encodes. Returns an empty map when the ZH
        /// TBL can't be loaded.
        /// </summary>
        static Dictionary<uint, (string Name, uint Moji)> BuildCodeBMap(ROM rom)
        {
            var codeBMap = new Dictionary<uint, (string, uint)>();
            ISystemTextEncoder encoder = GetZHEncoder(rom);
            if (encoder == null) return codeBMap;

            Dictionary<string, uint> encodeMap = encoder.GetTBLEncodeDicLow();
            if (encodeMap == null) return codeBMap;

            foreach (var p in encodeMap)
            {
                uint codeB = CalcCodeB(p.Value);
                if (codeB == U.NOT_FOUND) continue;
                // last char for a colliding codeB wins (as WF); p.Value is the moji.
                codeBMap[codeB] = (p.Key, p.Value);
            }
            return codeBMap;
        }

        /// <summary>
        /// A fresh ZH_TBL encoder for this ROM, built from
        /// config/translate/zh_tbl/{FE6,FE7,FE8}.tbl. Always constructs a NEW
        /// SystemTextEncoder (independent of CoreState.SystemTextEncoder, since the
        /// Avalonia app keeps encoding=Auto). SystemTextEncoder.LoadTBL FALLS BACK
        /// rather than throwing when the TBL is missing, so this normally does NOT
        /// return null — a missing TBL yields an encoder whose GetTBLEncodeDicLow() is
        /// empty (the enumeration then produces an empty list, no crash). Returns null
        /// only if the constructor itself throws.
        /// </summary>
        static ISystemTextEncoder GetZHEncoder(ROM rom)
        {
            try
            {
                var enc = new SystemTextEncoder(TextEncodingEnum.ZH_TBL, rom);
                // A missing/unparseable TBL yields an empty GetTBLEncodeDicLow(); an
                // empty map just yields an empty list (no crash), so this is safe.
                return enc;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The engine char code (little-endian) for a single-char string, via the ZH
        /// encoder. Returns 0 when it can't be encoded. Used to key a glyph for
        /// re-import after a list reload.
        /// </summary>
        static uint MojiFromChar(ROM rom, string ch)
        {
            if (string.IsNullOrEmpty(ch)) return 0;
            ISystemTextEncoder encoder = GetZHEncoder(rom);
            if (encoder == null) return 0;
            try
            {
                byte[] b = encoder.Encode(ch);
                if (b == null || b.Length == 0) return 0;
                if (b.Length == 1) return b[0];
                return (uint)(b[0] | (b[1] << 8)); // little-endian, like the TBL value
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Engine char code for <paramref name="ch"/> (public for the VM).</summary>
        public static uint EncodeMoji(ROM rom, string ch) => MojiFromChar(rom, ch);

        // ====================================================================
        // Render — port of ImageUtil.ByteToImage4ZH
        // ====================================================================

        /// <summary>
        /// Decode the 40-byte 2bpp glyph bitmap at <paramref name="addr"/>+4 into a
        /// 16x13 RGBA <see cref="IImage"/> using the fixed ZH font palette. Index 0
        /// (background) is rendered transparent. The decode wraps at the glyph's
        /// render-width (the stored advance width, +1 for the item font per the WF
        /// quirk), read from the struct (byte @ +1). Returns null on a bad addr /
        /// null ROM / null ImageService; never throws.
        /// </summary>
        public static IImage RenderGlyphZH(ROM rom, uint addr, bool isItemFont)
        {
            if (rom == null || CoreState.ImageService == null) return null;
            if (!GlyphStructFits(rom, addr)) return null;

            byte[] fontbyte = rom.getBinaryData(addr + OFF_BITMAP, GLYPH_BITMAP_BYTES);
            if (fontbyte == null || fontbyte.Length < GLYPH_BITMAP_BYTES) return null;

            int storedWidth = (int)rom.u8(addr + OFF_WIDTH);
            int renderWidth = RenderWidth(storedWidth, isItemFont);
            return RenderGlyphZHBytes(fontbyte, isItemFont, renderWidth);
        }

        /// <summary>
        /// The on-screen render width: the item font is stored as width-1, so it
        /// renders at width+1 (the WF "+1" quirk). Clamped to 1..GLYPH_W.
        /// </summary>
        static int RenderWidth(int storedWidth, bool isItemFont)
        {
            int w = isItemFont ? storedWidth + 1 : storedWidth;
            if (w < 1) w = 1;
            if (w > GLYPH_W) w = GLYPH_W;
            return w;
        }

        /// <summary>
        /// Decode an in-memory 40-byte 2bpp ZH glyph bitmap into a 16x13 RGBA image.
        /// Exposed for tests. Returns null on bad input. Ports ByteToImage4ZH: the
        /// 2bpp stream is read CONTINUOUSLY (x advances by ONE per pixel, NOT 4) and
        /// wraps at <paramref name="renderWidth"/> — the GBA font is a continuous
        /// 2bpp stream wrapping at the glyph width, NOT a 4-px-per-byte tile. The
        /// glyph is left-aligned in the 16x13 canvas; the rest stays transparent.
        /// </summary>
        public static IImage RenderGlyphZHBytes(byte[] fontbyte, bool isItemFont, int renderWidth)
        {
            if (CoreState.ImageService == null) return null;
            if (fontbyte == null || fontbyte.Length < GLYPH_BITMAP_BYTES) return null;

            int w = renderWidth;
            if (w < 1) w = 1;
            if (w > GLYPH_W) w = GLYPH_W;

            (byte R, byte G, byte B) bg = BgColor(isItemFont);
            // 4-color palette as RGBA; index 0 = transparent. ZH order: bg/white/gray/black.
            (byte R, byte G, byte B, byte A)[] pal =
            {
                (bg.R, bg.G, bg.B, 0),         // 0: background (transparent)
                (White.R, White.G, White.B, 255),
                (Gray.R, Gray.G, Gray.B, 255),
                (Black.R, Black.G, Black.B, 255),
            };

            var img = CoreState.ImageService.CreateImage(GLYPH_W, GLYPH_H);
            byte[] pixels = new byte[GLYPH_W * GLYPH_H * 4]; // RGBA, default transparent

            int x = 0, y = 0;
            for (int i = 0; i < GLYPH_BITMAP_BYTES; i++)
            {
                byte a = fontbyte[i];
                // 4 horizontal pixels per byte, low 2 bits = leftmost.
                for (int sub = 0; sub < 4; sub++)
                {
                    int idx = (a >> (sub * 2)) & 0x03;
                    if (idx != 0 && y < GLYPH_H && x < GLYPH_W)
                    {
                        int po = ((y * GLYPH_W) + x) * 4;
                        pixels[po + 0] = pal[idx].R;
                        pixels[po + 1] = pal[idx].G;
                        pixels[po + 2] = pal[idx].B;
                        pixels[po + 3] = pal[idx].A;
                    }
                    x++;
                    if (x >= w) { x = 0; y++; } // continuous read wraps at the glyph width
                }
            }

            img.SetPixelData(pixels);
            return img;
        }

        // ====================================================================
        // Encode — inverse of the continuous ZH render (Image4ToByteZH family)
        // ====================================================================

        /// <summary>
        /// Pack a 16x13 row-major buffer of 0..3 indices (1 byte/pixel) into the
        /// 40-byte 2bpp ZH font bitmap. This is the exact inverse of the continuous
        /// <see cref="RenderGlyphZHBytes"/> decode: it walks pixels in RENDER order
        /// (the first <paramref name="width"/> columns of each row, continuing the bit
        /// accumulator ACROSS row boundaries — the GBA font is a continuous 2bpp
        /// stream wrapping at the glyph width, NOT per-row-padded), 4 px per output
        /// byte, capped at 40 bytes. So render(pack(img)) == img for any width.
        /// Returns null if any consumed index is out of 0..3 (caller treats null as a
        /// validation error — NO mutation).
        /// </summary>
        public static byte[] PackGlyphZHBytes(byte[] indexedPixels, int width)
        {
            if (indexedPixels == null) return null;
            int w = Math.Min(width, GLYPH_W);
            if (w <= 0) return null;
            // The source buffer is a GLYPH_W-wide grid (the importer always remaps to
            // 16x13). We read the first w columns of each row, in render order.
            if (indexedPixels.Length < GLYPH_W * GLYPH_H) return null;

            byte[] data = new byte[GLYPH_BITMAP_BYTES];
            int nn = 0;     // output byte index
            int n = 0;      // sub-pixel slot within the current byte (0..3)
            byte one = 0;   // accumulating byte
            for (int y = 0; y < GLYPH_H; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int v = indexedPixels[y * GLYPH_W + x];
                    if (v < 0 || v > 3) return null; // out of 4-color range
                    one = (byte)(one | ((v & 0x03) << (n * 2)));
                    n++;
                    if (n >= 4)
                    {
                        data[nn++] = one;
                        n = 0;
                        one = 0;
                        if (nn >= GLYPH_BITMAP_BYTES) return data;
                    }
                }
            }
            // Flush a partial trailing byte (continuous stream may end mid-byte).
            if (n > 0 && nn < GLYPH_BITMAP_BYTES) data[nn] = one;
            return data;
        }

        // ====================================================================
        // Import — port of FontZHForm.WriteButton_Click (ROM-MUTATING)
        // ====================================================================

        /// <summary>
        /// Import one glyph for <paramref name="moji"/> into the item/serif ZH font.
        /// Validate-all-before-mutate: dims must be 16x13 and every consumed index
        /// 0..3 (else localized error + ZERO mutation). The ZH table is a DIRECT-reference
        /// array (every glyph has a fixed slot at topaddress+codeB), so this always
        /// updates that slot IN PLACE — width + bitmap; there is no append/repoint path.
        /// A slot that does not fit in-bounds returns an out-of-range error (the table is
        /// too small for this char). This call clones the ROM and restores it
        /// byte-identical on any fault (#885/#923). Returns "" on success or a localized
        /// error string; never throws.
        /// </summary>
        /// <param name="explicitWidth">Advance width to write. When &lt; 0 it is
        /// derived from the glyph pixels; when &gt;= 0 the supplied value wins.</param>
        public static string ImportGlyphZH(ROM rom, bool isItemFont, uint moji,
            byte[] indexedPixels, int width, int height, int explicitWidth = -1)
        {
            return ImportGlyphZH(rom, isItemFont, moji, indexedPixels, width, height,
                explicitWidth, manageSnapshot: true);
        }

        /// <summary>
        /// Import one glyph (see <see cref="ImportGlyphZH(ROM,bool,uint,byte[],int,int,int)"/>),
        /// with explicit control of the snapshot/restore ownership.
        /// </summary>
        /// <param name="manageSnapshot">When true, this call clones the ROM and
        /// restores it byte-identical on any fault. When false, the CALLER owns the
        /// snapshot/restore (bulk import keeps ONE snapshot for the whole
        /// transaction — avoids an O(glyphCount × romSize) per-glyph clone that would
        /// OOM on a real ZH ROM's thousands of glyphs). On a fault with
        /// manageSnapshot=false this returns the error WITHOUT restoring (the caller's
        /// bulk restore reverts every row).</param>
        public static string ImportGlyphZH(ROM rom, bool isItemFont, uint moji,
            byte[] indexedPixels, int width, int height, int explicitWidth, bool manageSnapshot)
        {
            if (rom?.RomInfo == null) return R._("ROM is not loaded.");
            if (!IsZHRom(rom)) return R._("This is not a Chinese ROM.");
            if (indexedPixels == null) return R._("No image data.");
            if (width != GLYPH_W || height != GLYPH_H)
                return R._("The image size is not correct. The font glyph must be {0}x{1}. Selected: {2}x{3}", GLYPH_W, GLYPH_H, width, height);

            // Derive/clamp the advance width BEFORE packing (the bitmap pack only
            // consumes the first `fontWidth` columns of each row).
            uint fontWidth = explicitWidth >= 0
                ? (uint)Math.Min(explicitWidth, GLYPH_W)
                : ComputeGlyphWidth(indexedPixels);

            // The ZH item font stores width-1 (the WF "+1" quirk): writing back the
            // stored value reproduces the same on-screen render width.
            uint storedWidth = fontWidth;
            if (isItemFont && storedWidth > 0) storedWidth -= 1;

            // The render width is the on-screen advance: serif renders at the stored
            // width, item at stored+1 (== fontWidth for both). Pack the bitmap at the
            // render width so RenderGlyphZH(pack(img)) reproduces the imported glyph
            // exactly (the continuous 2bpp stream wraps at this width).
            int renderWidth = RenderWidth((int)storedWidth, isItemFont);

            // Pack + validate indices BEFORE any mutation. null => out-of-range index.
            byte[] bitmap = PackGlyphZHBytes(indexedPixels, renderWidth);
            if (bitmap == null)
                return R._("The image uses colors outside the 4-color font palette. Remap it to the font palette first.");

            uint topaddress = GetFontPointerZH(rom, isItemFont);
            if (!U.isSafetyOffset(topaddress, rom)) return R._("The font pointer is invalid.");

            uint codeB = CalcCodeB(moji);
            if (codeB == U.NOT_FOUND)
                return R._("This character cannot be registered in the font.");

            uint slotaddr = topaddress + codeB;

            // Defensive snapshot AFTER all validation/encode succeeded (only when this
            // call owns it). A FAILED mutation leaves ZERO surviving bytes; the bulk
            // path passes manageSnapshot=false so it keeps ONE snapshot for the whole
            // batch instead of cloning the ROM per glyph.
            byte[] snap = manageSnapshot ? (byte[])rom.Data.Clone() : null;
            try
            {
                // ZH is a DIRECT-reference array: the slot at topaddress+codeB IS the
                // glyph struct. If it fits in-bounds, update it in place. (Unlike the
                // main font there is no append/chain path on a real ROM — the table is
                // pre-sized; out-of-range means the table is too small for this char.)
                if (!GlyphStructFits(rom, slotaddr))
                {
                    if (manageSnapshot) RestoreSnapshot(rom, snap);
                    return R._("The glyph entry address is out of range.");
                }

                // struct: unk1(0xD) | width | height(0xD) | 0 | 40-byte bitmap
                rom.write_u8(slotaddr + 0, 0xD);
                rom.write_u8(slotaddr + OFF_WIDTH, storedWidth);
                rom.write_u8(slotaddr + 2, 0xD);
                rom.write_u8(slotaddr + 3, 0);
                rom.write_range(slotaddr + OFF_BITMAP, bitmap);
                return "";
            }
            catch (Exception ex)
            {
                if (manageSnapshot) RestoreSnapshot(rom, snap);
                return R._("Font glyph import failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Advance width = the rightmost non-background (index &gt; 0) column + 1,
        /// clamped to 1..16. Background-only glyph → 1.
        /// </summary>
        static uint ComputeGlyphWidth(byte[] indexedPixels)
        {
            int maxX = -1;
            for (int y = 0; y < GLYPH_H; y++)
            {
                for (int x = 0; x < GLYPH_W; x++)
                {
                    if (indexedPixels[y * GLYPH_W + x] != 0 && x > maxX) maxX = x;
                }
            }
            if (maxX < 0) return 1;
            uint w = (uint)(maxX + 1);
            if (w > GLYPH_W) w = GLYPH_W;
            return w;
        }

        /// <summary>
        /// Length-aware byte-identical restore (a future append path could GROW
        /// rom.Data; down-resize first so a naive Array.Copy can't leave a grown
        /// tail alive). #885/#923 pattern.
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
