using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform Map Action Animation export/import engine. Ports the
    /// WinForms <c>ImageUtilMapActionAnimation.Export</c> /
    /// <c>ExportGif</c> / <c>Import</c> trio to Core so the Avalonia
    /// <c>ImageMapActionAnimationView</c> can drive Export / Import / Open
    /// Source / Select Source buttons through the
    /// <c>ImageMapActionAnimationViewModel</c> (#499 — closes WF-only labels
    /// `アニメーション取出` / `アニメーション読込` / `ソースファイルを開く` /
    /// `ソースフォルダーを開く`).
    ///
    /// Frame table layout (12 bytes per entry, terminated by 12 zero bytes):
    /// <code>
    ///   byte  wait;        // GBA 60-fps wait counter
    ///   byte  padding;     // always 0
    ///   ushort sound;      // music ID (0 = silent)
    ///   void*  imgPtr;     // GBA pointer to LZ77-compressed 4bpp tile data
    ///   void*  palPtr;     // GBA pointer to raw 16-color palette (0x20 bytes)
    /// </code>
    /// Rendered as 64x64 4bpp image (8x8 tiles).
    ///
    /// Script format (`.MapActionAnimation.txt`):
    /// <code>
    /// //NAME=&lt;display name&gt;
    /// # RAW-TILES: &lt;hex bytes of post-LZ77-decompress 4bpp tile data&gt;
    /// # RAW-PALETTE: &lt;hex bytes of 32-byte raw GBA palette&gt;
    /// &lt;waitDec&gt;TAB&lt;pngFilename&gt;[TAB&lt;0xsoundHex&gt;]
    /// </code>
    ///
    /// The optional RAW-* comment blocks let Export/Import round-trip be
    /// byte-equal even when the user does not re-edit the PNG. The Import
    /// path reads the immediately preceding RAW block (if any) per frame
    /// line; absent RAW data, it falls back to PNG-based quantization via
    /// <see cref="DecreaseColorCore.Quantize"/>.
    ///
    /// Implementation notes (Copilot CLI plan reviews 1-3):
    /// * **C1 / B1** Indexed-pixel <c>IImage</c> from <c>Decode4bppTiles</c> is
    ///   expanded to RGBA via <c>GifEncoderCore.IndexedToRgba</c> before
    ///   feeding the GIF encoder.
    /// * **B2** <see cref="PadGBAPaletteTo16"/> normalizes any quantize
    ///   output (which can be smaller than 32 bytes) to exactly 16 colors
    ///   × 2 bytes — the on-ROM palette size every consumer expects.
    /// * **C3** <c>ImageImportCore.FindAndWriteData</c> handles free-space
    ///   placement (with append fallback). No raw <c>AppendToRomEnd</c>.
    /// * **C4** GIF delays go through <see cref="U.GameFrameSecToGifFrameSec"/>
    ///   so the rounding matches WinForms exactly (wait=1 → 2cs, not 1cs).
    /// * **C5** Export dedup keys on the tuple <c>(imgPtr, palPtr)</c>, not
    ///   their sum.
    /// * **A3** Importer recognises <c>#&#32;RAW-TILES:</c> / <c>#&#32;RAW-PALETTE:</c>
    ///   BEFORE generic <c>U.IsComment</c> skipping. RAW blocks attach to
    ///   the next data line (which lets duplicate frame references each
    ///   carry their own preserved bytes).
    /// </summary>
    public static class MapActionAnimationExportImportCore
    {
        public const int SCREEN_WIDTH = 64;
        public const int SCREEN_HEIGHT = 64;
        public const int FRAME_ENTRY_SIZE = 12;
        public const int PALETTE_SIZE_BYTES = 32; // 16 colors × 2 bytes (GBA 5-5-5 BGR)

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Write a Map Action Animation to a <c>.MapActionAnimation.txt</c>
        /// script (with optional per-frame RAW-* preservation) + per-unique-frame
        /// PNG companion files placed alongside the script.
        /// </summary>
        /// <param name="rom">Source ROM.</param>
        /// <param name="animeAddr">ROM offset of the 12-byte frame entry table.</param>
        /// <param name="txtPath">Output .txt path (PNGs land in the same directory).</param>
        /// <param name="name">Display name (becomes `//NAME=<name>` header).</param>
        /// <param name="includeRaw">When true, emit RAW-TILES + RAW-PALETTE
        /// comments per frame so import can round-trip byte-equal. Default true.</param>
        /// <returns>Empty on success, error message otherwise.</returns>
        public static string ExportScript(ROM rom, uint animeAddr, string txtPath, string name, bool includeRaw = true)
        {
            if (rom == null) return "ROM is not loaded.";
            if (string.IsNullOrEmpty(txtPath)) return "Output path is empty.";
            if (CoreState.ImageService == null) return "ImageService is not configured.";

            animeAddr = U.toOffset(animeAddr);
            if (!U.isSafetyOffset(animeAddr, rom))
                return $"Invalid animation address: 0x{animeAddr:X08}";

            string baseName = Path.GetFileNameWithoutExtension(txtPath);
            // Strip the .MapActionAnimation chunk so filenames don't double up.
            if (baseName.EndsWith(".MapActionAnimation", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - ".MapActionAnimation".Length);
            string outDir = Path.GetDirectoryName(Path.GetFullPath(txtPath));
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var lines = new List<string>();
            if (!string.IsNullOrEmpty(name))
                lines.Add("//NAME=" + name);

            // Dedup map — (imgPtr, palPtr) tuple → PNG filename (Plan v3 C5).
            var dedup = new Dictionary<(uint imgPtr, uint palPtr), string>();
            // Per-frame RAW caches — separate from dedup so duplicate frame
            // lines each emit their own RAW-* block (Plan v3 A3 association).
            var rawTilesCache = new Dictionary<(uint imgPtr, uint palPtr), string>();
            var rawPaletteCache = new Dictionary<(uint imgPtr, uint palPtr), string>();

            uint id = 0;
            uint limiter = animeAddr + 1024u * 1024u;
            limiter = (uint)Math.Min(limiter, rom.Data.Length);

            for (uint n = animeAddr; n < limiter; n += FRAME_ENTRY_SIZE)
            {
                if (n + FRAME_ENTRY_SIZE > (uint)rom.Data.Length) break;
                uint term1 = rom.u32(n);
                uint imgPtr = rom.p32(n + 4);
                uint palPtr = rom.p32(n + 8);
                if (term1 == 0 && imgPtr == 0) break;

                uint wait = rom.u8(n + 0);
                uint sound = rom.u16(n + 2);

                var key = (imgPtr, palPtr);
                string pngFilename;
                if (dedup.TryGetValue(key, out var existingFile))
                {
                    pngFilename = existingFile;
                }
                else
                {
                    pngFilename = MakePngFilename(baseName, id);
                    id++;

                    // Decompress LZ77 image, read palette, render PNG.
                    if (!U.isSafetyOffset(imgPtr, rom))
                        return $"Bad image pointer in frame at 0x{n:X08}: 0x{imgPtr:X08}";
                    if (!U.isSafetyOffset(palPtr, rom))
                        return $"Bad palette pointer in frame at 0x{n:X08}: 0x{palPtr:X08}";

                    byte[] tiles = LZ77.decompress(rom.Data, imgPtr);
                    if (tiles == null || tiles.Length == 0)
                        return $"Failed to decompress LZ77 image at 0x{imgPtr:X08}";

                    // Copilot bot review on PR #620 round 2: read from the
                    // ROM passed by the caller, not CoreState.ROM (which
                    // ImageUtilCore.GetPalette uses internally). Keeps the
                    // export self-consistent across headless tooling, tests,
                    // and multi-ROM flows.
                    byte[] palette = ReadPaletteFromRom(rom, palPtr, 16);
                    if (palette == null)
                        return $"Failed to read palette at 0x{palPtr:X08}";
                    palette = PadGBAPaletteTo16(palette);

                    // Render and persist the PNG.
                    using (var img = CoreState.ImageService.Decode4bppTiles(
                        tiles, 0, SCREEN_WIDTH, SCREEN_HEIGHT, palette))
                    {
                        if (img == null)
                            return $"Failed to decode 4bpp tile data at 0x{imgPtr:X08}";
                        try
                        {
                            string pngPath = Path.Combine(outDir ?? "", pngFilename);
                            img.Save(pngPath);
                        }
                        catch (Exception ex)
                        {
                            return $"Failed to write PNG: {ex.Message}";
                        }
                    }

                    dedup[key] = pngFilename;
                    rawTilesCache[key] = BytesToHex(tiles);
                    rawPaletteCache[key] = BytesToHex(palette);
                }

                // Emit RAW-* comments per data line so byte-equal round-trip
                // survives duplicate frame references (Plan v3 A3).
                if (includeRaw)
                {
                    lines.Add("# RAW-TILES: " + rawTilesCache[key]);
                    lines.Add("# RAW-PALETTE: " + rawPaletteCache[key]);
                }

                string dataLine = wait.ToString(CultureInfo.InvariantCulture) + "\t" + pngFilename;
                if (sound != 0)
                    dataLine += "\t0x" + sound.ToString("X", CultureInfo.InvariantCulture);
                lines.Add(dataLine);
            }

            try
            {
                File.WriteAllLines(txtPath, lines);
            }
            catch (Exception ex)
            {
                return $"Failed to write script file: {ex.Message}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Export a Map Action Animation as an animated GIF.
        /// Each frame is rendered from indexed tile data through
        /// <c>GifEncoderCore.IndexedToRgba</c> (Plan v3 B1) and the GIF delay
        /// rounding uses <c>U.GameFrameSecToGifFrameSec</c> (Plan v2 C4).
        /// </summary>
        public static string ExportGif(ROM rom, uint animeAddr, string gifPath)
        {
            if (rom == null) return "ROM is not loaded.";
            if (string.IsNullOrEmpty(gifPath)) return "Output path is empty.";
            if (CoreState.ImageService == null) return "ImageService is not configured.";

            animeAddr = U.toOffset(animeAddr);
            if (!U.isSafetyOffset(animeAddr, rom))
                return $"Invalid animation address: 0x{animeAddr:X08}";

            var gifFrames = new List<GifEncoderCore.GifFrame>();
            uint limiter = animeAddr + 1024u * 1024u;
            limiter = (uint)Math.Min(limiter, rom.Data.Length);

            for (uint n = animeAddr; n < limiter; n += FRAME_ENTRY_SIZE)
            {
                if (n + FRAME_ENTRY_SIZE > (uint)rom.Data.Length) break;
                uint term1 = rom.u32(n);
                uint imgPtr = rom.p32(n + 4);
                uint palPtr = rom.p32(n + 8);
                if (term1 == 0 && imgPtr == 0) break;

                uint wait = rom.u8(n + 0);

                if (!U.isSafetyOffset(imgPtr, rom) || !U.isSafetyOffset(palPtr, rom))
                    continue; // skip bad frame

                byte[] tiles = LZ77.decompress(rom.Data, imgPtr);
                if (tiles == null || tiles.Length == 0) continue;
                // Read palette from the rom parameter, not CoreState.ROM —
                // Copilot bot review on PR #620 round 2.
                byte[] palette = ReadPaletteFromRom(rom, palPtr, 16);
                if (palette == null) continue;
                palette = PadGBAPaletteTo16(palette);

                using var img = CoreState.ImageService.Decode4bppTiles(
                    tiles, 0, SCREEN_WIDTH, SCREEN_HEIGHT, palette);
                if (img == null) continue;

                // Indexed-pixel IImage → RGBA buffer (Plan v3 B1).
                byte[] indexedPx = img.GetPixelData();
                byte[] rgbaPalette = img.GetPaletteRGBA();
                byte[] rgba = GifEncoderCore.IndexedToRgba(indexedPx, rgbaPalette, img.Width, img.Height);

                gifFrames.Add(new GifEncoderCore.GifFrame
                {
                    Width = img.Width,
                    Height = img.Height,
                    RgbaPixels = rgba,
                    DelayCs = U.GameFrameSecToGifFrameSec(wait), // Plan v2 C4
                });
            }

            if (gifFrames.Count == 0)
                return "No renderable frames in animation.";

            try
            {
                string outDir = Path.GetDirectoryName(Path.GetFullPath(gifPath));
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);
                GifEncoderCore.Encode(gifFrames, gifPath);
            }
            catch (Exception ex)
            {
                return $"Failed to write GIF: {ex.Message}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Import a Map Action Animation from a <c>.MapActionAnimation.txt</c>
        /// script. Honors per-frame <c># RAW-TILES:</c> / <c># RAW-PALETTE:</c>
        /// hints (Plan v3 A3) for byte-equal round-trip; falls back to PNG
        /// quantization via <see cref="DecreaseColorCore.Quantize"/> when
        /// no RAW block precedes a frame line.
        /// </summary>
        /// <param name="rom">Target ROM.</param>
        /// <param name="pointerAddr">ROM offset of the 32-bit pointer slot
        /// that holds the frame-table address.</param>
        /// <param name="txtPath">Input .MapActionAnimation.txt file.</param>
        /// <param name="imageLoader">Callback that turns a PNG path into
        /// <c>(rgba bytes, width, height)</c> or returns null when missing.</param>
        /// <returns>Empty on success, localized error string otherwise.</returns>
        public static string ImportScript(
            ROM rom,
            uint pointerAddr,
            string txtPath,
            Func<string, (byte[] rgba, int w, int h)?> imageLoader)
        {
            if (rom == null) return "ROM is not loaded.";
            if (string.IsNullOrEmpty(txtPath)) return "Script path is empty.";
            if (!File.Exists(txtPath))
                return $"Script file not found: {txtPath}";
            if (imageLoader == null)
                return "Image loader callback is null.";
            // Plan v3 + Copilot bot review on PR #620 (round 1 inline #4):
            // validate that pointerAddr is in-bounds for a 4-byte write so a
            // caller passing a garbage address gets a friendly error instead
            // of an IndexOutOfRangeException from rom.write_u32.
            if (!U.isSafetyOffset(pointerAddr, rom) || pointerAddr + 3 >= (uint)rom.Data.Length)
                return $"Invalid pointer address: 0x{pointerAddr:X08}";

            string baseDir = Path.GetDirectoryName(Path.GetFullPath(txtPath)) ?? "";
            string[] lines;
            try
            {
                lines = File.ReadAllLines(txtPath);
            }
            catch (Exception ex)
            {
                return $"Failed to read script file: {ex.Message}";
            }

            // Walk lines: track pending RAW blocks; on each data line, build
            // the frame entry (with imgPtr/palPtr placeholders) + a sheet
            // pool the dedup pass will collapse.
            var sheetPool = new List<byte[]>();       // LZ77-compressed image bytes
            var palettePool = new List<byte[]>();     // 32-byte raw palettes
            var frameEntries = new List<FrameEntry>();
            string scriptName = "";
            string pendingRawTiles = null;
            string pendingRawPalette = null;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                if (line == null) continue;
                string trimmed = line.TrimStart();

                // Plan v3 A3: RAW-* check BEFORE generic comment skip.
                if (trimmed.StartsWith("# RAW-TILES:"))
                {
                    pendingRawTiles = trimmed.Substring("# RAW-TILES:".Length).Trim();
                    continue;
                }
                if (trimmed.StartsWith("# RAW-PALETTE:"))
                {
                    pendingRawPalette = trimmed.Substring("# RAW-PALETTE:".Length).Trim();
                    continue;
                }

                // Capture the name from the //NAME= header before comment skip.
                if (trimmed.StartsWith("//NAME="))
                {
                    scriptName = trimmed.Substring("//NAME=".Length).Trim();
                    continue;
                }

                if (U.IsComment(trimmed))
                    continue;
                // `U.OtherLangLine` reads `CoreState.ROM.RomInfo.is_multibyte`
                // — guard against null RomInfo (some headless paths and the
                // Core.Tests synthetic ROM don't populate RomInfo).
                if (rom.RomInfo != null && U.OtherLangLine(trimmed, rom))
                    continue;

                // Strip trailing inline comment.
                trimmed = U.ClipComment(trimmed);
                if (string.IsNullOrEmpty(trimmed)) continue;

                string[] sp = trimmed.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length < 2)
                    sp = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length < 2)
                    continue;

                uint wait = U.atoi0x(sp[0]);
                string pngName = sp[1];
                uint sound = sp.Length >= 3 ? U.atoi0x(sp[2]) : 0;

                // Plan v3 + Copilot bot review on PR #620 (round 1 inline #5):
                // wait must fit in a byte (frame entry stores it as u8); sound
                // must fit in a u16. Silently truncating a malformed script
                // line would import the animation with wrong timing/sound.
                if (wait > 0xFF)
                    return $"Wait value {wait} at line {li + 1} exceeds the u8 frame-entry range (0..255).";
                if (sound > 0xFFFF)
                    return $"Sound value 0x{sound:X} at line {li + 1} exceeds the u16 frame-entry range (0..0xFFFF).";

                byte[] tileBytes;
                byte[] paletteBytes;

                if (pendingRawTiles != null && pendingRawPalette != null)
                {
                    tileBytes = HexToBytes(pendingRawTiles);
                    if (tileBytes == null || tileBytes.Length == 0)
                        return $"Invalid RAW-TILES hex at line {li + 1} (preceding frame line).";

                    // Plan v3 + Copilot bot review on PR #620 (round 1 inline #3):
                    // a malformed RAW-PALETTE silently produces an all-zero
                    // palette via PadGBAPaletteTo16(null). Validate explicitly
                    // so a typo in the .txt fails the import loudly instead
                    // of corrupting the palette.
                    byte[] rawPaletteBytes = HexToBytes(pendingRawPalette);
                    if (rawPaletteBytes == null)
                        return $"Invalid RAW-PALETTE hex at line {li + 1} (preceding frame line).";
                    paletteBytes = PadGBAPaletteTo16(rawPaletteBytes);
                }
                else
                {
                    // PNG path — load, crop/pad to 64x64, quantize, encode.
                    string fullPath = Path.Combine(baseDir, pngName);
                    var loaded = imageLoader(fullPath);
                    if (loaded == null)
                        return $"PNG not found or unreadable: {pngName} (line {li + 1})";
                    var (rgbaPixels, srcW, srcH) = loaded.Value;
                    if (srcW != SCREEN_WIDTH || srcH != SCREEN_HEIGHT)
                    {
                        rgbaPixels = CropOrPadRgba(rgbaPixels, srcW, srcH, SCREEN_WIDTH, SCREEN_HEIGHT);
                    }
                    var qr = DecreaseColorCore.Quantize(rgbaPixels, SCREEN_WIDTH, SCREEN_HEIGHT, 16);
                    if (qr == null)
                        return $"Failed to quantize {pngName} (line {li + 1})";

                    tileBytes = ImageImportCore.EncodeDirectTiles4bpp(qr.IndexData, SCREEN_WIDTH, SCREEN_HEIGHT);
                    if (tileBytes == null)
                        return $"Failed to encode tile data for {pngName} (line {li + 1})";
                    paletteBytes = PadGBAPaletteTo16(qr.GBAPalette); // Plan v3 B2
                }

                pendingRawTiles = null;
                pendingRawPalette = null;

                byte[] compressedTiles = LZ77.compress(tileBytes);

                int sheetIdx = FindMatchingBlob(sheetPool, compressedTiles);
                if (sheetIdx < 0) { sheetIdx = sheetPool.Count; sheetPool.Add(compressedTiles); }

                int palIdx = FindMatchingBlob(palettePool, paletteBytes);
                if (palIdx < 0) { palIdx = palettePool.Count; palettePool.Add(paletteBytes); }

                frameEntries.Add(new FrameEntry
                {
                    Wait = wait,
                    Sound = sound,
                    SheetIndex = sheetIdx,
                    PaletteIndex = palIdx,
                });
            }

            if (frameEntries.Count == 0)
                return "No frames found in script.";

            // Place sheets + palettes in free ROM space (Plan v3 C3:
            // FindAndWriteData not AppendToRomEnd).
            var sheetAddrs = new List<uint>();
            foreach (var blob in sheetPool)
            {
                uint addr = ImageImportCore.FindAndWriteData(rom, blob);
                if (addr == U.NOT_FOUND)
                    return $"No free space for {blob.Length}-byte tile sheet.";
                sheetAddrs.Add(addr);
            }

            var palAddrs = new List<uint>();
            foreach (var blob in palettePool)
            {
                uint addr = ImageImportCore.FindAndWriteData(rom, blob);
                if (addr == U.NOT_FOUND)
                    return $"No free space for {blob.Length}-byte palette.";
                palAddrs.Add(addr);
            }

            // Serialize the frame table: 12 bytes per frame + 12 zero terminator + 4 extra
            // zero pad (matches WinForms `ImageUtilMapActionAnimation.Import` exactly).
            byte[] frameTable = new byte[frameEntries.Count * FRAME_ENTRY_SIZE + 16];
            for (int i = 0; i < frameEntries.Count; i++)
            {
                var fe = frameEntries[i];
                int off = i * FRAME_ENTRY_SIZE;
                frameTable[off + 0] = (byte)(fe.Wait & 0xFF);
                frameTable[off + 1] = 0;
                frameTable[off + 2] = (byte)(fe.Sound & 0xFF);
                frameTable[off + 3] = (byte)((fe.Sound >> 8) & 0xFF);
                uint imgPtr = U.toPointer(sheetAddrs[fe.SheetIndex]);
                uint palPtr = U.toPointer(palAddrs[fe.PaletteIndex]);
                WriteU32(frameTable, off + 4, imgPtr);
                WriteU32(frameTable, off + 8, palPtr);
            }
            // Trailing 16 bytes already zero from `new`.

            uint frameAddr = ImageImportCore.FindAndWriteData(rom, frameTable);
            if (frameAddr == U.NOT_FOUND)
                return $"No free space for {frameTable.Length}-byte frame table.";

            // Update the pointer slot to point at the new frame table.
            rom.write_p32(pointerAddr, frameAddr);

            // Persist the name in CommentCache so the editor's comment box
            // can rehydrate it on next load (mirrors WF MakeImportDataName).
            if (!string.IsNullOrEmpty(scriptName) && CoreState.CommentCache != null)
            {
                try { CoreState.CommentCache.Update(pointerAddr, scriptName); }
                catch { /* non-fatal — cache is best effort */ }
            }

            return string.Empty;
        }

        /// <summary>
        /// Read a 16-color GBA palette (or any color count) from a SPECIFIC
        /// ROM, not <c>CoreState.ROM</c>. Mirrors
        /// <c>ImageUtilCore.GetPalette</c> but parameterizes the ROM so the
        /// export/import path stays self-consistent across headless tooling,
        /// multi-ROM flows, and tests that don't touch
        /// <c>CoreState.ROM</c> — Copilot bot review on PR #620 round 2.
        /// </summary>
        public static byte[] ReadPaletteFromRom(ROM rom, uint offset, int colorCount)
        {
            if (rom == null || rom.Data == null) return null;
            int byteLen = colorCount * 2;
            if (offset + (uint)byteLen > (uint)rom.Data.Length) return null;
            byte[] palette = new byte[byteLen];
            Array.Copy(rom.Data, offset, palette, 0, byteLen);
            return palette;
        }

        /// <summary>
        /// Normalize a GBA palette buffer to exactly <see cref="PALETTE_SIZE_BYTES"/>
        /// bytes (16 colors × 2 bytes). Plan v3 B2 — guarantees the on-ROM
        /// palette is the size every downstream consumer expects, even when
        /// <see cref="DecreaseColorCore.Quantize"/> returns a shorter buffer.
        /// </summary>
        public static byte[] PadGBAPaletteTo16(byte[] input)
        {
            byte[] result = new byte[PALETTE_SIZE_BYTES];
            if (input == null) return result;
            int copyLen = Math.Min(input.Length, PALETTE_SIZE_BYTES);
            Array.Copy(input, result, copyLen);
            return result;
        }

        // ----------------------------------------------------------------
        // Internal helpers
        // ----------------------------------------------------------------

        struct FrameEntry
        {
            public uint Wait;
            public uint Sound;
            public int SheetIndex;
            public int PaletteIndex;
        }

        static string MakePngFilename(string baseName, uint id)
        {
            string safeBase = (baseName ?? "anim").Replace(" ", "_");
            return safeBase + "_g" + id.ToString("000", CultureInfo.InvariantCulture) + ".png";
        }

        static int FindMatchingBlob(List<byte[]> pool, byte[] candidate)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (BlobsEqual(pool[i], candidate)) return i;
            }
            return -1;
        }

        static bool BlobsEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        static string BytesToHex(byte[] data)
        {
            if (data == null) return "";
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data) sb.AppendFormat("{0:X2}", b);
            return sb.ToString();
        }

        static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
            // Strip whitespace.
            var sb = new StringBuilder(hex.Length);
            foreach (char c in hex)
            {
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
            string clean = sb.ToString();
            if (clean.Length % 2 != 0) return null;
            byte[] result = new byte[clean.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                if (!byte.TryParse(clean.Substring(i * 2, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out byte b))
                    return null;
                result[i] = b;
            }
            return result;
        }

        static byte[] CropOrPadRgba(byte[] srcRgba, int srcW, int srcH, int dstW, int dstH)
        {
            byte[] dst = new byte[dstW * dstH * 4];
            int copyW = Math.Min(srcW, dstW);
            int copyH = Math.Min(srcH, dstH);
            // Top-left aligned crop/pad (matches WinForms `ImageUtil.Copy(bmp, 0, 0, w, h)`).
            for (int y = 0; y < copyH; y++)
            {
                int srcOff = y * srcW * 4;
                int dstOff = y * dstW * 4;
                Array.Copy(srcRgba, srcOff, dst, dstOff, copyW * 4);
            }
            return dst;
        }

        static void WriteU32(byte[] dst, int offset, uint value)
        {
            dst[offset + 0] = (byte)(value & 0xFF);
            dst[offset + 1] = (byte)((value >> 8) & 0xFF);
            dst[offset + 2] = (byte)((value >> 16) & 0xFF);
            dst[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
