using System;
using System.Collections.Generic;

namespace FEBuilderGBA
{
    /// <summary>
    /// Platform-independent image operations extracted from WinForms ImageUtil.
    /// Uses IImageService/IImage instead of System.Drawing.
    /// </summary>
    public static class ImageUtilCore
    {
        /// <summary>
        /// Load 4bpp tiles from ROM at the given offset.
        /// Decompresses LZ77 if isCompressed is true.
        /// Returns indexed IImage with the given palette.
        /// </summary>
        public static IImage LoadROMTiles4bpp(uint offset, byte[] gbaPalette, int tileCountX, int tileCountY, bool isCompressed = false)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null)
                return null;

            byte[] tileData;
            if (isCompressed)
            {
                tileData = LZ77.decompress(rom.Data, offset);
                if (tileData == null) return null;
            }
            else
            {
                int dataLen = tileCountX * tileCountY * 32; // 32 bytes per 8x8 tile at 4bpp
                if (offset + dataLen > (uint)rom.Data.Length) return null;
                tileData = new byte[dataLen];
                Array.Copy(rom.Data, offset, tileData, 0, dataLen);
            }

            int width = tileCountX * 8;
            int height = tileCountY * 8;
            int colorCount = Math.Min(gbaPalette.Length / 2, 16);

