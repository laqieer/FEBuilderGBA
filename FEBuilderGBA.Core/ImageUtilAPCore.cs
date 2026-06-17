// SPDX-License-Identifier: GPL-3.0-or-later
// Pure cross-platform port of WinForms ImageUtilAP: AP (Animated Parts) data
// parser and per-frame OAM-blit renderer. Used by the border AP preview
// (#849, NV5c). No System.Drawing dependency.
//
// WF source: FEBuilderGBA/ImageUtilAP.cs
//   * Parse  — reads the AP header → frame-list + anime-list with all the WF
//     U.isSafetyOffset / count guards (ImageUtilAP.cs ~:153,169,198,247,296).
//   * FrameToDump — decodes a 6-byte OAM entry into width/height/x/y/tile/
//     palette/flip fields (AP.cs:66-95; G4 bit math).
//   * RenderFrame — blits the OAM entries for one frame onto a transparent
//     256×160 canvas using ByteToImage16Tile source pixels; skips source
//     index-0 pixels (transparent_index=0, per G4 correctness note).
//
// Key correctness details from the plan-review:
//   G4a — SharpTable: 2-D [sharp1][sharp2] where sharp1=(OAM0>>14)&3,
//          sharp2=(OAM1>>14)&3. FULL 4×4 table incl. invalid (0,0) entry.
//   G4b — sign extension: image_x 9-bit via &0x100 check; image_y 8-bit
//          via &0x80 check (mirror AP.cs:80-87 exactly).
//   G4c — transparent_index=0: source pixels at palette index 0 are NOT
//          blitted onto the destination (they stay transparent/invisible).
//          Getting this wrong paints a solid rectangle over the background.
//   G4d — DrawFrame src math: graphicsWidth=parts.Width/8; src_x=(tile%gW)*8;
//          src_y=(tile/gW)*8; blit width*8 × height*8 (in tiles) from the
//          parts sheet at (src_x,src_y) to (originX+image_x, originY+image_y)
//          with flips.
//
// All ROM reads go via the PASSED romData / rom.Data — NO hidden Program.ROM
// or CoreState.ROM static reads inside this class.
//
// Safety: every U.isSafetyOffset is evaluated against the romData array
// length, not a global. All count/offset validations match WF. Never throws;
// returns null/false on any structural anomaly.
using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform AP (Animated Parts) data parser and per-frame OAM renderer.
    /// Pure port of WinForms <c>ImageUtilAP</c> with no System.Drawing dependency.
    /// </summary>
    public class ImageUtilAPCore
    {
        // =====================================================================
        // OAM size look-up (WF SharpTable: [sharp1][sharp2]).
        // G4a: FULL 4×4 table including the invalid (0,0) entry for sharp1=3.
        // sharp1 = (OAM0 >> 14) & 3   (shape)
        // sharp2 = (OAM1 >> 14) & 3   (size)
        // Result is tile units (×8 for pixels).
        // =====================================================================

        // Width × Height in tiles. Index: [sharp1][sharp2]
        // Row 0 (square),  Row 1 (horizontal), Row 2 (vertical), Row 3 (invalid)
        static readonly int[] SharpW = new int[16]
        {
            // square (shape=0): 1,2,4,8
            1, 2, 4, 8,
            // horizontal (shape=1): 2,4,4,8
            2, 4, 4, 8,
            // vertical (shape=2): 1,1,2,4
            1, 1, 2, 4,
            // invalid (shape=3): 0,0,0,0
            0, 0, 0, 0
        };
        static readonly int[] SharpH = new int[16]
        {
            // square (shape=0): 1,2,4,8
            1, 2, 4, 8,
            // horizontal (shape=1): 1,1,2,4
            1, 1, 2, 4,
            // vertical (shape=2): 2,4,4,8
            2, 4, 4, 8,
            // invalid (shape=3): 0,0,0,0
            0, 0, 0, 0
        };

        // =====================================================================
        // Parsed internal structures
        // =====================================================================

        class APFrame
        {
            public uint OAM0;
            public uint OAM1;
            public uint OAM2;
        }

        class APFrameArr
        {
            public List<APFrame> Frames = new List<APFrame>();
        }

        class APAnime
        {
            public uint Wait;
            public uint FrameIndex;
        }

        class APAnimeArr
        {
            public List<APAnime> Animes = new List<APAnime>();
        }

        List<APFrameArr> _frameArrays = new List<APFrameArr>();
        List<APAnimeArr> _animeArrays = new List<APAnimeArr>();
        uint _baseAddr;
        uint _length;
        string _errorMessage;
        int _romDataLen;

        public string ErrorMessage => _errorMessage ?? "";

        // =====================================================================
        // OAM decode — mirrors WF FrameToDump (AP.cs:66-95, G4 bit math)
        // =====================================================================

        /// <summary>
        /// Decoded OAM entry for a single sprite part.
        /// </summary>
        public struct OAMEntry
        {
            public int WidthTiles;  // tile units (×8 for pixels)
            public int HeightTiles;
            public int ImageX;      // screen X offset (signed, relative to origin)
            public int ImageY;      // screen Y offset (signed, relative to origin)
            public uint Tile;       // source tile index in the parts sheet
            public int PaletteShift;// sub-palette offset (0 for border sheets)
            public bool VFlipped;
            public bool HFlipped;
        }

        /// <summary>
        /// Decode a single 6-byte AP OAM entry into an <see cref="OAMEntry"/>.
        /// Mirrors WF <c>ImageUtilAP.FrameToDump</c> exactly (AP.cs:66-95, G4).
        /// </summary>
        static OAMEntry FrameToDump(APFrame f)
        {
            // G4a — SharpTable lookup.
            int sharp1 = (int)((f.OAM0 >> 14) & 0x3);
            int sharp2 = (int)((f.OAM1 >> 14) & 0x3);
            int tableIdx = sharp1 * 4 + sharp2;
            int wTiles = SharpW[tableIdx];
            int hTiles = SharpH[tableIdx];

            // G4b — sign extension.
            // image_x: 9-bit → signed via &0x100 check (AP.cs:80-83)
            int image_x = (int)(f.OAM1 & 0x1FF);
            if ((image_x & 0x100) == 0x100)
                image_x = (image_x & 0xFF) - 256;

            // image_y: 8-bit → signed via &0x80 check (AP.cs:84-87)
            int image_y = (int)(f.OAM0 & 0x0FF);
            if ((image_y & 0x80) == 0x80)
                image_y = (image_y & 0x7F) - 128;

            uint tile = f.OAM2 & 0x3FF;
            int paletteShift = (int)((f.OAM2 & 0xF000) >> 12);
            bool v_flipped = (f.OAM1 & 0x2000) == 0x2000;
            bool h_flipped = (f.OAM1 & 0x1000) == 0x1000;

            return new OAMEntry
            {
                WidthTiles  = wTiles,
                HeightTiles = hTiles,
                ImageX      = image_x,
                ImageY      = image_y,
                Tile        = tile,
                PaletteShift = paletteShift,
                VFlipped    = v_flipped,
                HFlipped    = h_flipped,
            };
        }

        // =====================================================================
        // Parse — mirrors WF ImageUtilAP.Parse / ParseFrame / ParseAnime
        // with all safety guards (AP.cs:150-310, G2)
        // =====================================================================

        /// <summary>
        /// Parse the AP data structure at <paramref name="apAddr"/> in
        /// <paramref name="romData"/>. Returns <c>true</c> on success;
        /// <c>false</c> (with <see cref="ErrorMessage"/> set) on any structural
        /// anomaly. Never throws.
        /// </summary>
        public bool Parse(byte[] romData, uint apAddr)
        {
            if (romData == null || romData.Length == 0) return false;
            _romDataLen = romData.Length;
            _baseAddr = apAddr;
            _length = 0;
            _frameArrays.Clear();
            _animeArrays.Clear();

            // Header: 4 bytes minimum.
            if (!IsSafetyOffset(apAddr) || !IsRegionSafe(apAddr, 4))
            {
                _errorMessage = $"AP data at 0x{apAddr:X} is corrupt: header extends beyond ROM end.";
                return false;
            }

            uint frameDataOffset = ReadU16(romData, apAddr);
            uint animeTableOffset = ReadU16(romData, apAddr + 2);
            UpdateLength(apAddr + 4);

            uint minData = 0xFFFF;

            // Frame-list: from (base+frameDataOffset) to (base+animeTableOffset).
            uint end = _baseAddr + animeTableOffset;
            if (!IsSafetyOffset(end - 1))
            {
                _errorMessage = $"AP data at 0x{apAddr:X} is corrupt: frame end beyond ROM end.";
                return false;
            }

            uint addr;
            for (addr = _baseAddr + frameDataOffset; addr < end; addr += 2)
            {
                uint f = ReadU16(romData, addr);
                if (f < minData) minData = f;
                APFrameArr farr = ParseFrame(romData, _baseAddr + frameDataOffset + f);
                if (farr == null) return false;
                _frameArrays.Add(farr);
            }

            // G2 — minData sanity check (WF :190-194).
            if (minData >= 0x100)
            {
                _errorMessage = $"AP data at 0x{apAddr:X} is corrupt: anime count out of range ({minData}).";
                return false;
            }

            // Anime-list: from (base+animeTableOffset) to (base+frameDataOffset+minData).
            end = _baseAddr + frameDataOffset + minData;
            if (!IsSafetyOffset(end - 1))
            {
                _errorMessage = $"AP data at 0x{apAddr:X} is corrupt: anime end beyond ROM end.";
                return false;
            }

            for (addr = _baseAddr + animeTableOffset; addr < end; addr += 2)
            {
                uint a = ReadU16(romData, addr);
                APAnimeArr aarr = ParseAnime(romData, _baseAddr + animeTableOffset + a);
                if (aarr == null) return false;
                _animeArrays.Add(aarr);
            }

            UpdateLength(addr);
            return true;
        }

        APFrameArr ParseFrame(byte[] romData, uint addr)
        {
            if (!IsRegionSafe(addr, 2))
            {
                _errorMessage = $"AP frame at 0x{addr:X} beyond ROM end.";
                return null;
            }

            APFrameArr arr = new APFrameArr();
            uint count = ReadU16(romData, addr);
            addr += 2;

            uint rotateCount = 0;
            if ((count & 0x8000) == 0x8000)
            {
                rotateCount = count & 0x7FFF;
                if (rotateCount > 0x100)
                {
                    _errorMessage = $"AP frame at 0x{addr:X}: rotate count out of range ({rotateCount}).";
                    return null;
                }
                count = rotateCount;
                arr.Frames = new List<APFrame>(); // UseRotate (not needed for render, just parse)
            }

            if (count > 0x100)
            {
                _errorMessage = $"AP frame at 0x{addr:X}: OAM count out of range ({count}).";
                return null;
            }

            for (int i = 0; i < count; addr += 6, i++)
            {
                if (!IsRegionSafe(addr, 6))
                {
                    _errorMessage = $"AP frame OAM scan at 0x{addr:X} @ entry {i} beyond ROM end.";
                    return null;
                }
                APFrame f = new APFrame
                {
                    OAM0 = ReadU16(romData, addr + 0),
                    OAM1 = ReadU16(romData, addr + 2),
                    OAM2 = ReadU16(romData, addr + 4),
                };
                arr.Frames.Add(f);
            }

            UpdateLength(addr);
            return arr;
        }

        APAnimeArr ParseAnime(byte[] romData, uint addr)
        {
            APAnimeArr arr = new APAnimeArr();
            for (; ; addr += 4)
            {
                if (!IsRegionSafe(addr, 2))
                {
                    _errorMessage = $"AP anime scan at 0x{addr:X} beyond ROM end.";
                    return null;
                }
                APAnime a = new APAnime
                {
                    Wait       = ReadU16(romData, addr + 0),
                    FrameIndex = ReadU16(romData, addr + 2),
                };
                arr.Animes.Add(a);
                if (a.Wait == 0) break; // animation terminator
            }
            addr += 4;
            UpdateLength(addr);
            return arr;
        }

        void UpdateLength(uint addr)
        {
            uint a = addr - _baseAddr;
            if (a > _length) _length = a;
        }

        /// <summary>
        /// Total parsed AP region length, 4-byte-padded. Byte-exact port of WF
        /// <c>ImageUtilAP.GetLength()</c> = <c>U.Padding4(Length)</c> — feeds AP
        /// export bytes (and any AP MD5 / dictionary matching), so the padding
        /// MUST match WF or those drift. Call only after a successful
        /// <see cref="Parse"/>.
        /// </summary>
        public uint GetLength()
        {
            return U.Padding4(_length);
        }

        /// <summary>
        /// Compute the (4-byte-padded) length of the AP region at
        /// <paramref name="apAddr"/> in <paramref name="romData"/>. Port of WF
        /// <c>ImageUtilAP.CalcAPLength</c>: parse the AP structure and return its
        /// padded length, or <c>0</c> when the region is unparseable / corrupt.
        /// Never throws.
        /// </summary>
        public static uint CalcAPLength(byte[] romData, uint apAddr)
        {
            var ap = new ImageUtilAPCore();
            if (!ap.Parse(romData, apAddr)) return 0;
            return ap.GetLength();
        }

        // =====================================================================
        // RenderFrame — mirrors WF DrawFrame (AP.cs:96-131, G4d)
        //
        // Blits each OAM entry from the parts sheet onto a transparent canvas.
        // Source index 0 → skip (transparent_index=0, G4c).
        // =====================================================================

        /// <summary>
        /// Render frame <paramref name="frameIndex"/> onto a new transparent
        /// <paramref name="canvasW"/>×<paramref name="canvasH"/> RGBA canvas.
        /// Blits each OAM entry from <paramref name="parts"/> (the border parts
        /// sheet — decoded via <see cref="ImageUtilCore.ByteToImage16Tile"/>)
        /// using the WF <c>DrawFrame</c> + <c>BitBlt</c> math (AP.cs:96-131;
        /// G4d src math). Source palette index 0 is TRANSPARENT (G4c) — pixels
        /// at index 0 are NOT blitted onto the canvas (they stay transparent /
        /// invisible), matching WF <c>BitBlt(..., transparent_index:0, ...)</c>.
        /// </summary>
        /// <returns>A new RGBA <see cref="IImage"/> of <paramref name="canvasW"/>×
        /// <paramref name="canvasH"/>, or <c>null</c> when
        /// <see cref="CoreState.ImageService"/> is null, <paramref name="parts"/>
        /// is null/empty, or <paramref name="frameIndex"/> is out of range.
        /// Never throws.</returns>
        public IImage RenderFrame(IImage parts, int frameIndex,
            int originX, int originY, int canvasW = 256, int canvasH = 160)
        {
            if (CoreState.ImageService == null) return null;
            if (parts == null) return null;
            if (frameIndex < 0 || frameIndex >= _frameArrays.Count) return null;

            var img = CoreState.ImageService.CreateImage(canvasW, canvasH);
            byte[] pixels = new byte[canvasW * canvasH * 4]; // all-transparent RGBA

            APFrameArr fa = _frameArrays[frameIndex];

            // G4d: src tile math uses parts.Width/8 as graphicsWidth.
            int graphicsWidth = parts.Width / 8;
            if (graphicsWidth <= 0) { img.SetPixelData(pixels); return img; }

            // We need direct pixel access from the parts sheet.
            byte[] partsPixels = parts.GetPixelData(); // RGBA bytes
            int partsW = parts.Width;
            int partsH = parts.Height;

            foreach (APFrame f in fa.Frames)
            {
                OAMEntry oe = FrameToDump(f);

                // Zero-size OAM entry from invalid SharpTable entry → skip.
                if (oe.WidthTiles <= 0 || oe.HeightTiles <= 0) continue;

                int pixW = oe.WidthTiles * 8;
                int pixH = oe.HeightTiles * 8;

                // G4d: src origin in the parts sheet.
                int src_x = (int)((oe.Tile % (uint)graphicsWidth) * 8);
                int src_y = (int)((oe.Tile / (uint)graphicsWidth) * 8);

                // Destination on the canvas.
                int dst_x = originX + oe.ImageX;
                int dst_y = originY + oe.ImageY;

                // Apply paletteShift (normally 0 for border sheets, but port faithfully).
                int paletteShift = oe.PaletteShift * 16;

                // Blit pixW×pixH pixels with optional flips, skipping source index 0.
                BitBltRGBA(
                    pixels, canvasW, canvasH,
                    dst_x, dst_y,
                    pixW, pixH,
                    partsPixels, partsW, partsH,
                    src_x, src_y,
                    paletteShift,
                    oe.VFlipped, oe.HFlipped);
            }

            img.SetPixelData(pixels);
            return img;
        }

        // =====================================================================
        // BitBltRGBA — RGBA port of WF BitBlt (ImageUtil.cs:922).
        //
        // Key differences vs WF BitBlt:
        //   • Source is RGBA (not indexed), so "transparent" = alpha 0 OR the
        //     source pixel was palette index 0 in ByteToImage16Tile (already
        //     stored as alpha=0 per G4c). We skip any source pixel with alpha=0.
        //   • paletteShift applies to source pixels stored as RGBA — for border
        //     sheets paletteShift is always 0, but port faithfully.
        //   • All WF clip math ported: ydest<0, ysrc<0, ydest+h>dstH, etc.
        // =====================================================================

        static void BitBltRGBA(
            byte[] dst, int dstW, int dstH, int xdest, int ydest,
            int width, int height,
            byte[] src, int srcW, int srcH, int xsrc, int ysrc,
            int paletteShift, bool vflip, bool hflip)
        {
            // ---- Clip logic (mirrors WF BitBlt :935-973 exactly) ----
            if (ydest < 0) { ysrc += (-ydest); height -= (-ydest); ydest = 0; }
            if (ysrc  < 0) { height -= (-ysrc); ysrc = 0; }
            if (ysrc + height > srcH) height -= (ysrc + height) - srcH;
            if (ydest + height > dstH) height -= (ydest + height) - dstH;

            if (xdest < 0) { xsrc += (-xdest); width -= (-xdest); xdest = 0; }
            if (xsrc  < 0) { width -= (-xsrc); xsrc = 0; }
            if (xsrc + width > srcW) width -= (xsrc + width) - srcW;
            if (xdest + width > dstW) width -= (xdest + width) - dstW;

            if (height <= 0 || width <= 0) return;

            // Iterate pixels with flip variants.
            if (vflip)
            {
                if (hflip) // v+h flip
                {
                    for (int row = 0; row < height; row++)
                    {
                        int srcRow = ysrc + (height - 1 - row);
                        int dstRow = ydest + row;
                        for (int col = 0; col < width; col++)
                        {
                            int srcIdx = (srcRow * srcW + xsrc + (width - 1 - col)) * 4;
                            BlitPixelRGBA(dst, dstW, xdest + col, dstRow, src, srcIdx, paletteShift);
                        }
                    }
                }
                else // v only
                {
                    for (int row = 0; row < height; row++)
                    {
                        int srcRow = ysrc + row;
                        int dstRow = ydest + row;
                        for (int col = 0; col < width; col++)
                        {
                            int srcIdx = (srcRow * srcW + xsrc + (width - 1 - col)) * 4;
                            BlitPixelRGBA(dst, dstW, xdest + col, dstRow, src, srcIdx, paletteShift);
                        }
                    }
                }
            }
            else
            {
                if (hflip) // h only
                {
                    for (int row = 0; row < height; row++)
                    {
                        int srcRow = ysrc + (height - 1 - row);
                        int dstRow = ydest + row;
                        for (int col = 0; col < width; col++)
                        {
                            int srcIdx = (srcRow * srcW + xsrc + col) * 4;
                            BlitPixelRGBA(dst, dstW, xdest + col, dstRow, src, srcIdx, paletteShift);
                        }
                    }
                }
                else // no flip
                {
                    for (int row = 0; row < height; row++)
                    {
                        int srcRow = ysrc + row;
                        int dstRow = ydest + row;
                        for (int col = 0; col < width; col++)
                        {
                            int srcIdx = (srcRow * srcW + xsrc + col) * 4;
                            BlitPixelRGBA(dst, dstW, xdest + col, dstRow, src, srcIdx, paletteShift);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Copy one RGBA source pixel to the destination. G4c: if the source
        /// pixel has alpha=0 (palette index 0 via ByteToImage16Tile), skip —
        /// do NOT overwrite the background. paletteShift is always 0 for border
        /// sheets but ported faithfully.
        /// </summary>
        static void BlitPixelRGBA(byte[] dst, int dstW, int dstX, int dstY,
            byte[] src, int srcBase, int paletteShift)
        {
            if (srcBase < 0 || srcBase + 3 >= src.Length) return;
            // G4c: skip source pixels at index 0 (alpha=0 from ByteToImage16Tile).
            if (src[srcBase + 3] == 0) return;

            int dstBase = (dstY * dstW + dstX) * 4;
            if (dstBase < 0 || dstBase + 3 >= dst.Length) return;

            // For border sheets paletteShift=0, so just copy R/G/B/A unchanged.
            // (For other AP uses a palette shift would remap color indices, but
            // we are operating in RGBA space here — the shift has no equivalent
            // effect on already-decoded RGBA; copying is the correct faithful
            // port for the AP border case where paletteShift is always 0.)
            dst[dstBase + 0] = src[srcBase + 0]; // R
            dst[dstBase + 1] = src[srcBase + 1]; // G
            dst[dstBase + 2] = src[srcBase + 2]; // B
            dst[dstBase + 3] = src[srcBase + 3]; // A
        }

        // =====================================================================
        // Static convenience: parse + render in one call.
        // =====================================================================

        /// <summary>
        /// Parse the AP data at <paramref name="apAddr"/> in
        /// <paramref name="romData"/> and render <paramref name="frameIndex"/>
        /// onto a transparent <paramref name="canvasW"/>×<paramref name="canvasH"/>
        /// canvas. Returns <c>null</c> on any parse/render failure. Never throws.
        /// </summary>
        public static IImage RenderFrame(byte[] romData, uint apAddr,
            int frameIndex, int originX, int originY,
            IImage parts, int canvasW = 256, int canvasH = 160)
        {
            if (romData == null || parts == null) return null;
            var ap = new ImageUtilAPCore();
            if (!ap.Parse(romData, apAddr)) return null;
            return ap.RenderFrame(parts, frameIndex, originX, originY, canvasW, canvasH);
        }

        // =====================================================================
        // Low-level ROM read helpers — operate on the passed romData array.
        // =====================================================================

        static uint ReadU16(byte[] data, uint addr)
        {
            if ((ulong)addr + 2 > (ulong)data.Length) return 0;
            return (uint)(data[addr] | (data[addr + 1] << 8));
        }

        bool IsSafetyOffset(uint addr)
        {
            // Mirrors U.isSafetyOffset: addr must be above the GBA header danger
            // zone AND below the ROM length. We use the romData length recorded
            // during Parse for all bounds checks inside this instance.
            if (_romDataLen == 0) return false;
            if (addr < 0x200) return false;  // danger zone (GBA ROM header area)
            return addr < (uint)_romDataLen;
        }

        bool IsRegionSafe(uint addr, int bytes)
        {
            if (_romDataLen == 0) return false;
            if (addr < 0x200) return false;
            ulong lastByte = (ulong)addr + (ulong)bytes - 1UL;
            return lastByte < (ulong)_romDataLen;
        }
    }
}
