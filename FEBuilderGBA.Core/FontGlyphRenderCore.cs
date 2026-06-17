// SPDX-License-Identifier: GPL-3.0-or-later
//
// Cross-platform main-font glyph editor seam (#1165) — the Avalonia Font editor's
// list/render/PNG-import half of WinForms FontForm.cs.
//
// FontForm is SEARCH-by-character; the on-screen glyph render (DrawFont /
// ByteToImage4) plus the full-charset enumeration (MakeAllDataLengthInner) and
// the bitmap pack/unpack (Image4ToByte) all live in WinForms behind
// System.Drawing.Bitmap. This seam ports the GUI-free math:
//
//   * EnumerateGlyphs(rom, isItemFont)  — port of MakeAllDataLengthInner: walks
//     the font hash table (SJIS / UTF8 / LAT1 branch) yielding one entry per
//     glyph { Addr, Moji, Width, Name }.
//   * RenderGlyph(rom, addr, isItemFont) — port of DrawFont/ByteToImage4: decode
//     the 64-byte 2bpp (4-color) bitmap at addr+8 to a 16x16 RGBA IImage using
//     the fixed font palette (bg/gray/white/black; bg differs item vs serif).
//   * ImportGlyph(rom, isItemFont, moji, indexedPixels, w, h) — ROM-MUTATING port
//     of WriteButton_Click / ImportAll: validate 16x16 + indices 0..3, 2-bit pack
//     (Image4ToByte port), then in-place update OR MakeNewFontData + append +
//     chain-link. Validate-all-before-mutate; lazy snapshot AFTER encode succeeds;
//     byte-identical fault restore (#885/#923). Runs under the caller's ambient
//     undo scope.
//
// The font glyph bitmap is 16x16 at 2 bits/pixel (4 colors): 64 bytes, each byte
// holds 4 horizontal pixels (low 2 bits = leftmost). This is NOT the standard
// 4bpp/16-color tile format, so it needs its own decode/encode here.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Core
{
    /// <summary>
    /// One enumerated main-font glyph. <see cref="Moji"/> is the engine character
    /// code (needed to re-import a glyph; see <see cref="FontGlyphRenderCore.ImportGlyph"/>).
    /// </summary>
    public sealed class FontGlyphEntry
    {
        /// <summary>ROM offset of the 72-byte glyph struct (header @ +0, width @ +5, 64-byte bitmap @ +8).</summary>
        public uint Addr;
        /// <summary>Engine character code (SJIS: moji1&lt;&lt;8|moji2 ; LAT1: moji2 ; UTF8: 4-byte packed).</summary>
        public uint Moji;
        /// <summary>Glyph advance width (byte @ Addr+5).</summary>
        public int Width;
        /// <summary>Human-readable character (decoded via the system text encoder) or an @hex fallback.</summary>
        public string Name = "";
    }

    /// <summary>
    /// GUI-free main-font glyph helpers (#1165). READ-ONLY enumeration + render,
    /// plus one ROM-MUTATING <see cref="ImportGlyph"/>. Every ROM access is
    /// bounds-guarded; nothing throws on a bad ROM.
    /// </summary>
    public static class FontGlyphRenderCore
    {
        // The 64-byte 2bpp bitmap is 16x16 pixels.
        public const int GLYPH_W = 16;
        public const int GLYPH_H = 16;
        public const int GLYPH_BITMAP_BYTES = (GLYPH_W * GLYPH_H) / 4; // 64
        public const int GLYPH_STRUCT_BYTES = 8 + GLYPH_BITMAP_BYTES;  // 72

        // ---- Fixed font palette (matches WF ImageUtil.ByteToImage4) ----
        // Index 0 = background (item vs serif differ), 1 = gray, 2 = white, 3 = black.
        // Stored as RGB; index 0 is rendered TRANSPARENT in the preview.
        static readonly (byte R, byte G, byte B) ItemBg  = (0x68, 0x88, 0xA8);
        static readonly (byte R, byte G, byte B) SerifBg = (0xE0, 0xE0, 0xE0);
        static readonly (byte R, byte G, byte B) Gray    = (0xA8, 0xA8, 0xA7);
        static readonly (byte R, byte G, byte B) White   = (0xF8, 0xF8, 0xF8);
        static readonly (byte R, byte G, byte B) Black    = (0x28, 0x28, 0x28);

        static (byte R, byte G, byte B) BgColor(bool isItemFont) => isItemFont ? ItemBg : SerifBg;

        /// <summary>
        /// The 4 font colors as a 4-entry GBA palette (2 bytes/color, BGR555 LE).
        /// Used by the Avalonia importer to remap a PNG onto the font palette with
        /// colorCount=4 (so quantized indices stay 0..3 — see the #1165 plan v2).
        /// </summary>
        public static byte[] GetFontPaletteGBA(bool isItemFont)
        {
            var colors = new[] { BgColor(isItemFont), Gray, White, Black };
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
        // Enumeration — port of FontForm.MakeAllDataLengthInner
        // ====================================================================

        /// <summary>
        /// Enumerate every glyph in the item (true) or serif (false) font hash
        /// table. Branches by priority code / is_multibyte exactly like
        /// MakeAllDataLengthInner. Returns an empty list on a bad ROM; never throws.
        /// </summary>
        public static List<FontGlyphEntry> EnumerateGlyphs(ROM rom, bool isItemFont)
        {
            var list = new List<FontGlyphEntry>();
            if (rom?.RomInfo == null) return list;

            uint topaddress = FontCore.GetFontPointer(isItemFont, rom);
            if (!U.isSafetyOffset(topaddress, rom)) return list;

            PRIORITY_CODE priorityCode = PriorityCodeUtil.SearchPriorityCode(rom);

            if (rom.RomInfo.is_multibyte)
            {
                EnumerateSJIS(rom, topaddress, priorityCode, list);
            }
            else if (priorityCode == PRIORITY_CODE.UTF8)
            {
                EnumerateUTF8(rom, topaddress, list);
            }
            else
            {
                EnumerateLat1(rom, topaddress, priorityCode, list);
            }
            return list;
        }

        static void EnumerateSJIS(ROM rom, uint topaddress, PRIORITY_CODE priorityCode, List<FontGlyphEntry> list)
        {
            // Japanese: hash by moji1 (0x1f..0xff), list root at topaddress + (moji1<<2) - 0x100.
            for (uint moji1 = 0x1f; moji1 <= 0xff; moji1++)
            {
                uint fontlist = topaddress + (moji1 << 2) - 0x100;
                if (!U.isSafetyOffset(fontlist, rom)) continue;
                uint p = rom.p32(fontlist);
                if (!U.isSafetyOffset(p, rom)) continue;

                while (p > 0)
                {
                    uint moji2 = rom.u8(p + 4);
                    uint moji = (moji1 << 8) | moji2;
                    list.Add(new FontGlyphEntry
                    {
                        Addr = p,
                        Moji = moji,
                        Width = (int)rom.u8(p + 5),
                        Name = FontChar(rom, moji2, moji1, priorityCode),
                    });

                    uint next = rom.u32(p);
                    if (next == 0) break;
                    if (!U.isSafetyPointer(next, rom)) break;
                    p = U.toOffset(next);
                }
            }
        }

        static void EnumerateUTF8(ROM rom, uint topaddress, List<FontGlyphEntry> list)
        {
            // UTF-8: hash by moji1 (0x00..0xff), list root at topaddress + (moji1<<2).
            for (uint moji1 = 0x0; moji1 <= 0xff; moji1++)
            {
                uint fontlist = topaddress + (moji1 << 2);
                if (!U.isSafetyOffset(fontlist, rom)) continue;
                uint p = rom.p32(fontlist);
                if (!U.isSafetyOffset(p, rom)) continue;

                while (p > 0)
                {
                    uint moji2 = rom.u8(p + 4);
                    uint moji3 = rom.u8(p + 6);
                    uint moji4 = rom.u8(p + 7);
                    uint moji = moji1 | (moji2 << 8) | (moji3 << 16) | (moji4 << 24);
                    list.Add(new FontGlyphEntry
                    {
                        Addr = p,
                        Moji = moji,
                        Width = (int)rom.u8(p + 5),
                        Name = FontCharUTF8(moji1, moji2, moji3, moji4),
                    });

                    uint next = rom.u32(p);
                    if (next == 0) break;
                    if (!U.isSafetyPointer(next, rom)) break;
                    p = U.toOffset(next);
                }
            }
        }

        static void EnumerateLat1(ROM rom, uint topaddress, PRIORITY_CODE priorityCode, List<FontGlyphEntry> list)
        {
            // English: hash by moji2 (0x00..0xff), list root at topaddress + (moji2<<2),
            // direct lookup (one glyph per bucket, but WF still walks the chain).
            for (uint moji2 = 0x0; moji2 <= 0xff; moji2++)
            {
                uint fontlist = topaddress + (moji2 << 2);
                if (!U.isSafetyOffset(fontlist, rom)) continue;
                uint p = rom.p32(fontlist);
                if (!U.isSafetyOffset(p, rom)) continue;

                while (p > 0)
                {
                    list.Add(new FontGlyphEntry
                    {
                        Addr = p,
                        Moji = moji2,
                        Width = (int)rom.u8(p + 5),
                        Name = FontChar(rom, moji2, 0, priorityCode),
                    });

                    uint next = rom.u32(p);
                    if (next == 0) break;
                    if (!U.isSafetyPointer(next, rom)) break;
                    p = U.toOffset(next);
                }
            }
        }

        // Port of FontForm.FontChar — decode (moji1,moji2) to a display string.
        static string FontChar(ROM rom, uint moji2, uint moji1, PRIORITY_CODE priorityCode)
        {
            var encoder = CoreState.SystemTextEncoder;
            if (encoder != null && priorityCode == PRIORITY_CODE.SJIS)
            {
                if (U.isSJIS1stCode((byte)moji1) && U.isSJIS2ndCode((byte)moji2))
                {
                    byte[] s = { (byte)moji1, (byte)moji2, 0 };
                    return encoder.Decode(s, 0, 2);
                }
                if (moji1 > 0 && moji2 == 0x40)
                {
                    byte[] s = { (byte)moji1, 0 };
                    return encoder.Decode(s, 0, 1);
                }
            }

            if (moji1 == 0 && encoder != null)
            {
                byte[] s = { (byte)moji2, 0 };
                return encoder.Decode(s, 0, 1);
            }

            return "@" + Hex2((byte)moji2) + Hex2((byte)moji1);
        }

        static string FontCharUTF8(uint moji1, uint moji2, uint moji3, uint moji4)
        {
            try
            {
                byte[] s = { (byte)moji1, (byte)moji2, (byte)moji3, (byte)moji4 };
                return System.Text.Encoding.GetEncoding("UTF-32").GetString(s, 0, 4);
            }
            catch
            {
                return "@" + Hex2((byte)moji1);
            }
        }

        // Two-hex-digit string for a byte (Core has no U.ToCharOneHex).
        static string Hex2(byte a) => a.ToString("X2");

        // ====================================================================
        // Render — port of FontForm.DrawFont / ImageUtil.ByteToImage4
        // ====================================================================

        /// <summary>
        /// Decode the 64-byte 2bpp glyph bitmap at <paramref name="addr"/>+8 into
        /// a 16x16 RGBA <see cref="IImage"/> using the fixed font palette. Index 0
        /// (background) is rendered transparent so glyphs composite cleanly.
        /// Returns null on a bad addr / null ROM / null ImageService; never throws.
        /// </summary>
        public static IImage RenderGlyph(ROM rom, uint addr, bool isItemFont)
        {
            if (rom == null || CoreState.ImageService == null) return null;
            if (!U.isSafetyOffset(addr, rom)) return null;
            // Need 64 bitmap bytes at addr+8.
            if ((ulong)addr + GLYPH_STRUCT_BYTES > (ulong)rom.Data.Length) return null;

            byte[] fontbyte = rom.getBinaryData(addr + 8, GLYPH_BITMAP_BYTES);
            if (fontbyte == null || fontbyte.Length < GLYPH_BITMAP_BYTES) return null;

            return RenderGlyphBytes(fontbyte, isItemFont);
        }

        /// <summary>
        /// Decode an in-memory 64-byte 2bpp glyph bitmap into a 16x16 RGBA image.
        /// Exposed for tests + the bulk export path. Returns null on bad input.
        /// </summary>
        public static IImage RenderGlyphBytes(byte[] fontbyte, bool isItemFont)
        {
            if (CoreState.ImageService == null) return null;
            if (fontbyte == null || fontbyte.Length < GLYPH_BITMAP_BYTES) return null;

            (byte R, byte G, byte B) bg = BgColor(isItemFont);
            // 4-color palette as RGBA; index 0 = transparent.
            (byte R, byte G, byte B, byte A)[] pal =
            {
                (bg.R, bg.G, bg.B, 0),       // 0: background (transparent)
                (Gray.R, Gray.G, Gray.B, 255),
                (White.R, White.G, White.B, 255),
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
                    if (idx != 0)
                    {
                        int po = ((y * GLYPH_W) + (x + sub)) * 4;
                        pixels[po + 0] = pal[idx].R;
                        pixels[po + 1] = pal[idx].G;
                        pixels[po + 2] = pal[idx].B;
                        pixels[po + 3] = pal[idx].A;
                    }
                }
                x += 4;
                if (x >= GLYPH_W) { x = 0; y++; }
            }

            img.SetPixelData(pixels);
            return img;
        }

        // ====================================================================
        // Encode — port of ImageUtil.Image4ToByte (16x16, 2bpp pack)
        // ====================================================================

        /// <summary>
        /// Pack a 16x16 buffer of 0..3 indices (row-major, 1 byte/pixel) into the
        /// 64-byte 2bpp font bitmap. Returns null if any index is out of 0..3 or
        /// the buffer is too small (caller treats null as a validation error — NO
        /// mutation).
        /// </summary>
        public static byte[] PackGlyphBytes(byte[] indexedPixels)
        {
            if (indexedPixels == null || indexedPixels.Length < GLYPH_W * GLYPH_H) return null;

            byte[] data = new byte[GLYPH_BITMAP_BYTES];
            int nn = 0;
            for (int y = 0; y < GLYPH_H; y++)
            {
                int n = 0;
                byte one = 0;
                for (int x = 0; x < GLYPH_W; x++)
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
                    }
                }
            }
            return data;
        }

        // ====================================================================
        // Import — port of FontForm.WriteButton_Click / ImportAll (ROM-MUTATING)
        // ====================================================================

        /// <summary>
        /// Import one glyph for <paramref name="moji"/> into the item/serif font.
        /// Validate-all-before-mutate: dims must be 16x16 and every index 0..3
        /// (else localized error + ZERO mutation). Existing glyph → in-place
        /// width + bitmap update; new glyph → MakeNewFontData + append to free
        /// space + chain-link the previous bucket. Runs under the caller's ambient
        /// undo scope; any fault restores the ROM byte-identical. Returns "" on
        /// success or a localized error string.
        /// </summary>
        public static string ImportGlyph(ROM rom, bool isItemFont, uint moji, byte[] indexedPixels, int width, int height)
        {
            if (rom?.RomInfo == null) return R._("ROM is not loaded.");
            if (indexedPixels == null) return R._("No image data.");
            if (width != GLYPH_W || height != GLYPH_H)
                return R._("The image size is not correct. The font glyph must be {0}x{1}. Selected: {2}x{3}", GLYPH_W, GLYPH_H, width, height);

            // Pack + validate indices BEFORE any mutation. null => out-of-range index.
            byte[] bitmap = PackGlyphBytes(indexedPixels);
            if (bitmap == null)
                return R._("The image uses colors outside the 4-color font palette. Remap it to the font palette first.");

            PRIORITY_CODE priorityCode = PriorityCodeUtil.SearchPriorityCode(rom);
            uint topaddress = FontCore.GetFontPointer(isItemFont, rom);
            if (!U.isSafetyOffset(topaddress, rom)) return R._("The font pointer is invalid.");

            uint fontWidth = ComputeGlyphWidth(indexedPixels);

            uint prevaddr;
            uint fontaddr = FontCore.FindFontData(topaddress, moji, out prevaddr, rom, priorityCode);

            // Defensive snapshot AFTER all validation/encode succeeded: a FAILED
            // mutation leaves ZERO surviving bytes (the caller's ambient undo scope
            // captures the success-path writes for UNDO).
            byte[] snap = (byte[])rom.Data.Clone();
            try
            {
                if (fontaddr != U.NOT_FOUND)
                {
                    // In-place update of an existing glyph.
                    if ((ulong)fontaddr + GLYPH_STRUCT_BYTES > (ulong)rom.Data.Length)
                    {
                        RestoreSnapshot(rom, snap);
                        return R._("The glyph entry address is out of range.");
                    }
                    rom.write_u8(fontaddr + 5, fontWidth);
                    rom.write_range(fontaddr + 8, bitmap);
                    return "";
                }

                // New glyph: needs a valid previous bucket/link to chain onto.
                if (prevaddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    // e.g. a JP control char that the font hash rules can't register.
                    return R._("This character cannot be registered in the font.");
                }
                if ((ulong)prevaddr + 4 > (ulong)rom.Data.Length)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("The glyph entry address is out of range.");
                }

                byte[] newFontData = FontCore.MakeNewFontData(moji, fontWidth, bitmap, rom, priorityCode);
                U.write_u32(newFontData, 0, 0); // NULL — appended at the chain tail.

                uint newaddr = MapEventUnitCore.AppendBinaryDataHeadless(rom, newFontData, null);
                if (newaddr == U.NOT_FOUND)
                {
                    RestoreSnapshot(rom, snap);
                    return R._("Failed to allocate free space.");
                }

                // Repoint the previous bucket/link to the freshly appended glyph.
                rom.write_p32(prevaddr + 0, U.toPointer(newaddr));
                return "";
            }
            catch (Exception ex)
            {
                RestoreSnapshot(rom, snap);
                return R._("Font glyph import failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Advance width = the rightmost non-background (index &gt; 0) column + 1,
        /// clamped to 1..16. Mirrors the spirit of WF (which defaults a fresh
        /// import to a fixed width); deriving it from the glyph keeps round-trips
        /// stable without a separate width control. Background-only glyph → 1.
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
        /// Length-aware byte-identical restore: a free-space append can GROW
        /// rom.Data, so down-resize to the snapshot length BEFORE the in-place copy
        /// (a naive Array.Copy would leave the grown tail alive). #885/#923 pattern.
        /// </summary>
        static void RestoreSnapshot(ROM rom, byte[] snap)
        {
            if (rom.Data.Length != snap.Length)
                rom.write_resize_data((uint)snap.Length);
            Array.Copy(snap, rom.Data, snap.Length);
        }
    }
}