            return CoreState.ImageService.Decode4bppTiles(tileData, 0, width, height, gbaPalette);
        }

        /// <summary>
        /// Load 8bpp tiles from ROM at the given offset.
        /// </summary>
        public static IImage LoadROMTiles8bpp(uint offset, byte[] gbaPalette, int tileCountX, int tileCountY, bool isCompressed = false)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null)
                return null;

            byte[] tileData;
            if (isCompressed)
            {
                tileData = LZ77.decompress(rom.Data, offset);
                if (tileData == null) return null;
            }
            else
            {
                int dataLen = tileCountX * tileCountY * 64; // 64 bytes per 8x8 tile at 8bpp
                if (offset + dataLen > (uint)rom.Data.Length) return null;
                tileData = new byte[dataLen];
                Array.Copy(rom.Data, offset, tileData, 0, dataLen);
            }

            int width = tileCountX * 8;
            int height = tileCountY * 8;
            int colorCount = Math.Min(gbaPalette.Length / 2, 256);

            return CoreState.ImageService.Decode8bppTiles(tileData, 0, width, height, gbaPalette);
        }

        /// <summary>
        /// Read a GBA palette from the ambient <see cref="CoreState.ROM"/>
        /// (array of 16-bit colors). Prefer the explicit-<paramref name="rom"/>
        /// overload in cross-platform Core seams so the read is consistent with
        /// the ROM instance the caller is working on (non-global contexts).
        /// </summary>
        public static byte[] GetPalette(uint offset, int colorCount = 16)
        {
            return GetPalette(CoreState.ROM, offset, colorCount);
        }

        /// <summary>
        /// Read a GBA palette from the GIVEN <paramref name="rom"/> (array of
        /// 16-bit colors). rom-consistent overload: a Core seam that already has
        /// a <c>ROM</c> instance must use this so it never silently reads palette
        /// bytes from a different ambient <see cref="CoreState.ROM"/> (#993
        /// Copilot review; same class of latent bug as #992).
        /// </summary>
        public static byte[] GetPalette(ROM rom, uint offset, int colorCount = 16)
        {
            if (rom == null) return null;

            int byteLen = colorCount * 2;
            if (offset + byteLen > (uint)rom.Data.Length)
                return null;

            byte[] palette = new byte[byteLen];
            Array.Copy(rom.Data, offset, palette, 0, byteLen);
            return palette;
        }

        /// <summary>
        /// Read a compressed palette from ROM (LZ77).
        /// </summary>
        public static byte[] GetCompressedPalette(uint offset, int colorCount = 16)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            byte[] decompressed = LZ77.decompress(rom.Data, offset);
            if (decompressed == null) return null;

            int byteLen = colorCount * 2;
            if (decompressed.Length < byteLen)
                return decompressed;

            byte[] palette = new byte[byteLen];
            Array.Copy(decompressed, palette, byteLen);
            return palette;
        }

        /// <summary>
        /// Decode TSA (Tile Screen Arrangement) data to produce a tile map.
        /// TSA entries are 16-bit: bits 0-9 = tile index, bits 10-11 = flip, bits 12-15 = palette.
        /// </summary>
        /// <param name="opaqueIndex0">
        /// When <c>false</c> (default), palette index 0 renders as transparent
        /// (alpha 0) -- the standard sprite/TSA convention. When <c>true</c>,
        /// index 0 renders OPAQUE (alpha 255). The battle-screen preview
        /// (#802) needs this because WinForms <c>ImageUtil.BitBlt</c> blits the
        /// battle screen with <c>transparent_index = 0xFF</c> which (since 4bpp
        /// color indices are 0..15) never matches -- so index 0 is opaque in
        /// the WF battle screen. This trailing optional param keeps all existing
        /// callers default-preserving (they pass &lt;= 7 positional args).
        /// </param>
        public static IImage DecodeTSA(byte[] tileData, byte[] tsaData, byte[] gbaPalette,
            int screenWidthTiles, int screenHeightTiles, bool is4bpp = true, int tsaOffset = 0,
            bool opaqueIndex0 = false)
        {
            if (CoreState.ImageService == null) return null;

            int width = screenWidthTiles * 8;
            int height = screenHeightTiles * 8;

            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA

            int maxEntries = screenWidthTiles * screenHeightTiles;
            int availableEntries = (tsaData.Length - tsaOffset) / 2;
            int tsaEntryCount = Math.Min(availableEntries, maxEntries);

            for (int i = 0; i < tsaEntryCount; i++)
            {
                int bytePos = tsaOffset + i * 2;
                ushort tsaEntry = (ushort)(tsaData[bytePos] | (tsaData[bytePos + 1] << 8));
                int tileIndex = tsaEntry & 0x3FF;
                bool hFlip = (tsaEntry & 0x400) != 0;
                bool vFlip = (tsaEntry & 0x800) != 0;
                int palIndex = (tsaEntry >> 12) & 0xF;

                int tileX = (i % screenWidthTiles) * 8;
                int tileY = (i / screenWidthTiles) * 8;

                DecodeTileToPixels(tileData, tileIndex, gbaPalette, palIndex,
                    pixels, width, tileX, tileY, hFlip, vFlip, is4bpp, opaqueIndex0);
            }

            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Result of <see cref="DecodeHeaderTSAToCells"/>: the 32-wide
        /// bottom-to-top-stride <c>tile[]</c> array plus the decoded header
        /// dimensions, and an explicit <see cref="IsValidHeader"/> bit that
        /// distinguishes a genuine header decode from the corrupt/fallback
        /// cases (<c>tsaData.Length &lt; 2</c>, <c>masterHeaderX/Y &gt; 32</c>,
        /// or a header start <c>n &gt;= size</c>).
        ///
        /// <para>The renderer (<see cref="DecodeHeaderTSA"/>) keeps its existing
        /// public fallback behavior byte/pixel-identical by checking this bit;
        /// the editor path (<c>ImageTSAEditorCore.DecodeHeaderTsaCells</c>) only
        /// exposes editable cells when <see cref="IsValidHeader"/> is true, so
        /// the ViewModel never enables per-cell editing from fallback cells
        /// (Copilot review on #1071).</para>
        /// </summary>
        public readonly struct HeaderTSACells
        {
            /// <summary>The decoded 32-wide bottom-to-top-stride tile array
            /// (null when the input was empty / unusable).</summary>
            public readonly ushort[] Tile;
            /// <summary>Decoded <c>masterHeaderX = tsaData[0]</c>. 0 only for the
            /// too-short fallback; for an oversized-header fallback this carries
            /// the raw (out-of-range) header byte, so callers must check
            /// <see cref="IsValidHeader"/> before trusting it.</summary>
            public readonly int MasterHeaderX;
            /// <summary>Decoded <c>masterHeaderY = tsaData[1]</c>. 0 only for the
            /// too-short fallback; for an oversized-header fallback this carries
            /// the raw (out-of-range) header byte, so callers must check
            /// <see cref="IsValidHeader"/> before trusting it.</summary>
            public readonly int MasterHeaderY;
            /// <summary>True only for a genuine in-range header decode; false for
            /// every corrupt/fallback case <see cref="DecodeHeaderTSA"/> would
            /// linear-decode or blank.</summary>
            public readonly bool IsValidHeader;

            public HeaderTSACells(ushort[] tile, int mhx, int mhy, bool valid)
            {
                Tile = tile;
                MasterHeaderX = mhx;
                MasterHeaderY = mhy;
                IsValidHeader = valid;
            }
        }

        /// <summary>
        /// Decode a header-TSA stream into the 32-wide bottom-to-top-stride
        /// <c>tile[]</c> array shared by the renderer, the per-cell editor, and
        /// the serializer (#1071). This is the EXACT stride that
        /// <see cref="DecodeHeaderTSA"/> used inline, extracted verbatim so there
        /// is ONE geometry (no duplication):
        /// <code>
        ///   i = 2; n = masterHeaderY &lt;&lt; 5;
        ///   for (headery 0..mhy) { for (headerx 0..mhx) { tile[n] = entry + addend; i+=2; n++; }
        ///                          n -= masterHeaderX; n -= 0x21; }
        /// </code>
        ///
        /// <para><see cref="HeaderTSACells.IsValidHeader"/> is false for the same
        /// corrupt cases <see cref="DecodeHeaderTSA"/> falls back on
        /// (<c>tsaData.Length &lt; 2</c>, <c>masterHeaderX/Y &gt; 32</c>, header
        /// start <c>n &gt;= size</c>) so callers can preserve fallback rendering
        /// while refusing to edit fallback cells. Never throws.</para>
        ///
        /// <para><c>SerializeHeaderTSA</c> is the EXACT inverse of this fill for
        /// the editor path (<paramref name="tsaAddend"/> == 0).</para>
        /// </summary>
        /// <param name="tsaData">The (already decompressed / raw-sliced) TSA
        ///   stream: a 2-byte <c>{masterHeaderX, masterHeaderY}</c> header
        ///   followed by little-endian u16 cells in stride order.</param>
        /// <param name="screenWidthTiles">Canvas width in 8-pixel tiles.</param>
        /// <param name="screenHeightTiles">Canvas height in 8-pixel tiles.</param>
        /// <param name="tsaAddend">Per-entry addend (BigCG uses a nonzero value;
        ///   the editor path uses 0). The serializer's <c>tsaSubtrahend</c> is
        ///   the inverse.</param>
        public static HeaderTSACells DecodeHeaderTSAToCells(byte[] tsaData,
            int screenWidthTiles, int screenHeightTiles, int tsaAddend = 0)
        {
            int size = screenWidthTiles * screenHeightTiles;

            if (tsaData == null || tsaData.Length < 2)
                return new HeaderTSACells(null, 0, 0, false);

            int masterHeaderX = tsaData[0];
            int masterHeaderY = tsaData[1];
            if (masterHeaderX > 32 || masterHeaderY > 32)
                return new HeaderTSACells(null, masterHeaderX, masterHeaderY, false);

            if (masterHeaderX * masterHeaderY > size)
                size = masterHeaderX * masterHeaderY;
            if (size <= 0)
                return new HeaderTSACells(null, masterHeaderX, masterHeaderY, false);

            ushort[] tile = new ushort[size];

            int length = 2 + (size * 2);
            length = Math.Min(length, tsaData.Length);

            int i = 2; // skip header

            // Start position: bottom-to-top fill matching WinForms ByteToHeaderTSA
            int n = masterHeaderY << 5; // masterHeaderY * 32
            if (n >= size)
                return new HeaderTSACells(null, masterHeaderX, masterHeaderY, false);

            for (int headery = 0; headery <= masterHeaderY; headery++)
            {
                for (int headerx = 0; headerx <= masterHeaderX; headerx++)
                {
                    if (i + 1 >= length) goto done;
                    if (n >= tile.Length) goto done;

                    ushort tsadata = (ushort)(tsaData[i] | (tsaData[i + 1] << 8));
                    tile[n] = (ushort)(tsadata + tsaAddend);

                    i += 2;
                    n++;
                }
                n = n - masterHeaderX;
                n = n - (0x42 / 2); // = n - 0x21
            }

            done:
            return new HeaderTSACells(tile, masterHeaderX, masterHeaderY, true);
        }

        /// <summary>
        /// Serialize a 32-wide bottom-to-top-stride <c>tile[]</c> array back to a
        /// header-TSA byte stream — the EXACT inverse of
        /// <see cref="DecodeHeaderTSAToCells"/> for the editor path
        /// (<c>tsaAddend == 0</c>): emit the 2-byte
        /// <c>{masterHeaderX, masterHeaderY}</c> header, then walk the SAME
        /// stride emitting <c>(ushort)(tile[n] - tsaSubtrahend)</c> little-endian.
        /// <code>
        ///   out[0] = mhx; out[1] = mhy;
        ///   i = 2; n = mhy &lt;&lt; 5;
        ///   for (headery 0..mhy) { for (headerx 0..mhx) { v = tile[n]-sub; out[i]=v&amp;0xFF; out[i+1]=v&gt;&gt;8; i+=2; n++; }
        ///                          n -= mhx; n -= 0x21; }
        /// </code>
        /// Output length = <c>2 + (mhx+1)*(mhy+1)*2</c>. The original
        /// <c>{mhx, mhy}</c> header is PRESERVED (never recomputed from the
        /// min-clamped canvas dimensions).
        ///
        /// <para>Returns a 2-byte degenerate <c>{0,0}</c> output (NOT a throw) for
        /// any invalid input — null tile, <paramref name="masterHeaderX"/> /
        /// <paramref name="masterHeaderY"/> out of <c>[0,32]</c>, a header start
        /// <c>n &gt;= tile.Length</c>, or a per-cell index out of range. A
        /// ROM-mutating caller MUST treat any output whose length is not the
        /// expected <c>2 + (mhx+1)*(mhy+1)*2</c> as an error and refuse to write
        /// (Copilot review on #1071).</para>
        /// </summary>
        /// <param name="tile">The 32-wide bottom-to-top-stride tile array (e.g.
        ///   from <see cref="DecodeHeaderTSAToCells"/>).</param>
        /// <param name="masterHeaderX">The ORIGINAL header X (preserved verbatim).</param>
        /// <param name="masterHeaderY">The ORIGINAL header Y (preserved verbatim).</param>
        /// <param name="tsaSubtrahend">Per-entry subtrahend (inverse of the
        ///   decoder's addend; 0 for the editor path).</param>
        public static byte[] SerializeHeaderTSA(ushort[] tile, int masterHeaderX,
            int masterHeaderY, int tsaSubtrahend = 0)
        {
            if (tile == null) return new byte[2];
            if (masterHeaderX < 0 || masterHeaderY < 0) return new byte[2];
            if (masterHeaderX > 32 || masterHeaderY > 32) return new byte[2];

            int outLength = 2 + (masterHeaderX + 1) * (masterHeaderY + 1) * 2;
            byte[] outBytes = new byte[outLength];
            outBytes[0] = (byte)masterHeaderX;
            outBytes[1] = (byte)masterHeaderY;

            int i = 2; // skip header
            int n = masterHeaderY << 5; // masterHeaderY * 32
            if (n >= tile.Length) return new byte[2];

            for (int headery = 0; headery <= masterHeaderY; headery++)
            {
                for (int headerx = 0; headerx <= masterHeaderX; headerx++)
                {
                    if (i + 1 >= outLength) return new byte[2];
                    if (n < 0 || n >= tile.Length) return new byte[2];

                    ushort v = (ushort)(tile[n] - tsaSubtrahend);
                    outBytes[i] = (byte)(v & 0xFF);
                    outBytes[i + 1] = (byte)((v >> 8) & 0xFF);

                    i += 2;
                    n++;
                }
                n = n - masterHeaderX;
                n = n - (0x42 / 2); // = n - 0x21
            }

            return outBytes;
        }

        /// <summary>
        /// Decode TSA with a 2-byte header (used by Big CG, OP Prologue).
        /// Matches WinForms ImageUtil.ByteToHeaderTSA: reads header (width,height),
        /// then fills a 32-wide tile grid bottom-to-top starting at row=headerY.
        ///
        /// <para>Refactored (#1071) to obtain the stride-filled <c>tile[]</c> via
        /// the shared <see cref="DecodeHeaderTSAToCells"/> helper — the public
        /// render behavior (including the linear-<see cref="DecodeTSA"/> fallback
        /// for short / oversized / unusable headers) is preserved byte/pixel-
        /// identical via the helper's <see cref="HeaderTSACells.IsValidHeader"/>
        /// bit.</para>
        /// </summary>
        /// <param name="skipTile0">When true (the sprite/CG default), a TSA cell of
        ///   <c>0x0000</c> is treated as blank/transparent and skipped (matching the
        ///   pre-#1184 behavior). When false, cell 0 is a VALID tile-0 reference and
        ///   is rendered — required by the FE7 World Map big field map (#1184), whose
        ///   WF path <c>ByteToImage16TileInner</c> only skips <c>0xFFFF</c>, so tile 0
        ///   is a real background tile.</param>
        public static IImage DecodeHeaderTSA(byte[] tileData, byte[] tsaData, byte[] gbaPalette,
            int screenWidthTiles, int screenHeightTiles, bool is4bpp = true,
            int tsaAddend = 0, int paletteShift = 0, bool skipTile0 = true)
        {
            if (CoreState.ImageService == null) return null;

            // Shared geometry (#1071). When the header is NOT valid, reproduce the
            // EXACT pre-refactor fallbacks: a too-short / oversized header linear-
            // decodes the raw stream; a header whose start row is out of range
            // (tsaData.Length >= 2, mhx/mhy in range, but n >= size) blank-decodes
            // (DecodeTSA with an empty TSA) — byte/pixel-identical to before.
            HeaderTSACells decoded = DecodeHeaderTSAToCells(
                tsaData, screenWidthTiles, screenHeightTiles, tsaAddend);

            if (!decoded.IsValidHeader)
            {
                bool oversized = tsaData != null && tsaData.Length >= 2
                    && (tsaData[0] > 32 || tsaData[1] > 32);
                bool tooShort = tsaData == null || tsaData.Length < 2;
                if (tooShort || oversized)
                {
                    // Short / oversized header -> linear-decode the raw stream
                    // (matches the pre-#1071 fallback paths).
                    return DecodeTSA(tileData, tsaData ?? new byte[0], gbaPalette,
                        screenWidthTiles, screenHeightTiles, is4bpp, 0);
                }
                // In-range header but unusable start (n >= size) -> blank decode.
                return DecodeTSA(tileData, new byte[0], gbaPalette,
                    screenWidthTiles, screenHeightTiles, is4bpp, 0);
            }

            ushort[] tile = decoded.Tile;

            // Render using the decoded TSA tile array
            int width = screenWidthTiles * 8;
            int height = screenHeightTiles * 8;
            var image = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4];

            int tileLength = tile.Length;
            int x = 0, y = 0;

            for (int tsaindex = 0; tsaindex < tileLength; tsaindex++, x += 8)
            {
                if (x >= width) { x = 0; y += 8; if (y >= height) break; }

                ushort tsatile = tile[tsaindex];
                // 0xFFFF is always blank. 0x0000 is blank for sprites/CG (skipTile0
                // default) but a VALID tile-0 reference for the FE7 big-map
                // background (skipTile0=false — WF ByteToImage16TileInner only skips
                // 0xFFFF) (#1184).
                if (tsatile == 0xFFFF) continue;
                if (tsatile == 0 && skipTile0) continue;

                int tileIndex = tsatile & 0x3FF;
                bool hFlip = (tsatile & 0x400) != 0;
                bool vFlip = (tsatile & 0x800) != 0;
                int palIndex = ((tsatile >> 12) & 0xF);

                // Apply palette shift (e.g., BigCG uses -0x80 → shift palette index)
                int adjustedPalIndex = palIndex + paletteShift;
                if (adjustedPalIndex < 0) adjustedPalIndex = 0;
                if (adjustedPalIndex > 15) adjustedPalIndex = 15;

                DecodeTileToPixels(tileData, tileIndex, gbaPalette, adjustedPalIndex,
                    pixels, width, x, y, hFlip, vFlip, is4bpp);
            }

            image.SetPixelData(pixels);
            return image;
        }

        /// <summary>
        /// Decode the FE8 World Map "big field map" image: 4bpp tiles whose
        /// per-tile 16-color sub-palette is selected by a linear PALETTE-MAP
        /// nibble stream, blitted left-to-right then top-to-bottom into a single
        /// 256-color palette. PURE pixel math — does ZERO decompression
        /// (the caller LZ77-decompresses the palette-map and passes image +
        /// palette RAW). Mirrors WinForms
        /// <c>ImageUtil.ByteToImage16TilePaletteMap</c> (ImageUtil.cs:399) used
        /// only by FE8 <c>ImageUtilMap.DrawWorldMap</c>.
        ///
        /// <para><b>Tile layout (verified WF ImageUtil.cs:430-447).</b> The
        /// <paramref name="image"/> stream is consumed SEQUENTIALLY (32 bytes =
        /// one 8x8 4bpp tile, row-major: byte at <c>tileBase + y8*4 + x8/2</c>,
        /// low nibble = left pixel). Each consumed tile is placed at the running
        /// (x, y); after a tile x advances by 8, and at the end of a visible row
        /// (x &gt;= <paramref name="width"/>) x resets to 0 and y advances by
        /// 8.</para>
        ///
        /// <para><b>Palette-map (verified WF :419-428).</b> ONE nibble per tile,
        /// two tiles per byte: byte index <c>paletteMapIndex / 2</c>, even index
        /// =&gt; low nibble, odd index =&gt; high nibble. That nibble (0..15) ×
        /// 0x10 is the sub-palette base ADDED to every 4bpp pixel index, then
        /// the color is read from the 256-color <paramref name="gbaPalette"/> at
        /// <c>(nibble*16 + pixelIndex)</c>. Out-of-range palette-map reads return
        /// 0 (WF uses <c>U.at</c>) — never throws.</para>
        ///
        /// <para><b>The <c>+4</c> per-row quirk (verified WF :452-457).</b> At
        /// the end of EACH visible row the palette-map index advances by an EXTRA
        /// 4 (on top of the per-tile +1): the world-map palette-map has an
        /// off-screen right margin of 4 nibbles, so the row STRIDE is
        /// (tilesPerRow + 4) nibbles for tilesPerRow visible tiles (WF comment:
        /// "like HEADERTSA, there is off-screen margin").</para>
        ///
        /// <para><b>Partial render, NOT a throw (verified WF :411-412,
        /// 442-446).</b> The image length is clamped to
        /// <c>min(width*height/2, image.Length)</c>; if the stream runs out
        /// mid-tile the already-written pixels are returned as a PARTIAL image
        /// (the remaining pixels stay at their zero/transparent default). A SHORT
        /// image must NEVER throw — the <c>TryRenderMainFieldMap</c> region guard,
        /// not this primitive, is responsible for rejecting truncated inputs.</para>
        ///
        /// <para>Every decoded pixel is OPAQUE (alpha 255) — the big field map is
        /// a full background image, NOT a sprite, so index 0 is a real color
        /// here (unlike the TSA/sprite decoders where index 0 is transparent).</para>
        /// </summary>
        /// <param name="image">RAW (already-decompressed) 4bpp tile bytes.</param>
        /// <param name="paletteMap">RAW (already-LZ77-decompressed) palette-map
        /// nibble stream (one nibble per tile + the 4-nibble off-screen margin
        /// per row).</param>
        /// <param name="gbaPalette">RAW 256-color palette (512 bytes, BGR555 LE,
        /// 2 bytes/color).</param>
        /// <param name="width">Output width in pixels (default 480 = 60 tiles).</param>
        /// <param name="height">Output height in pixels (default 320 = 40 tiles).</param>
        /// <returns>An RGBA <see cref="IImage"/> of <paramref name="width"/> ×
        /// <paramref name="height"/>, or <c>null</c> only when there is no
        /// <see cref="CoreState.ImageService"/> or the args are degenerate
        /// (null buffers / non-positive dims). A short image yields a
        /// PARTIAL image, not null.</returns>
        public static IImage ByteToImage16TilePaletteMap(byte[] image, byte[] paletteMap,
            byte[] gbaPalette, int width = 480, int height = 320)
        {
            if (CoreState.ImageService == null) return null;
            if (image == null || paletteMap == null || gbaPalette == null) return null;
            if (width <= 0 || height <= 0) return null;

            var img = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA

            // WF clamps the END of the image stream to image.Length so a short
            // image renders partially instead of throwing (ImageUtil.cs:411-412).
            int length = (width * height) / 2;
            if (length > image.Length) length = image.Length;

            int x = 0;
            int y = 0;
            int paletteMapIndex = 0;

            // Iterate the image stream one 8x8 4bpp tile (32 bytes) at a time,
            // exactly like WF's outer `for (int i = image_pos; i < length; )`
            // with the inner y8/x8 byte-advance (so the partial cutoff can land
            // mid-tile — see the inner length check).
            int i = 0;
            while (i < length)
            {
                // --- Select this tile's sub-palette from the palette-map nibble
                // (two tiles per byte; even index = low nibble). U.at-safe:
                // out-of-range reads return 0, never throw (WF :419). ---
                uint subPaletteCap = U.at(paletteMap, paletteMapIndex / 2, 0);
                if ((paletteMapIndex & 0x1) > 0)
                    subPaletteCap = (subPaletteCap >> 4) & 0xF;
                else
                    subPaletteCap = subPaletteCap & 0xF;
                subPaletteCap = subPaletteCap * 0x10; // sub-palette base index

                // --- Decode the 8x8 4bpp tile (two pixels per byte), blitting at
                // the running (x, y). Mirrors WF :430-447 including the mid-tile
                // partial cutoff. ---
                bool truncated = false;
                for (int y8 = 0; y8 < 8 && !truncated; y8++)
                {
                    for (int x8 = 0; x8 < 8; x8 += 2)
                    {
                        byte a = image[i];
                        int idx0 = (int)(((a & 0x0F) + subPaletteCap) & 0xFF);
                        int idx1 = (int)((((a >> 4) & 0x0F) + subPaletteCap) & 0xFF);

                        WritePalettePixel(pixels, width, x + x8 + 0, y + y8, gbaPalette, idx0);
                        WritePalettePixel(pixels, width, x + x8 + 1, y + y8, gbaPalette, idx1);

                        i++;
                        if (i >= length)
                        {
                            // Out of image bytes mid-tile -> return the partial
                            // image (WF :442-446 returns early here).
                            truncated = true;
                            break;
                        }
                    }
                }

                x += 8;
                paletteMapIndex++;

                if (x >= width)
                {
                    x = 0;
                    y += 8;
                    // The world-map palette-map has a 4-nibble off-screen right
                    // margin, so skip 4 extra nibbles at each row end (WF :452-457).
                    paletteMapIndex += 4;
                }
            }

            img.SetPixelData(pixels);
            return img;
        }

        // =========================================================================
        // ByteToImage16Tile — pure 4bpp tile renderer with a SINGLE 16-color
        // palette slice. Used by the border AP parts-sheet (#849, NV5c).
        //
        // WF original: ImageUtil.ByteToImage16Tile (ImageUtil.cs:327).
        //   * Linear 4bpp tiles: 8x8, row-major, 2 pixels per byte (low=left).
        //   * Single 16-color palette at palOffset: 16 colors * 2 bytes = 32 bytes.
        //   * palette_count=0 (no paletteShift for the border parts sheet).
        //   * Palette index 0 → alpha 0 (TRANSPARENT): per G4, BitBlt is called with
        //     transparent_index=0 so index 0 is the transparent key — critical for
        //     the AP OAM blit: source pixels at index 0 must NOT overwrite the
        //     background.
        //   * Partial render: if bin runs out mid-tile the partial image is returned
        //     (WF :379-383 returns early; we match that).
        //   * Degenerate (width<=0 / empty bin) → null without throw.
        // =========================================================================

        /// <summary>
        /// Decode a raw 4bpp 8×8-tiled image as a single-palette RGBA sheet.
        /// <para>Used by the border parts sheet (#849): the WF path calls
        /// <c>ImageUtil.ByteToImage16Tile</c> with <c>palette_count=0</c>
        /// (no palette shift) and <c>transparent_index=0</c> — palette index 0
        /// is <b>transparent</b> (alpha 0) so the subsequent AP OAM blit skips
        /// source pixels at index 0.</para>
        ///
        /// <para><b>Partial render, NOT throw.</b> If <paramref name="bin"/> runs
        /// out mid-tile the already-written pixels are returned (mirrors WF :379-383
        /// which returns the bitmap early on mid-tile truncation).</para>
        ///
        /// <para>Returns <c>null</c> when <see cref="CoreState.ImageService"/> is
        /// null, <paramref name="width"/> &lt;= 0, <paramref name="height"/> &lt;= 0,
        /// <paramref name="bin"/> is null/empty, or <paramref name="palette"/> is
        /// null (degenerate inputs, never throws).</para>
        /// </summary>
        /// <param name="bin">Raw (uncompressed) 4bpp tile bytes.</param>
        /// <param name="binOffset">Byte offset into <paramref name="bin"/> where
        /// the first tile starts (matches WF <c>image_pos</c>).</param>
        /// <param name="palette">Raw palette buffer in GBA BGR555 LE format
        /// (2 bytes per color). The 16-color slice starts at
        /// <paramref name="palOffset"/>.</param>
        /// <param name="palOffset">Byte offset into <paramref name="palette"/>
        /// where the 16-color palette slice starts (matches WF
        /// <c>palette_pos</c>).</param>
        /// <param name="width">Output width in pixels (must be a multiple of 8).</param>
        /// <param name="height">Output height in pixels (must be a multiple of 8).</param>
        /// <returns>RGBA <see cref="IImage"/> of <paramref name="width"/>×
        /// <paramref name="height"/>, or <c>null</c> on degenerate input.</returns>
        public static IImage ByteToImage16Tile(byte[] bin, int binOffset,
            byte[] palette, int palOffset, int width, int height)
        {
            if (CoreState.ImageService == null) return null;
            if (bin == null || bin.Length == 0) return null;
            if (palette == null) return null;
            if (width <= 0 || height <= 0) return null;

            // Build the 16-color RGBA palette slice (index 0 → transparent).
            // Each GBA color is 2 bytes (BGR555 LE); we decode up to 16 entries.
            // Out-of-range palOffset or truncated palette → treat as default 0
            // (mirrors WF U.at-style tolerance; never throw).
            byte[] rgbaPal = new byte[16 * 4]; // RGBA
            for (int ci = 0; ci < 16; ci++)
            {
                int off = palOffset + ci * 2;
                if (off < 0 || off + 1 >= palette.Length)
                    break; // remaining entries stay black/transparent
                ushort gbaColor = (ushort)(palette[off] | (palette[off + 1] << 8));
                CoreState.ImageService.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);
                rgbaPal[ci * 4 + 0] = r;
                rgbaPal[ci * 4 + 1] = g;
                rgbaPal[ci * 4 + 2] = b;
                // index 0 is TRANSPARENT (alpha=0); indices 1-15 are OPAQUE.
                rgbaPal[ci * 4 + 3] = (ci == 0) ? (byte)0 : (byte)255;
            }

            var img = CoreState.ImageService.CreateImage(width, height);
            byte[] pixels = new byte[width * height * 4]; // RGBA, default=transparent

            // Iterate 4bpp tile stream (32 bytes per 8×8 tile, row-major across
            // width/8 columns). Matches WF :365-395 outer loop.
            int maxLen = binOffset + ((width * height) / 2);
            if (maxLen > bin.Length) maxLen = bin.Length;

            int x = 0;
            int y = 0;
            for (int i = binOffset; i < maxLen;)
            {
                for (int y8 = 0; y8 < 8; y8++)
                {
                    for (int x8 = 0; x8 < 8; x8 += 2)
                    {
                        byte a = bin[i];
                        int idx0 = a & 0x0F;       // low nibble = left pixel
                        int idx1 = (a >> 4) & 0x0F; // high nibble = right pixel

                        WriteTransparentPixel(pixels, width, x + x8,     y + y8, rgbaPal, idx0);
                        WriteTransparentPixel(pixels, width, x + x8 + 1, y + y8, rgbaPal, idx1);

                        i++;
                        if (i >= maxLen)
                        {
                            // Mid-tile truncation: return the partial image (WF :379-383).
                            img.SetPixelData(pixels);
                            return img;
                        }
                    }
                }
                x += 8;
                if (x >= width)
                {
                    x = 0;
                    y += 8;
                }
            }

            img.SetPixelData(pixels);
            return img;
        }

        /// <summary>
        /// Write one pixel from the 16-color transparent-0 palette into an RGBA
        /// buffer. Index 0 → skip (stays transparent). Bounds-safe (never throws).
        /// Used by <see cref="ByteToImage16Tile"/> for the border parts sheet.
        /// </summary>
        static void WriteTransparentPixel(byte[] pixels, int imageWidth, int destX, int destY,
            byte[] rgbaPal16, int colorIndex)
        {
            if (colorIndex == 0) return; // transparent — do NOT write
            if (destX < 0 || destX >= imageWidth || destY < 0) return;
            int palBase = colorIndex * 4;
            if (palBase + 3 >= rgbaPal16.Length) return;

            int idx = (destY * imageWidth + destX) * 4;
            if (idx < 0 || idx + 3 >= pixels.Length) return;

            pixels[idx + 0] = rgbaPal16[palBase + 0]; // R
            pixels[idx + 1] = rgbaPal16[palBase + 1]; // G
            pixels[idx + 2] = rgbaPal16[palBase + 2]; // B
            pixels[idx + 3] = rgbaPal16[palBase + 3]; // A (255 for index 1-15)
        }

        // =========================================================================
        // End ByteToImage16Tile
        // =========================================================================

        /// <summary>
        /// Write one OPAQUE palette-indexed pixel into an RGBA buffer for the big
        /// field map. Bounds-safe on both the destination (x &gt;= width or
        /// off-buffer is skipped) and the source palette (an out-of-range color
        /// index is skipped, leaving the pixel at its zero default — mirrors WF's
        /// <c>U.at</c> tolerance). Always alpha 255 (the big field map is an
        /// opaque background, so index 0 is a real color, unlike sprite/TSA
        /// decoders).
        /// </summary>
        static void WritePalettePixel(byte[] pixels, int imageWidth, int destX, int destY,
            byte[] gbaPalette, int colorIndex)
        {
            if (CoreState.ImageService == null) return;
            if (destX < 0 || destX >= imageWidth || destY < 0) return;

            int palByteOffset = colorIndex * 2;
            if (palByteOffset < 0 || palByteOffset + 2 > gbaPalette.Length) return;

            ushort gbaColor = (ushort)(gbaPalette[palByteOffset] | (gbaPalette[palByteOffset + 1] << 8));
            CoreState.ImageService.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b);

            int idx = (destY * imageWidth + destX) * 4;
            if (idx < 0 || idx + 3 >= pixels.Length) return;

            pixels[idx + 0] = r;
            pixels[idx + 1] = g;
            pixels[idx + 2] = b;
            pixels[idx + 3] = 255; // opaque background
        }

        /// <summary>
        /// Apply a 1-pixel black outline (Fuchidori) around opaque regions of
        /// an 8bpp indexed-pixel buffer. Ports WinForms <c>ImageUtil.Fuchidori</c>:
        /// for every transparent pixel (index 0) that is adjacent to an opaque
        /// pixel on the "outside" edge (left/right transparent → up/down not
        /// black, or up/down transparent → left/right not black), the pixel is
        /// rewritten to <paramref name="blackColorIndex"/>.
        ///
        /// The buffer is <c>width * height</c> bytes, row-major (stride = width).
        /// Used by the Portrait Import Wizard (#662) when the user toggles
        /// "Add black outline (Fuchidori)".
        /// </summary>
        /// <param name="indexedPixels">8bpp indexed buffer (1 byte per pixel). Modified in place.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="blackColorIndex">Palette index to use for the outline.</param>
        public static void Fuchidori(byte[] indexedPixels, int width, int height, byte blackColorIndex)
        {
            if (indexedPixels == null) return;
            if (width <= 0 || height <= 0) return;
            if (indexedPixels.Length < width * height) return;

            // Snapshot original pixels so the outline test inspects only the
            // source image, not partially-outlined output (matches the WF
            // double-buffer src/dest behavior).
            byte[] src = new byte[indexedPixels.Length];
            Array.Copy(indexedPixels, src, indexedPixels.Length);

            int endX = width - 1;
            int endY = height - 1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte cc = src[idx];

                    // Only fill currently-transparent pixels with the outline.
                    if (cc != 0) continue;

                    int transCount = 0;
                    bool isLTrans = false, isLBlack = false;
                    bool isRTrans = false, isRBlack = false;
                    bool isUTrans = false, isUBlack = false;
                    bool isDTrans = false, isDBlack = false;

                    if (x > 0)
                    {
                        byte cDest = indexedPixels[idx - 1];
                        if (cDest == blackColorIndex) isLBlack = true;
                        if (src[idx - 1] == 0) { isLTrans = true; transCount++; }
                    }
                    if (x < endX)
                    {
                        byte cDest = indexedPixels[idx + 1];
                        if (cDest == blackColorIndex) isRBlack = true;
                        if (src[idx + 1] == 0) { isRTrans = true; transCount++; }
                    }
                    if (y > 0)
                    {
                        byte cDest = indexedPixels[idx - width];
                        if (cDest == blackColorIndex) isUBlack = true;
                        if (src[idx - width] == 0) { isUTrans = true; transCount++; }
                    }
                    if (y < endY)
                    {
                        byte cDest = indexedPixels[idx + width];
                        if (cDest == blackColorIndex) isDBlack = true;
                        if (src[idx + width] == 0) { isDTrans = true; transCount++; }
                    }

                    // 3+ transparent neighbors — isolated pixel, no outline.
                    if (transCount >= 3) continue;

                    // Left/right transparent and up/down not both black -> outline.
                    if (isLTrans || isRTrans)
                    {
                        if (!isUBlack || !isDBlack)
                        {
                            indexedPixels[idx] = blackColorIndex;
                            continue;
                        }
                    }
                    // Up/down transparent and left/right not both black -> outline.
                    if (isUTrans || isDTrans)
                    {
                        if (!isLBlack || !isRBlack)
                        {
                            indexedPixels[idx] = blackColorIndex;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Find the palette index with the darkest color (lowest combined R+G+B)
        /// inside the half-open range [start, end). Defaults to <paramref name="start"/>
        /// when no candidate is found. Mirrors WinForms <c>ImageUtil.FindBlackColorFromPalette</c>.
        /// </summary>
        /// <param name="gbaPalette">Palette bytes (BGR555 LE, 2 bytes/color).</param>
        /// <param name="start">First palette index considered (inclusive).</param>
        /// <param name="end">Stop index (exclusive).</param>
        /// <returns>Index of the darkest color in the range.</returns>
        public static int FindBlackColorIndex(byte[] gbaPalette, int start, int end)
        {
            if (gbaPalette == null || gbaPalette.Length < 2) return start;
            if (CoreState.ImageService == null) return start;

            int maxColors = gbaPalette.Length / 2;
            if (end > maxColors) end = maxColors;
            if (start < 0) start = 0;
            if (start >= end) return start;

            int bestIndex = start;
            int bestSum = int.MaxValue;
            for (int i = start; i < end; i++)
            {
                ushort gba = (ushort)(gbaPalette[i * 2] | (gbaPalette[i * 2 + 1] << 8));
                CoreState.ImageService.GBAColorToRGBA(gba, out byte r, out byte g, out byte b);
                int sum = r + g + b;
                if (sum < bestSum)
                {
                    bestSum = sum;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        // internal (not private) so cross-platform editor helpers in the same
        // assembly -- e.g. ImageBattleScreenCore.RenderChipsetPreview (#805) --
        // can render a single tile with explicit flip + palette-bank +
        // opaque-index-0 control WITHOUT duplicating the 4bpp/8bpp decode loop.
        internal static void DecodeTileToPixels(byte[] tileData, int tileIndex, byte[] gbaPalette, int palIndex,
            byte[] pixels, int imageWidth, int tileX, int tileY, bool hFlip, bool vFlip, bool is4bpp,
            bool opaqueIndex0 = false)
        {
            if (CoreState.ImageService == null) return;

            int bytesPerTile = is4bpp ? 32 : 64;
            int tileOffset = tileIndex * bytesPerTile;
            if (tileOffset + bytesPerTile > tileData.Length) return;

            int palOffset = is4bpp ? palIndex * 16 * 2 : 0; // palette offset in bytes

            for (int py = 0; py < 8; py++)
            {
                int srcY = vFlip ? (7 - py) : py;
                for (int px = 0; px < 8; px++)
                {
                    int srcX = hFlip ? (7 - px) : px;

                    int colorIndex;
                    if (is4bpp)
                    {
                        int bytePos = tileOffset + srcY * 4 + srcX / 2;
                        if (bytePos >= tileData.Length) continue;
                        byte b = tileData[bytePos];
                        colorIndex = (srcX % 2 == 0) ? (b & 0x0F) : ((b >> 4) & 0x0F);
                    }
                    else
                    {
                        int bytePos = tileOffset + srcY * 8 + srcX;
                        if (bytePos >= tileData.Length) continue;
                        colorIndex = tileData[bytePos];
                    }

                    // Convert to RGBA
                    int palByteOffset = palOffset + colorIndex * 2;
                    if (palByteOffset + 2 > gbaPalette.Length) continue;

                    ushort gbaColor = (ushort)(gbaPalette[palByteOffset] | (gbaPalette[palByteOffset + 1] << 8));
                    CoreState.ImageService.GBAColorToRGBA(gbaColor, out byte r, out byte g, out byte b2);

                    int destX = tileX + px;
                    int destY = tileY + py;
                    if (destX >= imageWidth) continue;

                    int idx = (destY * imageWidth + destX) * 4;
                    if (idx + 3 >= pixels.Length) continue;

                    pixels[idx + 0] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b2;
                    // index 0 = transparent (default) unless opaqueIndex0 forces
                    // alpha 255 -- the battle-screen preview (#802) blits index 0
                    // opaque to match WinForms BitBlt (transparent_index = 0xFF).
                    pixels[idx + 3] = (byte)((colorIndex == 0 && !opaqueIndex0) ? 0 : 255);
                }
            }
        }

        // =========================================================================
        // EncodePaletteMap16Tile — EXACT INVERSE of ByteToImage16TilePaletteMap's
        // nibble walk (#875, WF ImageUtil.ImageToPaletteMap:2211).
        //
        // The world-map palette-map is a nibble stream: one nibble per 8×8 tile
        // selecting the sub-palette (0–15), stored two tiles per byte
        //   even tile index → low nibble  (& 0x0F)
        //   odd  tile index → high nibble (>> 4)
        // plus a 4-nibble right-margin skip at each row end
        //   nn += 4  per row (mirrors WF :2268  "nn += 4").
        //
        // Buffer size: (width/2 + 4) * height bytes (WF :2214).
        // Encoding: per 8×8 tile, compute selectpalette = indexedPixels[any pixel
        // in the tile] / 16 (the FIRST pixel at tile origin; the caller MUST have
        // already validated all pixels in each tile use the SAME sub-palette —
        // ByteToImage16TilePaletteMap round-trip test enforces this).
        // Returns the encoded palette-map byte array (never null; empty on bad args).
        // =========================================================================

        /// <summary>
        /// Encode a flat indexed-pixel buffer (1 byte/pixel, values 0–63 where
        /// <c>value / 16</c> is the sub-palette index) into the GBA world-map
        /// palette-map nibble stream — the EXACT inverse of
        /// <see cref="ByteToImage16TilePaletteMap"/>'s nibble walk (#875).
        ///
        /// <para>Buffer layout: <c>(width/2 + 4) * height</c> bytes. Per 8×8 tile
        /// the sub-palette is written as a nibble at position <c>nn/2</c>: even
        /// tile counter → low nibble; odd → high nibble. A 4-nibble right-margin
        /// gap is added at each row end (<c>nn += 4</c>), matching WF
        /// <c>ImageUtil.ImageToPaletteMap :2268</c>.</para>
        ///
        /// <para>The caller is responsible for validating that each 8×8 tile is
        /// mono-sub-palette before calling (WF validates and returns an error
        /// string if not). This method reads the first pixel of each tile to
        /// determine the sub-palette; the result is byte-exact for validated
        /// inputs.</para>
        /// </summary>
        /// <param name="indexedPixels">1 byte per pixel, row-major (width*height
        /// bytes). Each pixel value is a palette index 0–63 where
        /// <c>value / 16</c> is the sub-palette.</param>
        /// <param name="width">Image width in pixels (multiple of 8).</param>
        /// <param name="height">Image height in pixels (multiple of 8).</param>
        /// <returns>Encoded palette-map byte array of size
        /// <c>(width/2 + 4) * height</c>; empty on degenerate input.</returns>
        public static byte[] EncodePaletteMap16Tile(byte[] indexedPixels, int width, int height)
        {
            if (indexedPixels == null || width <= 0 || height <= 0) return new byte[0];
            if (width % 8 != 0 || height % 8 != 0) return new byte[0];
            if (indexedPixels.Length < width * height) return new byte[0];

            // Buffer size: (width/2 + 4) * height  — mirrors WF :2214.
            byte[] palettemap = new byte[(width / 2 + 4) * height];

            int nn = 0; // nibble counter — same variable as WF :2220
            for (int y = 0; y < height; y += 8)
            {
                for (int x = 0; x < width; x += 8)
                {
                    // Determine this tile's sub-palette from the first pixel
                    // (all pixels must be the same sub-palette — validated by caller).
                    int firstPixel = indexedPixels[y * width + x];
                    uint selectpalette = (uint)(firstPixel / 16);

                    // Write nibble: even nn → low nibble; odd nn → high nibble.
                    // Mirrors WF :2255-2264.
                    if ((nn & 0x01) > 0)
                    {   // odd → high nibble
                        byte a = palettemap[nn / 2];
                        palettemap[nn / 2] = (byte)((a & 0x0F) | ((selectpalette & 0xF) << 4));
                    }
                    else
                    {   // even → low nibble
                        palettemap[nn / 2] = (byte)(selectpalette & 0xF);
                    }
                    nn++;
                }
                // 4-nibble right-margin gap per row — WF :2268 "nn += 4".
                nn += 4;
            }
            return palettemap;
        }
    }
}
