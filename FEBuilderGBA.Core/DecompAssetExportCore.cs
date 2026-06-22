using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Status codes for decomp asset export operations.
    /// </summary>
    public enum DecompAssetStatus
    {
        /// <summary>Export succeeded.</summary>
        Ok,
        /// <summary>Required arguments are missing or invalid.</summary>
        BadArgs,
        /// <summary>Output path was rejected (outside project root, invalid, etc.).</summary>
        PathRejected,
        /// <summary>ROM data at the given address is missing, out-of-bounds, or cannot be decoded.</summary>
        NotData,
        /// <summary>An unexpected exception occurred during export.</summary>
        Faulted,
    }

    /// <summary>
    /// Typed result returned by <see cref="DecompAssetExportCore"/> methods.
    /// </summary>
    public sealed class DecompAssetResult
    {
        /// <summary>Outcome status code.</summary>
        public DecompAssetStatus Status;
        /// <summary>Human-readable message (error description or success summary).</summary>
        public string Message = "";
        /// <summary>Absolute paths of every file written by the export.</summary>
        public List<string> WrittenPaths = new List<string>();
        /// <summary>True when <see cref="Status"/> is <see cref="DecompAssetStatus.Ok"/>.</summary>
        public bool Ok => Status == DecompAssetStatus.Ok;
    }

    /// <summary>
    /// Orchestrates decomp-pipeline asset export from FEBuilderGBA ROMs to source-tree paths.
    ///
    /// This class is READ-ONLY (no ROM mutation) and NEVER throws: every public method is
    /// fully guarded by a try/catch and returns a typed <see cref="DecompAssetResult"/> on
    /// any fault. Classic ROM-mode import/export is UNCHANGED.
    ///
    /// Supported export kinds (#1133):
    /// <list type="bullet">
    ///   <item><description><c>ExportPalette</c> — JASC .pal file (faithful, lossless round-trip)</description></item>
    ///   <item><description><c>ExportGraphics</c> — indexed PNG (color type 3) + sidecar .pal (faithful)</description></item>
    ///   <item><description><c>ExportMap</c> — .mar tilemap + sidecar JSON (faithful)</description></item>
    ///   <item><description><c>ExportText</c> — texts.txt + textdefs.txt (migration format, not lossless macro round-trip)</description></item>
    /// </list>
    ///
    /// Asset types that require existing exporters (not reimplemented here):
    /// MIDI → <c>--export-midi</c>; portraits → <c>--export-portrait-all</c>;
    /// battle animations → <c>--export-battle-anime</c>.
    /// </summary>
    public static class DecompAssetExportCore
    {
        // ---- Path resolution ----

        /// <summary>
        /// Resolve an output path for a source-tree asset.
        ///
        /// <para>When <paramref name="project"/> is non-null, the path is resolved relative to
        /// <c>project.ProjectRoot</c> with containment enforcement: absolute paths and
        /// <c>..</c>-escaping relative paths are rejected (returns null). This ensures
        /// exported files can never be written outside the project source tree.</para>
        ///
        /// <para>When <paramref name="project"/> is null (classic --rom override mode), the
        /// path is accepted as-is: absolute paths are used directly, relative paths are
        /// expanded against the current working directory via <see cref="Path.GetFullPath"/>.
        /// This matches the behaviour of all other CLI exporters (e.g. --export-palette)
        /// which accept any writable path the user supplies.</para>
        ///
        /// <para>Returns null on any fault (never throws).</para>
        /// </summary>
        public static string ResolveSourcePath(DecompProject project, string relOut)
        {
            try
            {
                if (string.IsNullOrEmpty(relOut))
                    return null;

                if (project != null)
                {
                    // Project-root containment: use the internal DecompProjectDetector helper.
                    // ResolveArtifact rejects absolute paths and ..‑escaping.
                    return DecompProjectDetector.ResolveArtifact(project.ProjectRoot, relOut);
                }
                else
                {
                    // No project: expand to absolute path using cwd (no containment check).
                    return Path.GetFullPath(relOut);
                }
            }
            catch
            {
                return null;
            }
        }

        // ---- ExportPalette ----

        /// <summary>
        /// Export a GBA palette at <paramref name="addrOffset"/> (ROM byte offset, not GBA pointer)
        /// to a JASC .pal file at <paramref name="absOutPath"/>.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="addrOffset">ROM byte offset of the raw palette data.</param>
        /// <param name="colors">Number of colors to export (each 2 bytes BGR555 LE).</param>
        /// <param name="absOutPath">Absolute path for the output .pal file.</param>
        public static DecompAssetResult ExportPalette(ROM rom, uint addrOffset, int colors, string absOutPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (colors <= 0 || colors > 256)
                    return Fail(DecompAssetStatus.BadArgs, $"Color count must be 1-256, got {colors}");
                if (string.IsNullOrEmpty(absOutPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output path is null or empty");

                int byteCount = colors * 2;
                if (rom.Data == null || addrOffset + byteCount > (uint)rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Address 0x{addrOffset:X} + {byteCount} bytes exceeds ROM size ({rom.Data?.Length ?? 0})");

                byte[] raw = new byte[byteCount];
                Array.Copy(rom.Data, addrOffset, raw, 0, byteCount);

                byte[] palBytes = PaletteFormatConverter.ExportToFormat(raw, PaletteFormat.JascPal);

                EnsureParentDir(absOutPath);
                File.WriteAllBytes(absOutPath, palBytes);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = "Palette exported" };
                result.WrittenPaths.Add(absOutPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        // ---- ExportGraphics ----

        /// <summary>
        /// Export GBA tile graphics to an indexed PNG (color type 3) + sidecar JASC .pal.
        ///
        /// The PNG uses color type 3 so tools like gbagfx can recover the original palette indices.
        /// A sidecar .pal (JASC format) is written alongside the PNG for palette round-trips.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="addrOffset">ROM byte offset of the tile graphics data.</param>
        /// <param name="width">Image width in pixels (must be a multiple of 8).</param>
        /// <param name="height">Image height in pixels (must be a multiple of 8).</param>
        /// <param name="bpp">Bits per pixel (4 or 8).</param>
        /// <param name="compressed">True if the data is LZ77-compressed at addrOffset.</param>
        /// <param name="paletteAddrOffset">ROM byte offset of the palette. Must be provided explicitly.</param>
        /// <param name="paletteColors">Number of colors in the palette (1-256).</param>
        /// <param name="absOutPngPath">Absolute path for the output .png file.
        /// A sidecar .pal is written at the same path with the .pal extension appended/substituted.</param>
        public static DecompAssetResult ExportGraphics(
            ROM rom,
            uint addrOffset,
            int width,
            int height,
            int bpp,
            bool compressed,
            uint paletteAddrOffset,
            int paletteColors,
            string absOutPngPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (width <= 0 || height <= 0)
                    return Fail(DecompAssetStatus.BadArgs, $"Invalid dimensions {width}x{height}");
                if (width % 8 != 0 || height % 8 != 0)
                    return Fail(DecompAssetStatus.BadArgs, $"Graphics dimensions must be multiples of 8 (tile-aligned), got {width}x{height}");
                if (bpp != 4 && bpp != 8)
                    return Fail(DecompAssetStatus.BadArgs, $"bpp must be 4 or 8, got {bpp}");
                if (paletteColors <= 0 || paletteColors > 256)
                    return Fail(DecompAssetStatus.BadArgs, $"paletteColors must be 1-256, got {paletteColors}");
                if (string.IsNullOrEmpty(absOutPngPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output PNG path is null or empty");

                if (CoreState.ImageService == null)
                    return Fail(DecompAssetStatus.Faulted, "CoreState.ImageService is not set; cannot decode tiles");

                // ---- Get tile bytes ----
                byte[] tileBytes;
                if (compressed)
                {
                    if (rom.Data == null || addrOffset >= (uint)rom.Data.Length)
                        return Fail(DecompAssetStatus.NotData, $"Address 0x{addrOffset:X} is outside ROM");
                    tileBytes = LZ77.decompress(rom.Data, addrOffset);
                    if (tileBytes == null || tileBytes.Length == 0)
                        return Fail(DecompAssetStatus.NotData, $"LZ77 decompression failed at 0x{addrOffset:X}");
                }
                else
                {
                    int tileByteCount = width * height * bpp / 8;
                    if (tileByteCount <= 0)
                        return Fail(DecompAssetStatus.BadArgs, "Tile byte count is zero");
                    if (rom.Data == null || addrOffset + (uint)tileByteCount > (uint)rom.Data.Length)
                        return Fail(DecompAssetStatus.NotData,
                            $"Address 0x{addrOffset:X} + {tileByteCount} bytes exceeds ROM size");
                    tileBytes = new byte[tileByteCount];
                    Array.Copy(rom.Data, addrOffset, tileBytes, 0, tileByteCount);
                }

                // ---- Validate tile data is large enough for the requested dimensions ----
                // The uncompressed branch already sizes tileBytes exactly; this guard
                // catches the compressed case where LZ77 decompresses to too few bytes,
                // which would otherwise leave the trailing tiles as index 0 (silent corruption).
                int expectedTileBytes = width * height * bpp / 8;
                if (tileBytes == null || tileBytes.Length < expectedTileBytes)
                    return Fail(DecompAssetStatus.NotData, $"Decoded tile data ({tileBytes?.Length ?? 0} bytes) is smaller than required {expectedTileBytes} bytes for {width}x{height} {bpp}bpp");

                // ---- Read palette ----
                int palByteCount = paletteColors * 2;
                if (rom.Data == null || paletteAddrOffset + (uint)palByteCount > (uint)rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Palette address 0x{paletteAddrOffset:X} + {palByteCount} bytes exceeds ROM size");
                byte[] gbaPalette = new byte[palByteCount];
                Array.Copy(rom.Data, paletteAddrOffset, gbaPalette, 0, palByteCount);

                // ---- Decode tiles ----
                IImage img;
                if (bpp == 4)
                    img = CoreState.ImageService.Decode4bppTiles(tileBytes, 0, width, height, gbaPalette);
                else
                    img = CoreState.ImageService.Decode8bppTiles(tileBytes, 0, width, height, gbaPalette);

                if (img == null)
                    return Fail(DecompAssetStatus.Faulted, "Tile decode returned null");

                byte[] indices = null;
                byte[] outPalette = null;
                try
                {
                    indices = img.GetPixelData();
                    outPalette = img.GetPaletteGBA();
                    if (outPalette == null || outPalette.Length == 0)
                        outPalette = gbaPalette; // fall back to raw palette
                }
                finally
                {
                    img.Dispose();
                }

                if (indices == null || indices.Length == 0)
                    return Fail(DecompAssetStatus.Faulted, "GetPixelData returned empty array");

                // ---- Write indexed PNG ----
                byte[] pngBytes = IndexedPngWriter.Write(indices, width, height, outPalette, paletteColors, transparentIndex: 0);
                if (pngBytes == null)
                    return Fail(DecompAssetStatus.Faulted, "IndexedPngWriter returned null");

                EnsureParentDir(absOutPngPath);
                File.WriteAllBytes(absOutPngPath, pngBytes);

                // ---- Write sidecar .pal ----
                string palPath = Path.ChangeExtension(absOutPngPath, ".pal");
                byte[] sidecarPal = PaletteFormatConverter.ExportToFormat(outPalette, PaletteFormat.JascPal);
                File.WriteAllBytes(palPath, sidecarPal);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = "Graphics exported" };
                result.WrittenPaths.Add(absOutPngPath);
                result.WrittenPaths.Add(palPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        // ---- ExportMap ----

        /// <summary>
        /// Export a GBA tilemap (LZ77-compressed) to a .mar file and a sidecar JSON.
        ///
        /// The .mar format stores a flat array of u16 values where each entry is the
        /// raw tilemap u16 shifted left by 3 (WF SaveAsMAR parity). The sidecar JSON
        /// records the original width/height and source address for reconstruction.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="addrOffset">ROM byte offset of the LZ77-compressed tilemap data.</param>
        /// <param name="absOutMarPath">Absolute path for the output .mar file.
        /// A sidecar .mar.json is written at the same path with .json appended.</param>
        public static DecompAssetResult ExportMap(ROM rom, uint addrOffset, string absOutMarPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutMarPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output .mar path is null or empty");
                if (rom.Data == null || addrOffset >= (uint)rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData, $"Address 0x{addrOffset:X} is outside ROM");

                byte[] blob = LZ77.decompress(rom.Data, addrOffset);
                if (blob == null || blob.Length < 2)
                    return Fail(DecompAssetStatus.NotData, $"LZ77 decompression failed at 0x{addrOffset:X}");

                // Blob layout: [w:u8][h:u8][u16 raw tilemap entries...]
                int w = blob[0];
                int h = blob[1];
                if (w <= 0 || h <= 0)
                    return Fail(DecompAssetStatus.NotData, $"Invalid map dimensions {w}x{h} at 0x{addrOffset:X}");

                int expectedSize = 2 + w * h * 2;
                if (blob.Length < expectedSize)
                    return Fail(DecompAssetStatus.NotData,
                        $"Decompressed data ({blob.Length} bytes) too small for {w}x{h} map (need {expectedSize})");

                // Build .mar body: for each u16 tile entry, write (rawU16 << 3) as LE u16
                byte[] marBody = new byte[w * h * 2];
                for (int i = 0; i < w * h; i++)
                {
                    int srcOffset = 2 + i * 2;
                    ushort rawTile = (ushort)(blob[srcOffset] | (blob[srcOffset + 1] << 8));
                    ushort marTile = (ushort)(rawTile << 3);
                    marBody[i * 2 + 0] = (byte)(marTile & 0xFF);
                    marBody[i * 2 + 1] = (byte)(marTile >> 8);
                }

                EnsureParentDir(absOutMarPath);
                File.WriteAllBytes(absOutMarPath, marBody);

                // Write sidecar JSON
                string jsonPath = absOutMarPath + ".json";
                string json = BuildMapJson(w, h, addrOffset);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = "Map exported" };
                result.WrittenPaths.Add(absOutMarPath);
                result.WrittenPaths.Add(jsonPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        // ---- ImportMap ----

        /// <summary>
        /// Re-import a <c>.mar</c> tilemap LAYOUT (the inverse of <see cref="ExportMap"/>) into a
        /// RAW UNCOMPRESSED tilemap blob written ONLY into the source tree — this is the IMPORT/verify
        /// direction that makes the <c>.mar</c> a genuine source-backed round-trip artifact (#1148).
        ///
        /// <para>This method NEVER mutates <see cref="CoreState.ROM"/> and NEVER throws (every fault
        /// becomes a typed <see cref="DecompAssetResult"/>). It does NOT LZ77-compress and does NOT
        /// write any compressed binary: FEBuilder's LZ77 packer is non-canonical (its match/padding
        /// choices differ from the original ROM packer), so a compressed round-trip would NOT be
        /// byte-identical. The decomp build re-compresses the tilemap from source itself; what this
        /// writes is the raw uncompressed blob the build consumes.</para>
        ///
        /// <para>Output blob layout (the inverse of the <see cref="ExportMap"/> decompressed input):
        /// <c>[w:u8][h:u8]</c> then <c>w*h</c> raw u16 LE tilemap entries. For each <c>.mar</c> body
        /// entry <c>marU16</c>, the raw tile is reconstructed as <c>rawTile = marU16 &gt;&gt; 3</c>
        /// (the exact inverse of the export-side <c>rawTile &lt;&lt; 3</c>). Because the export-side
        /// shift truncates bits 13-15, the layout is LOSSLESS for tile indices &lt; 0x2000 — every
        /// valid <c>.mar</c> entry already has its low 3 bits clear (the validator enforces this),
        /// so the reconstruction is exact for the understood u16 layout body.</para>
        /// </summary>
        /// <param name="absInMarPath">Absolute path to the input <c>.mar</c> file. A sidecar
        /// <c>&lt;path&gt;.mar.json</c> (the export-side JSON) is REQUIRED — its width/height are
        /// needed to size the reconstructed blob.</param>
        /// <param name="absOutBlobPath">Absolute path for the output RAW uncompressed tilemap blob.</param>
        public static DecompAssetResult ImportMap(string absInMarPath, string absOutBlobPath)
        {
            try
            {
                if (string.IsNullOrEmpty(absInMarPath))
                    return Fail(DecompAssetStatus.BadArgs, "Input .mar path is null or empty");
                if (string.IsNullOrEmpty(absOutBlobPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output blob path is null or empty");

                // Structural + index-level validation (even length, length==w*h*2 vs sidecar,
                // and the <<3 low-3-bits-clear invariant). Reuse the shared validator so the
                // import refuses exactly the same malformed inputs the validator flags.
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapLayout, absInMarPath);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                byte[] body = File.ReadAllBytes(absInMarPath);

                // The sidecar dims are REQUIRED to reconstruct the [w][h] header — the validator
                // only WARNS (not errors) on a missing/unreadable sidecar, so re-check here.
                string sidecar = absInMarPath + ".json";
                if (!File.Exists(sidecar))
                    return Fail(DecompAssetStatus.NotData, "sidecar .mar.json required to reconstruct dimensions");
                if (!TryReadSidecarDims(sidecar, out int width, out int height))
                    return Fail(DecompAssetStatus.NotData, "sidecar .mar.json required to reconstruct dimensions");

                // The header stores width/height as u8 — they must fit 1..255.
                if (width < 1 || width > 255 || height < 1 || height > 255)
                    return Fail(DecompAssetStatus.NotData, "dimensions out of u8 range 1..255");

                // Double-check the body length matches w*h*2 (the validator already errors on
                // this, but guard explicitly so the reconstruction loop cannot read OOB).
                int entries = width * height;
                if (body.Length != entries * 2)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != width*height*2 ({width}*{height}*2 = {entries * 2})");

                // Reconstruct the raw uncompressed blob: [w:u8][h:u8] + w*h raw u16 LE.
                byte[] raw = new byte[2 + entries * 2];
                raw[0] = (byte)width;
                raw[1] = (byte)height;
                for (int i = 0; i < entries; i++)
                {
                    ushort marU16 = (ushort)(body[i * 2] | (body[i * 2 + 1] << 8));
                    ushort rawTile = (ushort)(marU16 >> 3);
                    raw[2 + i * 2 + 0] = (byte)(rawTile & 0xFF);
                    raw[2 + i * 2 + 1] = (byte)(rawTile >> 8);
                }

                EnsureParentDir(absOutBlobPath);
                File.WriteAllBytes(absOutBlobPath, raw);

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Imported {width}x{height} map layout to raw uncompressed tilemap blob ({raw.Length} bytes)"
                };
                result.WrittenPaths.Add(absOutBlobPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// PURE round-trip proof for a <c>.mar</c> BODY (no ROM, no compression, never throws).
        ///
        /// <para>For each u16 entry it computes <c>rawTile = marU16 &gt;&gt; 3</c> then re-exports
        /// <c>reExported = (ushort)(rawTile &lt;&lt; 3)</c> and asserts <c>reExported == marU16</c>.
        /// Returns true iff <paramref name="marBody"/> is non-null, has even length, and EVERY entry
        /// survives the export→import→export shift unchanged — i.e. every entry's low 3 bits are
        /// clear, which is exactly the lossless boundary (tile indices &lt; 0x2000). Returns false on
        /// a null/odd-length body or any entry that does not round-trip.</para>
        /// </summary>
        public static bool RoundTripMarBody(byte[] marBody)
        {
            try
            {
                if (marBody == null) return false;
                if (marBody.Length % 2 != 0) return false;

                int entries = marBody.Length / 2;
                for (int i = 0; i < entries; i++)
                {
                    ushort marU16 = (ushort)(marBody[i * 2] | (marBody[i * 2 + 1] << 8));
                    ushort rawTile = (ushort)(marU16 >> 3);
                    ushort reExported = (ushort)(rawTile << 3);
                    if (reExported != marU16)
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Read width/height from a <c>.mar.json</c> sidecar (the export-side JSON). NEVER throws;
        /// returns false on any fault or non-positive dim. Mirrors the validator's private reader
        /// (which is not accessible from here).
        /// </summary>
        static bool TryReadSidecarDims(string sidecar, out int width, out int height)
        {
            width = 0; height = 0;
            try
            {
                string json = File.ReadAllText(sidecar);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                if (root.TryGetProperty("width", out System.Text.Json.JsonElement w)
                    && w.ValueKind == System.Text.Json.JsonValueKind.Number)
                    width = w.GetInt32();
                if (root.TryGetProperty("height", out System.Text.Json.JsonElement h)
                    && h.ValueKind == System.Text.Json.JsonValueKind.Number)
                    height = h.GetInt32();
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }

        // ---- ExportText ----

        /// <summary>
        /// Export all ROM text entries to a migration-format text dump.
        ///
        /// Writes two files to <paramref name="absOutDir"/>:
        /// <list type="bullet">
        ///   <item><description><c>texts.txt</c> — each entry as <c># msg 0xNNNN</c> / text / blank line</description></item>
        ///   <item><description><c>textdefs.txt</c> — C-header-style <c>#define MSG_0xNNNN N</c> lines</description></item>
        /// </list>
        ///
        /// NOTE: This is a migration aid, not a lossless decomp macro round-trip.
        /// Escape codes are preserved as-is from <see cref="TranslateCore.DumpTexts"/>.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="absOutDir">Absolute path to the output directory (created if absent).</param>
        public static DecompAssetResult ExportText(ROM rom, string absOutDir)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutDir))
                    return Fail(DecompAssetStatus.BadArgs, "Output directory path is null or empty");

                var entries = TranslateCore.DumpTexts(rom);
                if (entries == null)
                    entries = new List<(uint, string)>();

                var (textsTxt, textdefsTxt) = FormatTexts(entries);

                Directory.CreateDirectory(absOutDir);
                string textsPath = Path.Combine(absOutDir, "texts.txt");
                string textdefsPath = Path.Combine(absOutDir, "textdefs.txt");
                File.WriteAllText(textsPath, textsTxt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.WriteAllText(textdefsPath, textdefsTxt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = $"Exported {entries.Count} text entries" };
                result.WrittenPaths.Add(textsPath);
                result.WrittenPaths.Add(textdefsPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        // ---- Internal formatters (testable without ROM) ----

        /// <summary>
        /// Format text entries into the two output file bodies.
        /// Extracted so unit tests can exercise the format without needing a ROM.
        /// </summary>
        /// <returns>(textsTxt content, textdefsTxt content)</returns>
        internal static (string textsTxt, string textdefsTxt) FormatTexts(List<(uint textId, string text)> entries)
        {
            var texts = new StringBuilder();
            var defs = new StringBuilder();

            foreach (var (id, text) in entries)
            {
                // texts.txt block: header comment, text body, blank separator
                texts.AppendLine($"# msg 0x{id:X4}");
                texts.AppendLine(text ?? "");
                texts.AppendLine();

                // textdefs.txt: #define MSG_0xNNNN N
                defs.AppendLine($"#define MSG_0x{id:X4} {id}");
            }

            return (texts.ToString(), defs.ToString());
        }

        // ---- Private helpers ----

        static DecompAssetResult Fail(DecompAssetStatus status, string message)
            => new DecompAssetResult { Status = status, Message = message ?? "" };

        static void EnsureParentDir(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        static string BuildMapJson(int w, int h, uint addrOffset)
        {
            // Hand-built to avoid any dependency on System.Text.Json serializer options
            return $"{{\n  \"width\": {w},\n  \"height\": {h},\n  \"srcAddr\": \"0x{addrOffset:X}\",\n  \"format\": \"febuilder-mar-u16-shl3\"\n}}\n";
        }
    }
}
