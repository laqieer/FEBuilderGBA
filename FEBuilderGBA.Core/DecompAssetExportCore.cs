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
        ///
        /// The <c>&lt;&lt;3</c> encoding only fits a u16 when the WHOLE raw tilemap entry is
        /// &lt; 0x2000 (its palette/flag bits 13-15 are clear); a larger raw entry is REJECTED
        /// (<see cref="DecompAssetStatus.NotData"/>) rather than silently truncated, so the
        /// exported .mar is guaranteed to round-trip through <see cref="ImportMap"/>.
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

                // Build .mar body: for each raw tilemap u16 entry, write (rawU16 << 3) as LE u16.
                // The <<3 only fits in a u16 when the WHOLE raw entry is < 0x2000 (i.e. bits 13-15 —
                // the palette/flag bits — are clear); a larger value would have those top 3 bits
                // SILENTLY truncated by the (ushort) cast, so the .mar would not round-trip back to
                // the original tilemap. Reject up front rather than emit a lossy .mar (Copilot #1148
                // review finding — keeps the round-trip claim honest).
                byte[] marBody = new byte[w * h * 2];
                for (int i = 0; i < w * h; i++)
                {
                    int srcOffset = 2 + i * 2;
                    ushort rawTile = (ushort)(blob[srcOffset] | (blob[srcOffset + 1] << 8));
                    if (rawTile >= 0x2000)
                        return Fail(DecompAssetStatus.NotData,
                            $"Raw tilemap entry #{i} (0x{rawTile:X4}) >= 0x2000 — its top 3 bits (palette/flag bits 13-15) would be truncated by the <<3 .mar encoding and is not round-trippable; this tilemap is not a supported .mar layout.");
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
        /// RAW UNCOMPRESSED tilemap blob — the IMPORT/verify direction that makes the <c>.mar</c> a
        /// genuine source-backed round-trip artifact (#1148). The blob is written to
        /// <paramref name="absOutBlobPath"/> verbatim; this method does NOT itself enforce
        /// project-root containment — callers that need it (e.g. the CLI) must pre-resolve the path
        /// through <see cref="ResolveSourcePath"/> so the write lands inside the project tree.
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
        /// shift truncates bits 13-15 (the palette/flag bits), the layout is LOSSLESS for raw u16
        /// entries &lt; 0x2000 — every valid <c>.mar</c> entry already has its low 3 bits clear (the
        /// validator enforces this), so the reconstruction is exact for the understood u16 layout body.</para>
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
        /// clear, which is exactly the lossless boundary (raw tilemap u16 entries &lt; 0x2000, i.e.
        /// palette/flag bits 13-15 clear). Returns false on a null/odd-length body or any entry that
        /// does not round-trip.</para>
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

        // ---- Map-change OVERLAY (raw uncompressed u16 LE, #1355) ----

        /// <summary>
        /// Export a Map-Change OVERLAY tile DATA BLOCK (#1355) — a RAW UNCOMPRESSED <c>u16</c> LE
        /// array of <c>width*height</c> config-descriptor indices — to a <c>.change</c> file plus a
        /// sidecar JSON. This is NOT the <c>.mar</c> tile layout (no <c>&lt;&lt;3</c> shift, no LZ77)
        /// and NOT the 12-byte change-RECORD chain (terminator/flagID/PLIST metadata): it is ONLY the
        /// overlay data block that the change pointer (record <c>+8</c>) points at.
        ///
        /// <para>The body is copied BYTE-FOR-BYTE from the (already-uncompressed) ROM region at
        /// <paramref name="changeAddr"/>; <c>srcAddr</c> in the sidecar is provenance metadata ONLY
        /// (no symbol/owner is fabricated). This method is READ-ONLY (never mutates the ROM) and
        /// NEVER throws.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="changeAddr">ROM byte offset of the raw overlay data block (record <c>+8</c>
        /// change pointer, dereferenced).</param>
        /// <param name="width">Overlay width (record <c>+3</c>), 1..255.</param>
        /// <param name="height">Overlay height (record <c>+4</c>), 1..255.</param>
        /// <param name="absOutChangePath">Absolute path for the output <c>.change</c> file. A sidecar
        /// <c>&lt;path&gt;.change.json</c> is written at the same path with <c>.json</c> appended.</param>
        public static DecompAssetResult ExportMapChange(ROM rom, uint changeAddr, int width, int height, string absOutChangePath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutChangePath))
                    return Fail(DecompAssetStatus.BadArgs, "Output .change path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // Dimensions must fit the u8 record fields (1..255).
                if (width < 1 || width > 255 || height < 1 || height > 255)
                    return Fail(DecompAssetStatus.NotData, $"Invalid map-change overlay dimensions {width}x{height} (must be 1..255)");

                long bodyLen = (long)width * height * 2;
                // Bounds: the start must be a safe offset AND the whole body (last byte inclusive)
                // must lie inside the ROM. Use a long for the end so a huge w*h cannot wrap a uint.
                if (!U.isSafetyOffset(changeAddr, rom)
                    || changeAddr + bodyLen > rom.Data.Length
                    || changeAddr + bodyLen - 1 >= rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Overlay region [0x{changeAddr:X}, 0x{changeAddr + bodyLen:X}) is outside ROM (size 0x{rom.Data.Length:X})");

                // Copy the raw overlay body byte-for-byte from the (already-uncompressed) ROM.
                byte[] body = rom.getBinaryData(changeAddr, (int)bodyLen);
                if (body == null || body.Length != bodyLen)
                    return Fail(DecompAssetStatus.NotData, $"Could not read {bodyLen}-byte overlay body at 0x{changeAddr:X}");

                EnsureParentDir(absOutChangePath);
                File.WriteAllBytes(absOutChangePath, body);

                string jsonPath = absOutChangePath + ".json";
                string json = BuildMapChangeJson(width, height, changeAddr);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = $"Map-change overlay exported ({width}x{height})" };
                result.WrittenPaths.Add(absOutChangePath);
                result.WrittenPaths.Add(jsonPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Re-import a <c>.change</c> map-change OVERLAY (the inverse of <see cref="ExportMapChange"/>)
        /// into a RAW UNCOMPRESSED overlay blob (#1355). The overlay is an IDENTITY copy — there is NO
        /// <c>[w][h]</c> header prepend, NO <c>&gt;&gt;3</c> shift, NO LZ77 compression — so the output
        /// blob is the validated body written VERBATIM.
        ///
        /// <para>This method NEVER reads <see cref="CoreState.ROM"/>, NEVER LZ77-compresses, NEVER
        /// mutates the ROM, and NEVER throws. The REQUIRED sidecar dims (validated against the body
        /// length) make this a genuine source-backed artifact.</para>
        /// </summary>
        /// <param name="absInChangePath">Absolute path to the input <c>.change</c> file. A sidecar
        /// <c>&lt;path&gt;.change.json</c> (the export-side JSON) is REQUIRED.</param>
        /// <param name="absOutBlobPath">Absolute path for the output RAW overlay blob (identity copy
        /// of the validated body).</param>
        public static DecompAssetResult ImportMapChange(string absInChangePath, string absOutBlobPath)
        {
            try
            {
                if (string.IsNullOrEmpty(absInChangePath))
                    return Fail(DecompAssetStatus.BadArgs, "Input .change path is null or empty");
                if (string.IsNullOrEmpty(absOutBlobPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output blob path is null or empty");

                // Structural validation (required sidecar, format, dims, even length, w*h*2).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, absInChangePath);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                byte[] body = File.ReadAllBytes(absInChangePath);

                // The sidecar dims are REQUIRED (the validator already errors when missing, but
                // re-read here to size-check the body before the identity copy).
                string sidecar = absInChangePath + ".json";
                if (!TryReadMapChangeDims(sidecar, out int width, out int height))
                    return Fail(DecompAssetStatus.NotData, "sidecar .change.json required to read dimensions");

                int expected = width * height * 2;
                if (body.Length != expected)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != width*height*2 ({width}*{height}*2 = {expected})");

                // The overlay is an identity copy: write the validated body VERBATIM.
                EnsureParentDir(absOutBlobPath);
                File.WriteAllBytes(absOutBlobPath, body);

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Imported {width}x{height} map-change overlay to raw blob ({body.Length} bytes)"
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
        /// PURE structural round-trip proof for a map-change OVERLAY BODY (#1355): true iff
        /// <paramref name="body"/> is non-null, has even length, the dims are positive, and
        /// <c>body.Length == width*height*2</c>. Try/catch → false.
        ///
        /// <para>This is source-level structure-exact IDENTITY, NOT a byte-pinned ROM round-trip.
        /// For a byte-exact ROM mismatch proof use <see cref="VerifyMapChangeAgainstRom"/>.</para>
        /// </summary>
        public static bool RoundTripMapChangeBody(byte[] body, int width, int height)
        {
            try
            {
                if (body == null) return false;
                if (body.Length % 2 != 0) return false;
                if (width < 1 || height < 1) return false;
                return body.Length == width * height * 2;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Byte-exact ROM-backed mismatch proof for a map-change OVERLAY (#1355): compare the
        /// raw ROM overlay region at <paramref name="changeAddr"/> against the <c>.change</c> file
        /// body byte-for-byte. This is the ONLY ROM-backed verification path (export/import never
        /// touch the ROM). READ-ONLY (never mutates the ROM), NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="changeAddr">ROM byte offset of the raw overlay data block.</param>
        /// <param name="width">Overlay width, 1..255.</param>
        /// <param name="height">Overlay height, 1..255.</param>
        /// <param name="absInChangePath">Absolute path to the <c>.change</c> file (with required sidecar).</param>
        public static DecompAssetResult VerifyMapChangeAgainstRom(ROM rom, uint changeAddr, int width, int height, string absInChangePath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absInChangePath))
                    return Fail(DecompAssetStatus.BadArgs, "Input .change path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // Validate the .change file first (required sidecar, format, dims, length).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChangeOverlay, absInChangePath);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                if (width < 1 || width > 255 || height < 1 || height > 255)
                    return Fail(DecompAssetStatus.NotData, $"Invalid map-change overlay dimensions {width}x{height} (must be 1..255)");

                long bodyLen = (long)width * height * 2;
                if (!U.isSafetyOffset(changeAddr, rom)
                    || changeAddr + bodyLen > rom.Data.Length
                    || changeAddr + bodyLen - 1 >= rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Overlay region [0x{changeAddr:X}, 0x{changeAddr + bodyLen:X}) is outside ROM (size 0x{rom.Data.Length:X})");

                byte[] body = File.ReadAllBytes(absInChangePath);
                if (body.Length != bodyLen)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != width*height*2 ({width}*{height}*2 = {bodyLen})");

                for (int i = 0; i < body.Length; i++)
                {
                    byte romByte = rom.Data[changeAddr + i];
                    byte fileByte = body[i];
                    if (romByte != fileByte)
                        return Fail(DecompAssetStatus.NotData,
                            $"Overlay mismatch at byte offset {i}: ROM=0x{romByte:X2} file=0x{fileByte:X2}");
                }

                return new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Verified {width}x{height} map-change overlay byte-identical to ROM"
                };
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Read width/height from a <c>.change.json</c> sidecar (the export-side JSON). NEVER throws;
        /// returns false on any fault or non-positive dim. Public so the CLI <c>--roundtrip-asset</c>
        /// path can size the overlay body before calling <see cref="RoundTripMapChangeBody"/>.
        /// </summary>
        public static bool TryReadMapChangeDims(string sidecarPath, out int width, out int height)
        {
            width = 0; height = 0;
            try
            {
                if (string.IsNullOrEmpty(sidecarPath) || !File.Exists(sidecarPath))
                    return false;
                string json = File.ReadAllText(sidecarPath);
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

        // ---- Map tile-animation-2 PALETTE block (raw uncompressed u16 LE, #1360) ----

        /// <summary>
        /// Export a map tile-animation-2 PALETTE DATA BLOCK (#1360) — a RAW UNCOMPRESSED <c>u16</c> LE
        /// array of <paramref name="count"/> 15-bit GBA colors (<c>count*2</c> bytes) — to a <c>.mapanime2pal</c>
        /// file plus a sidecar JSON. This is the structural TWIN of <see cref="ExportMapChange"/> (the
        /// map-change overlay path, #1355) with a single <c>count</c> descriptor in place of width/height.
        /// It is reached by each anime-2 entry's <c>+0</c> pointer (see
        /// <see cref="MapTileAnimation2Core.EntryRow.P0"/> / <c>Count</c> = u8 at entry <c>+5</c>) — NOT
        /// the anime-2 ENTRY/PLIST table, NOT a <c>&lt;&lt;3</c>-shifted <c>.mar</c> layout, NOT LZ77.
        ///
        /// <para>The body is copied BYTE-FOR-BYTE from the (already-uncompressed) ROM region at
        /// <paramref name="palAddr"/>; <c>srcAddr</c> in the sidecar is provenance metadata ONLY
        /// (no symbol/owner is fabricated). This method is READ-ONLY (never mutates the ROM) and
        /// NEVER throws.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="palAddr">ROM byte offset of the raw palette data block (anime-2 entry <c>+0</c>
        /// pointer, dereferenced).</param>
        /// <param name="count">Number of <c>u16</c> colors (anime-2 entry <c>+5</c>), 1..255.</param>
        /// <param name="absOutPath">Absolute path for the output <c>.mapanime2pal</c> file. A sidecar
        /// <c>&lt;path&gt;.json</c> is written at the same path with <c>.json</c> appended.</param>
        public static DecompAssetResult ExportMapAnime2Pal(ROM rom, uint palAddr, int count, string absOutPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output .mapanime2pal path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // Count must fit the u8 record field (1..255).
                if (count < 1 || count > 255)
                    return Fail(DecompAssetStatus.NotData, $"Invalid map tile-animation-2 palette count {count} (must be 1..255)");

                long bodyLen = (long)count * 2;
                // Bounds: the start must be a safe offset AND the whole body (last byte inclusive)
                // must lie inside the ROM. Use a long for the end so a huge count cannot wrap a uint.
                if (!U.isSafetyOffset(palAddr, rom)
                    || palAddr + bodyLen > rom.Data.Length
                    || palAddr + bodyLen - 1 >= rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Palette region [0x{palAddr:X}, 0x{palAddr + bodyLen:X}) is outside ROM (size 0x{rom.Data.Length:X})");

                // Copy the raw palette body byte-for-byte from the (already-uncompressed) ROM.
                byte[] body = rom.getBinaryData(palAddr, (int)bodyLen);
                if (body == null || body.Length != bodyLen)
                    return Fail(DecompAssetStatus.NotData, $"Could not read {bodyLen}-byte palette body at 0x{palAddr:X}");

                EnsureParentDir(absOutPath);
                File.WriteAllBytes(absOutPath, body);

                string jsonPath = absOutPath + ".json";
                string json = BuildMapAnime2PalJson(count, palAddr);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = $"Map tile-animation-2 palette exported ({count} colors)" };
                result.WrittenPaths.Add(absOutPath);
                result.WrittenPaths.Add(jsonPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Re-import a <c>.mapanime2pal</c> map tile-animation-2 PALETTE block (the inverse of
        /// <see cref="ExportMapAnime2Pal"/>, #1360) into a RAW UNCOMPRESSED palette blob. The block is an
        /// IDENTITY copy — NO header prepend, NO <c>&gt;&gt;3</c> shift, NO LZ77 compression — so the output
        /// blob is the validated body written VERBATIM.
        ///
        /// <para>This method NEVER reads <see cref="CoreState.ROM"/>, NEVER LZ77-compresses, NEVER
        /// mutates the ROM, and NEVER throws. The REQUIRED sidecar <c>count</c> (validated against the body
        /// length) makes this a genuine source-backed artifact.</para>
        /// </summary>
        /// <param name="absIn">Absolute path to the input <c>.mapanime2pal</c> file. A sidecar
        /// <c>&lt;path&gt;.json</c> (the export-side JSON) is REQUIRED.</param>
        /// <param name="absOutBlob">Absolute path for the output RAW palette blob (identity copy of the
        /// validated body).</param>
        public static DecompAssetResult ImportMapAnime2Pal(string absIn, string absOutBlob)
        {
            try
            {
                if (string.IsNullOrEmpty(absIn))
                    return Fail(DecompAssetStatus.BadArgs, "Input .mapanime2pal path is null or empty");
                if (string.IsNullOrEmpty(absOutBlob))
                    return Fail(DecompAssetStatus.BadArgs, "Output blob path is null or empty");

                // Structural validation (required sidecar, format, count, even length, count*2).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, absIn);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                byte[] body = File.ReadAllBytes(absIn);

                // The sidecar count is REQUIRED (the validator already errors when missing, but
                // re-read here to size-check the body before the identity copy).
                string sidecar = absIn + ".json";
                if (!TryReadMapAnime2PalCount(sidecar, out int count))
                    return Fail(DecompAssetStatus.NotData, "sidecar .json required to read count");

                int expected = count * 2;
                if (body.Length != expected)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != count*2 ({count}*2 = {expected})");

                // The block is an identity copy: write the validated body VERBATIM.
                EnsureParentDir(absOutBlob);
                File.WriteAllBytes(absOutBlob, body);

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Imported {count}-color map tile-animation-2 palette to raw blob ({body.Length} bytes)"
                };
                result.WrittenPaths.Add(absOutBlob);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// PURE structural round-trip proof for a map tile-animation-2 PALETTE BODY (#1360): true iff
        /// <paramref name="body"/> is non-null, has even length, the count is positive, and
        /// <c>body.Length == count*2</c>. Try/catch → false.
        ///
        /// <para>This is source-level structure-exact IDENTITY, NOT a byte-pinned ROM round-trip.
        /// For a byte-exact ROM mismatch proof use <see cref="VerifyMapAnime2PalAgainstRom"/>.</para>
        /// </summary>
        public static bool RoundTripMapAnime2PalBody(byte[] body, int count)
        {
            try
            {
                if (body == null) return false;
                if (body.Length % 2 != 0) return false;
                if (count < 1) return false;
                return body.Length == count * 2;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Byte-exact ROM-backed mismatch proof for a map tile-animation-2 PALETTE block (#1360): compare
        /// the raw ROM palette region at <paramref name="palAddr"/> against the <c>.mapanime2pal</c> file
        /// body byte-for-byte. This is the ONLY ROM-backed verification path (export/import never touch the
        /// ROM). READ-ONLY (never mutates the ROM), NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="palAddr">ROM byte offset of the raw palette data block.</param>
        /// <param name="count">Number of <c>u16</c> colors, 1..255.</param>
        /// <param name="absIn">Absolute path to the <c>.mapanime2pal</c> file (with required sidecar).</param>
        public static DecompAssetResult VerifyMapAnime2PalAgainstRom(ROM rom, uint palAddr, int count, string absIn)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absIn))
                    return Fail(DecompAssetStatus.BadArgs, "Input .mapanime2pal path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // Validate the .mapanime2pal file first (required sidecar, format, count, length).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation2Palette, absIn);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                if (count < 1 || count > 255)
                    return Fail(DecompAssetStatus.NotData, $"Invalid map tile-animation-2 palette count {count} (must be 1..255)");

                long bodyLen = (long)count * 2;
                if (!U.isSafetyOffset(palAddr, rom)
                    || palAddr + bodyLen > rom.Data.Length
                    || palAddr + bodyLen - 1 >= rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Palette region [0x{palAddr:X}, 0x{palAddr + bodyLen:X}) is outside ROM (size 0x{rom.Data.Length:X})");

                byte[] body = File.ReadAllBytes(absIn);
                if (body.Length != bodyLen)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != count*2 ({count}*2 = {bodyLen})");

                for (int i = 0; i < body.Length; i++)
                {
                    byte romByte = rom.Data[palAddr + i];
                    byte fileByte = body[i];
                    if (romByte != fileByte)
                        return Fail(DecompAssetStatus.NotData,
                            $"Palette mismatch at byte offset {i}: ROM=0x{romByte:X2} file=0x{fileByte:X2}");
                }

                return new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Verified {count}-color map tile-animation-2 palette byte-identical to ROM"
                };
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Read <c>count</c> from a <c>.mapanime2pal.json</c> sidecar (the export-side JSON). NEVER throws;
        /// returns false on any fault or non-positive count. Public so the CLI <c>--roundtrip-asset</c>
        /// path can size the palette body before calling <see cref="RoundTripMapAnime2PalBody"/>.
        /// </summary>
        public static bool TryReadMapAnime2PalCount(string sidecarPath, out int count)
        {
            count = 0;
            try
            {
                if (string.IsNullOrEmpty(sidecarPath) || !File.Exists(sidecarPath))
                    return false;
                string json = File.ReadAllText(sidecarPath);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                if (root.TryGetProperty("count", out System.Text.Json.JsonElement c)
                    && c.ValueKind == System.Text.Json.JsonValueKind.Number)
                    count = c.GetInt32();
                return count > 0;
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

                Directory.CreateDirectory(absOutDir);

                // #1774: the FE8J (JP) decomp tree consumes text from texts/jp_texts.txt
                // (header "#0xNNNN", no blank separator) via msg_jp.py — NOT the fe8u
                // texts.txt/textdefs.txt "# msg" migration format. jp_textdefs.txt
                // (control-token table) and jp_huffman_tiebreaks.txt (ROM-derived) are
                // hand/ROM-maintained and must NOT be overwritten by a text dump.
                bool isJp = rom.RomInfo?.is_multibyte ?? false;
                if (isJp)
                {
                    string textsDir = Path.Combine(absOutDir, "texts");
                    Directory.CreateDirectory(textsDir);
                    string jpTextsPath = Path.Combine(textsDir, "jp_texts.txt");
                    File.WriteAllText(jpTextsPath, FormatTextsJp(entries), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    var jpResult = new DecompAssetResult
                    {
                        Status = DecompAssetStatus.Ok,
                        Message = $"Exported {entries.Count} JP text entries to texts/jp_texts.txt " +
                                  "(jp_textdefs.txt and jp_huffman_tiebreaks.txt are hand/ROM-maintained and were left untouched)"
                    };
                    jpResult.WrittenPaths.Add(jpTextsPath);
                    return jpResult;
                }

                var (textsTxt, textdefsTxt) = FormatTexts(entries);
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

        // ---- ExportShops (#1149) ----

        /// <summary>
        /// One <c>u16</c> shop item entry plus its already-resolved display name. The name is
        /// resolved by <see cref="ExportShops"/> (which owns the ROM) and stored here so the
        /// formatter stays ROM-independent (#1149).
        /// </summary>
        internal readonly struct ShopItemEntry
        {
            /// <summary>
            /// The full 16-bit item entry — the low byte is the item id; the high byte is a
            /// flag preserved verbatim (it is meaningful to some hacks and to the decomp
            /// <c>u16[]</c> shop lists).
            /// </summary>
            public readonly ushort Value;
            /// <summary>The pre-resolved item display name (from the export ROM), for the comment.</summary>
            public readonly string Name;
            public ShopItemEntry(ushort value, string name) { Value = value; Name = name ?? ""; }
        }

        /// <summary>
        /// One shop's exportable contents: a display label, the source address of its
        /// sentinel-terminated item list, the address of the 4-byte pointer slot that
        /// references it, and the <c>u16</c> item entries (terminator excluded, names already
        /// resolved). Used by <see cref="ExportShops"/> and the pure <see cref="FormatShops"/>
        /// formatter (testable without a ROM).
        /// </summary>
        internal readonly struct ShopExportRecord
        {
            /// <summary>
            /// Human-readable shop label (e.g. "Preparation Shop", "Ch1 Armory"), already
            /// sanitized of control characters so it is safe to inline in a comment.
            /// </summary>
            public readonly string Label;
            /// <summary>ROM byte offset of the shop's item list (the ORG target).</summary>
            public readonly uint ShopAddr;
            /// <summary>ROM byte offset of the 4-byte pointer slot that references the list.</summary>
            public readonly uint PointerSlotAddr;
            /// <summary>The <c>u16</c> item entries (with resolved names), in order, terminator excluded.</summary>
            public readonly List<ShopItemEntry> Items;
            public ShopExportRecord(string label, uint shopAddr, uint pointerSlotAddr, List<ShopItemEntry> items)
            { Label = label ?? ""; ShopAddr = shopAddr; PointerSlotAddr = pointerSlotAddr; Items = items ?? new List<ShopItemEntry>(); }
        }

        /// <summary>
        /// Strip control characters (anything below <c>0x20</c> or <c>0x7F</c>) from a label so
        /// it can be inlined into a <c>//</c> comment without corrupting the line. Worldmap
        /// point names come from the ROM text table and may carry raw FE control bytes (e.g.
        /// <c>0x1F</c>); those are replaced with a single space and collapsed. NEVER throws.
        /// </summary>
        internal static string SanitizeLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            var sb = new StringBuilder(label.Length);
            bool lastWasSpace = false;
            foreach (char c in label)
            {
                bool isControl = c < 0x20 || c == 0x7F;
                if (isControl)
                {
                    if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = (c == ' ');
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Export every shop's item list to a reviewable EA <c>.event</c> MIGRATION artifact
        /// (<c>shops.event</c> under <paramref name="absOutDir"/>).
        ///
        /// <para>Shop inventories in the GBA FE engine are NOT a manifest-owned rectangular
        /// C-array row table: each is a variable-length list of <c>u16</c> item entries,
        /// <b>terminated by a zero item id</b> (the low byte == 0, i.e. <c>ITEM_NONE</c> —
        /// the same termination FEBuilder's <see cref="ItemShopCore"/> and the editor use),
        /// reached only via scattered pointers (the hensei preparation pointer, FE8
        /// worldmap-point shop pointers, and per-map event-cond OBJECT records). The
        /// fireemblem8u decomp tree DOES carry source-level shop-list symbols (e.g.
        /// <c>ItemList_WM_*Armory</c> in <c>src/worldmap_shop_data.c</c>,
        /// <c>GMapNodeData.{armory,vendor,secretShop}</c> pointers, and <c>Armory(...)</c>
        /// event macros), but FEBuilder has no manifest mapping from a ROM shop address /
        /// pointer slot to its owning list symbol, and no variable-length list writer/repoint
        /// model. So the <see cref="DecompSourceWriterCore"/> in-place row rewriter cannot
        /// target shops — the decomp migration path for shops is this EXPORT, not an in-place
        /// writer.</para>
        ///
        /// <para>The emitted file is a MIGRATION AID, not a byte-pinned round-trip (exactly
        /// like <see cref="ExportText"/>): it recreates each shop list at its recorded
        /// address via <c>ORG</c> + <c>SHORT</c> (u16) directives the decomp build
        /// understands. The contributor drops it into the source tree and ORGs/repoints as
        /// needed.</para>
        ///
        /// <para>Item names in the comments are resolved here against the ACTIVE ROM
        /// (<see cref="CoreState.ROM"/>) — the CLI loads the export ROM into the active slot,
        /// so the common path resolves correctly; if the passed <paramref name="rom"/> is not
        /// the active ROM the entries still emit (with neutral names) so the artifact is never
        /// wrong, just less annotated.</para>
        ///
        /// READ-ONLY (never mutates the ROM) and NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="absOutDir">Absolute path to the output directory (created if absent).</param>
        public static DecompAssetResult ExportShops(ROM rom, string absOutDir)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutDir))
                    return Fail(DecompAssetStatus.BadArgs, "Output directory path is null or empty");

                // NameResolver.GetItemName reads the ambient CoreState.ROM, so only resolve
                // names when the passed ROM IS the active one (the CLI export path loads the
                // export ROM into CoreState.ROM, so this is the common case). Otherwise emit
                // the u16 entries with no name comment rather than wrong/empty names.
                bool canResolveNames = ReferenceEquals(rom, CoreState.ROM);

                var records = new List<ShopExportRecord>();
                List<AddrResult> shops = ItemShopCore.MakeShopList(rom) ?? new List<AddrResult>();
                uint romLen = (uint)rom.Data.Length;
                foreach (AddrResult shop in shops)
                {
                    var items = new List<ShopItemEntry>();
                    for (uint i = 0; i < ItemShopCore.MAX_SCAN_ENTRIES; i++)
                    {
                        uint entryAddr = shop.addr + i * ItemShopCore.ENTRY_SIZE;
                        if (entryAddr + ItemShopCore.ENTRY_SIZE > romLen) break;
                        // Shop entries are u16; ItemShopCore (and the editor) terminate the
                        // list on the item-id LOW BYTE == 0 (ITEM_NONE). Mirror that exactly.
                        if (rom.u8(entryAddr) == 0) break;   // sentinel terminator (low byte == 0)
                        ushort value = (ushort)rom.u16(entryAddr);
                        // Resolve the item name HERE (we own the ROM); the formatter stays pure.
                        string name = canResolveNames
                            ? (NameResolver.GetItemName((uint)(value & 0xFF)) ?? "")
                            : "";
                        items.Add(new ShopItemEntry(value, name));
                    }
                    records.Add(new ShopExportRecord(SanitizeLabel(shop.name), shop.addr, shop.tag, items));
                }

                string body = FormatShops(records);

                Directory.CreateDirectory(absOutDir);
                string outPath = Path.Combine(absOutDir, "shops.event");
                File.WriteAllText(outPath, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Exported {records.Count} shop(s)"
                };
                result.WrittenPaths.Add(outPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        // ---- Internal formatters (testable without ROM) ----

        /// <summary>
        /// Format shop records into the <c>shops.event</c> migration body. GENUINELY PURE —
        /// it formats only the pre-resolved <see cref="ShopExportRecord"/> data (labels +
        /// u16 values + already-resolved item names from <see cref="ExportShops"/>) and NEVER
        /// reads the ROM, so unit tests exercise the exact byte-stable format with no ROM.
        /// NEVER throws.
        ///
        /// <para>Per shop: a comment header (sanitized label + source list address +
        /// pointer-slot address), then <c>ORG 0x&lt;addr&gt;</c>, one <c>SHORT 0xNNNN</c>
        /// (u16) line per item (full 16-bit value, with the pre-resolved item name in a
        /// trailing comment), then a <c>SHORT 0x0000</c> terminator (<c>ITEM_NONE</c>). Shops
        /// are separated by a blank line. A null record list / null inner item list is
        /// treated as empty.</para>
        /// </summary>
        internal static string FormatShops(List<ShopExportRecord> shops)
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("// FEBuilderGBA shop-list migration export (#1149)");
                sb.AppendLine("// Migration aid (NOT source-backed in-place editing, NOT a byte-pinned");
                sb.AppendLine("// round-trip): recreates each shop's u16 item list, ITEM_NONE-terminated,");
                sb.AppendLine("// at its source address. ORG/repoint into your decomp tree as needed.");
                sb.AppendLine();

                if (shops != null)
                {
                    foreach (ShopExportRecord shop in shops)
                    {
                        sb.AppendLine($"// Shop: {shop.Label}  (list @ 0x{shop.ShopAddr:X}, ptr-slot @ 0x{shop.PointerSlotAddr:X})");
                        sb.AppendLine($"ORG 0x{shop.ShopAddr:X}");
                        var items = shop.Items;
                        if (items != null)
                        {
                            foreach (ShopItemEntry entry in items)
                            {
                                // Name was pre-resolved by ExportShops (which owns the ROM).
                                sb.AppendLine($"SHORT 0x{entry.Value:X4}   // {entry.Name}");
                            }
                        }
                        sb.AppendLine("SHORT 0x0000   // terminator (ITEM_NONE)");
                        sb.AppendLine();
                    }
                }
            }
            catch
            {
                // never throw at the boundary — return whatever was built
            }
            return sb.ToString();
        }

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

        /// <summary>
        /// #1774: format text entries for the FE8J (JP) decomp tree's
        /// <c>texts/jp_texts.txt</c>, which <c>msg_jp.py</c> consumes: a
        /// <c>#0xNNNN</c> header line followed by the decoded text line, with NO
        /// <c># msg</c> prefix and NO blank separator between entries. LF line
        /// endings (decomp convention). ROM-free so it is unit-tested directly.
        /// </summary>
        internal static string FormatTextsJp(List<(uint textId, string text)> entries)
        {
            var texts = new StringBuilder();
            if (entries != null)
            {
                foreach (var (id, text) in entries)
                {
                    texts.Append("#0x" + id.ToString("X4") + "\n");
                    texts.Append((text ?? "") + "\n");
                }
            }
            return texts.ToString();
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

        static string BuildMapChangeJson(int w, int h, uint addrOffset)
        {
            // Hand-built (same shape as BuildMapJson); srcAddr is provenance metadata ONLY.
            return $"{{\n  \"width\": {w},\n  \"height\": {h},\n  \"srcAddr\": \"0x{addrOffset:X}\",\n  \"format\": \"febuilder-mapchange-u16\"\n}}\n";
        }

        static string BuildMapAnime2PalJson(int count, uint addrOffset)
        {
            // Hand-built (twin of BuildMapChangeJson with a single count descriptor); srcAddr is
            // provenance metadata ONLY (no symbol/owner is fabricated).
            return $"{{\n  \"count\": {count},\n  \"srcAddr\": \"0x{addrOffset:X}\",\n  \"format\": \"febuilder-mapanime2-pal-u16\"\n}}\n";
        }


        // ---- OBJ tileset LZ77 decompressed-payload export/import/verify (#1371) ----

        /// <summary>
        /// Export an OBJ tileset LZ77 block at <paramref name="objAddr"/> by LZ77-DECOMPRESSING it
        /// and writing the DECOMPRESSED 4bpp payload to <paramref name="absOutObjTilesPath"/>
        /// plus a sidecar JSON. This is the source body the decomp build re-compresses (FEBuilder's
        /// LZ77 packer is non-canonical, so the source is the decompressed payload, NOT the stream).
        /// READ-ONLY (never mutates the ROM), NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="objAddr">ROM byte offset of the OBJ LZ77 stream (the DEREFERENCED address,
        /// NOT <c>RomInfo.map_obj_pointer</c>). FE7 obj2 split is out of scope.</param>
        /// <param name="absOutObjTilesPath">Absolute path for the output <c>.objtiles</c> file.
        /// A sidecar <c>&lt;path&gt;.json</c> is written at the same path with <c>.json</c> appended.</param>
        public static DecompAssetResult ExportObjTiles(ROM rom, uint objAddr, string absOutObjTilesPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutObjTilesPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output .objtiles path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                if (!U.isSafetyOffset(objAddr, rom))
                    return Fail(DecompAssetStatus.NotData, $"Address 0x{objAddr:X} is outside the ROM safety range");

                // The 4-byte LZ77 header is at objAddr..objAddr+3. getCompressedSize reads
                // input[offset+3] but only guards length-offset < 3, so an objAddr within the
                // last 1-3 ROM bytes would throw. Guard the FULL header in-bounds first so the
                // boundary surfaces as a clean NotData, never a Faulted exception.
                if ((long)objAddr + 4 > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 header at 0x{objAddr:X} extends beyond ROM (romSize={rom.Data.Length})");

                uint compLen = LZ77.getCompressedSize(rom.Data, objAddr);
                if (compLen == 0)
                    return Fail(DecompAssetStatus.NotData,
                        $"0x{objAddr:X} is not a valid LZ77 stream (getCompressedSize returned 0)");

                if ((long)objAddr + compLen > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 stream at 0x{objAddr:X} extends beyond ROM (compLen={compLen}, romSize={rom.Data.Length})");

                byte[] body = LZ77.decompress(rom.Data, objAddr);
                if (body == null || body.Length == 0)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 decompression failed at 0x{objAddr:X}");

                EnsureParentDir(absOutObjTilesPath);
                File.WriteAllBytes(absOutObjTilesPath, body);

                string jsonPath = absOutObjTilesPath + ".json";
                string json = BuildObjTilesJson(body.Length, objAddr);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = $"OBJ tileset exported ({body.Length} bytes decompressed)" };
                result.WrittenPaths.Add(absOutObjTilesPath);
                result.WrittenPaths.Add(jsonPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Re-import a <c>.objtiles</c> decompressed 4bpp OBJ payload (the inverse of
        /// <see cref="ExportObjTiles"/>) — an IDENTITY copy of the validated decompressed body
        /// to <paramref name="absOutBlobPath"/>. This method NEVER reads <see cref="CoreState.ROM"/>,
        /// NEVER LZ77-compresses, NEVER mutates the ROM, and NEVER throws.
        /// </summary>
        /// <param name="absInObjTilesPath">Absolute path to the input <c>.objtiles</c> file.
        /// A sidecar <c>&lt;path&gt;.json</c> with <c>"format": "febuilder-objtiles-lz77"</c> and
        /// <c>"length"</c> is REQUIRED.</param>
        /// <param name="absOutBlobPath">Absolute path for the output RAW decompressed OBJ blob
        /// (identity copy of the validated body).</param>
        public static DecompAssetResult ImportObjTiles(string absInObjTilesPath, string absOutBlobPath)
        {
            try
            {
                if (string.IsNullOrEmpty(absInObjTilesPath))
                    return Fail(DecompAssetStatus.BadArgs, "Input .objtiles path is null or empty");
                if (string.IsNullOrEmpty(absOutBlobPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output blob path is null or empty");

                // Structural validation (required sidecar, format, length).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.ObjTiles, absInObjTilesPath);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                byte[] body = File.ReadAllBytes(absInObjTilesPath);

                // The sidecar length is REQUIRED to verify the body before the identity copy.
                string sidecar = absInObjTilesPath + ".json";
                if (!TryReadObjTilesLength(sidecar, out int len))
                    return Fail(DecompAssetStatus.NotData, "sidecar .objtiles.json required to read length");

                if (body.Length != len)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != sidecar length {len}");

                // Identity copy: write the validated body VERBATIM.
                EnsureParentDir(absOutBlobPath);
                File.WriteAllBytes(absOutBlobPath, body);

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Imported OBJ tileset decompressed payload to blob ({body.Length} bytes)"
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
        /// PURE structural round-trip proof for an OBJ tileset decompressed body (#1371): true iff
        /// <paramref name="body"/> is non-null, <paramref name="expectedLen"/> is positive, and
        /// <c>body.Length == expectedLen</c>. Try/catch → false.
        ///
        /// <para>This is source-level structure-exact IDENTITY, NOT a byte-pinned ROM round-trip.
        /// For a byte-exact ROM mismatch proof use <see cref="VerifyObjTilesAgainstRom"/>.</para>
        /// </summary>
        public static bool RoundTripObjTilesBody(byte[] body, int expectedLen)
        {
            try
            {
                if (body == null) return false;
                if (expectedLen <= 0) return false;
                return body.Length == expectedLen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decompress-and-byte-compare ROM-backed mismatch proof for an OBJ tileset (#1371):
        /// LZ77-decompresses the ROM at <paramref name="objAddr"/> and compares byte-for-byte
        /// against the <c>.objtiles</c> file body. READ-ONLY (never mutates the ROM), NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="objAddr">ROM byte offset of the OBJ LZ77 stream (DEREFERENCED address).</param>
        /// <param name="absInObjTilesPath">Absolute path to the <c>.objtiles</c> file (with required sidecar).</param>
        public static DecompAssetResult VerifyObjTilesAgainstRom(ROM rom, uint objAddr, string absInObjTilesPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absInObjTilesPath))
                    return Fail(DecompAssetStatus.BadArgs, "Input .objtiles path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // Validate the .objtiles file first.
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.ObjTiles, absInObjTilesPath);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                if (!U.isSafetyOffset(objAddr, rom))
                    return Fail(DecompAssetStatus.NotData, $"Address 0x{objAddr:X} is outside the ROM safety range");

                // Guard the FULL 4-byte LZ77 header in-bounds before getCompressedSize (which
                // reads input[offset+3]) so a last-1-3-byte objAddr surfaces as NotData, not Faulted.
                if ((long)objAddr + 4 > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 header at 0x{objAddr:X} extends beyond ROM (romSize={rom.Data.Length})");

                uint compLen = LZ77.getCompressedSize(rom.Data, objAddr);
                if (compLen == 0)
                    return Fail(DecompAssetStatus.NotData,
                        $"0x{objAddr:X} is not a valid LZ77 stream (getCompressedSize returned 0)");

                if ((long)objAddr + compLen > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 stream at 0x{objAddr:X} extends beyond ROM (compLen={compLen}, romSize={rom.Data.Length})");

                byte[] romBody = LZ77.decompress(rom.Data, objAddr);
                // LZ77.decompress returns an EMPTY array (not null) on failure — treat
                // null OR empty as a decompression fault.
                if (romBody == null || romBody.Length == 0)
                    return Fail(DecompAssetStatus.NotData, $"LZ77 decompression failed at 0x{objAddr:X}");

                byte[] body = File.ReadAllBytes(absInObjTilesPath);
                string sidecar = absInObjTilesPath + ".json";
                // The sidecar length is REQUIRED — a read/parse fault here must fail cleanly,
                // never proceed with a bogus (zero) length.
                if (!TryReadObjTilesLength(sidecar, out int len))
                    return Fail(DecompAssetStatus.NotData, "sidecar .objtiles.json required to read length");

                // Compare lengths BEFORE byte-diff.
                if (romBody.Length != len)
                    return Fail(DecompAssetStatus.NotData,
                        $"ROM decompressed length {romBody.Length} != sidecar length {len}");

                if (romBody.Length != body.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"ROM decompressed length {romBody.Length} != file length {body.Length}");

                for (int i = 0; i < body.Length; i++)
                {
                    byte romByte = romBody[i];
                    byte fileByte = body[i];
                    if (romByte != fileByte)
                        return Fail(DecompAssetStatus.NotData,
                            $"OBJ tileset mismatch at byte offset {i}: ROM=0x{romByte:X2} file=0x{fileByte:X2}");
                }

                return new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Verified OBJ tileset decompressed payload byte-identical to ROM ({body.Length} bytes)"
                };
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Read <c>length</c> from a <c>.objtiles.json</c> sidecar. NEVER throws;
        /// returns false on any fault or a non-positive length. Public so the CLI
        /// <c>--roundtrip-asset</c> path can size the body before calling
        /// <see cref="RoundTripObjTilesBody"/>.
        /// </summary>
        public static bool TryReadObjTilesLength(string sidecarPath, out int len)
        {
            len = 0;
            try
            {
                if (string.IsNullOrEmpty(sidecarPath) || !File.Exists(sidecarPath))
                    return false;
                string json = File.ReadAllText(sidecarPath);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                if (root.TryGetProperty("length", out System.Text.Json.JsonElement l)
                    && l.ValueKind == System.Text.Json.JsonValueKind.Number)
                    len = l.GetInt32();
                return len > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Build the sidecar JSON for an OBJ tileset export. Hand-built to avoid serializer deps.
        /// <paramref name="addrOffset"/> is provenance ONLY — the decomp build re-compresses the
        /// decompressed body (FEBuilder's LZ77 packer is non-canonical).
        /// </summary>
        static string BuildObjTilesJson(int length, uint addrOffset)
        {
            // srcAddr is provenance ONLY — the decomp build re-compresses the decompressed body.
            // FEBuilder's LZ77 packer is non-canonical; the source is the DECOMPRESSED 4bpp payload.
            return $"{{\n  \"length\": {length},\n  \"srcAddr\": \"0x{addrOffset:X}\",\n  \"format\": \"febuilder-objtiles-lz77\"\n}}\n";
        }

        // ---- Map chipset TSA/config LZ77 decompressed-payload export/import/verify (#1375) ----

        /// <summary>
        /// Export a map chipset TSA/CONFIG LZ77 block at <paramref name="configAddr"/> by
        /// LZ77-DECOMPRESSING it and writing the DECOMPRESSED config payload to
        /// <paramref name="absOutPath"/> plus a sidecar JSON (#1375). This is the structural
        /// TWIN of <see cref="ExportObjTiles"/>: the chipset config is a single LZ77 stream
        /// reached by one dereferenced CONFIG-PLIST pointer (WF <c>ImageUtilMap.UnLZ77ChipsetData</c>
        /// = <c>LZ77.decompress(config_offset)</c>), so the source body is the DECOMPRESSED bytes,
        /// NOT a byte-pinned LZ77 stream (FEBuilder's LZ77 packer is non-canonical, so the build
        /// re-compresses). READ-ONLY (never mutates the ROM), NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="configAddr">ROM byte offset of the chipset config LZ77 stream (the
        /// DEREFERENCED address — e.g. <c>MapChangeCore.PlistToOffsetAddr(CONFIG, plist)</c> —
        /// NOT <c>RomInfo.map_config_pointer</c>). FE7 split layouts use a separate per-plist
        /// <c>configAddr</c>; this method exports ONE stream.</param>
        /// <param name="absOutPath">Absolute path for the output <c>.mapchipconfig</c> file.
        /// A sidecar <c>&lt;path&gt;.json</c> is written at the same path with <c>.json</c> appended.</param>
        public static DecompAssetResult ExportMapChipConfig(ROM rom, uint configAddr, string absOutPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output .mapchipconfig path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                if (!U.isSafetyOffset(configAddr, rom))
                    return Fail(DecompAssetStatus.NotData, $"Address 0x{configAddr:X} is outside the ROM safety range");

                // The 4-byte LZ77 header is at configAddr..configAddr+3. getCompressedSize reads
                // input[offset+3] but only guards length-offset < 3, so a configAddr within the
                // last 1-3 ROM bytes would throw. Guard the FULL header in-bounds first so the
                // boundary surfaces as a clean NotData, never a Faulted exception.
                if ((long)configAddr + 4 > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 header at 0x{configAddr:X} extends beyond ROM (romSize={rom.Data.Length})");

                uint compLen = LZ77.getCompressedSize(rom.Data, configAddr);
                if (compLen == 0)
                    return Fail(DecompAssetStatus.NotData,
                        $"0x{configAddr:X} is not a valid LZ77 stream (getCompressedSize returned 0)");

                if ((long)configAddr + compLen > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 stream at 0x{configAddr:X} extends beyond ROM (compLen={compLen}, romSize={rom.Data.Length})");

                byte[] body = LZ77.decompress(rom.Data, configAddr);
                if (body == null || body.Length == 0)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 decompression failed at 0x{configAddr:X}");

                EnsureParentDir(absOutPath);
                File.WriteAllBytes(absOutPath, body);

                string jsonPath = absOutPath + ".json";
                string json = BuildMapChipConfigJson(body.Length, configAddr);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = $"Map chipset config exported ({body.Length} bytes decompressed)" };
                result.WrittenPaths.Add(absOutPath);
                result.WrittenPaths.Add(jsonPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Re-import a <c>.mapchipconfig</c> decompressed chipset config payload (the inverse of
        /// <see cref="ExportMapChipConfig"/>, #1375) — an IDENTITY copy of the validated decompressed
        /// body to <paramref name="absOutBlobPath"/>. This method NEVER reads <see cref="CoreState.ROM"/>,
        /// NEVER LZ77-compresses, NEVER mutates the ROM, and NEVER throws.
        /// </summary>
        /// <param name="absInPath">Absolute path to the input <c>.mapchipconfig</c> file.
        /// A sidecar <c>&lt;path&gt;.json</c> with <c>"format": "febuilder-mapchipconfig-lz77"</c> and
        /// <c>"length"</c> is REQUIRED.</param>
        /// <param name="absOutBlobPath">Absolute path for the output RAW decompressed config blob
        /// (identity copy of the validated body).</param>
        public static DecompAssetResult ImportMapChipConfig(string absInPath, string absOutBlobPath)
        {
            try
            {
                if (string.IsNullOrEmpty(absInPath))
                    return Fail(DecompAssetStatus.BadArgs, "Input .mapchipconfig path is null or empty");
                if (string.IsNullOrEmpty(absOutBlobPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output blob path is null or empty");

                // Structural validation (required sidecar, format, length).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChipConfig, absInPath);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                byte[] body = File.ReadAllBytes(absInPath);

                // The sidecar length is REQUIRED to verify the body before the identity copy.
                string sidecar = absInPath + ".json";
                if (!TryReadMapChipConfigLength(sidecar, out int len))
                    return Fail(DecompAssetStatus.NotData, "sidecar .mapchipconfig.json required to read length");

                if (body.Length != len)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != sidecar length {len}");

                // Identity copy: write the validated body VERBATIM.
                EnsureParentDir(absOutBlobPath);
                File.WriteAllBytes(absOutBlobPath, body);

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Imported map chipset config decompressed payload to blob ({body.Length} bytes)"
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
        /// PURE structural round-trip proof for a map chipset config decompressed body (#1375): true iff
        /// <paramref name="body"/> is non-null, <paramref name="expectedLen"/> is positive, and
        /// <c>body.Length == expectedLen</c>. Try/catch → false.
        ///
        /// <para>This is source-level structure-exact IDENTITY, NOT a byte-pinned ROM round-trip.
        /// For a byte-exact ROM mismatch proof use <see cref="VerifyMapChipConfigAgainstRom"/>.</para>
        /// </summary>
        public static bool RoundTripMapChipConfigBody(byte[] body, int expectedLen)
        {
            try
            {
                if (body == null) return false;
                if (expectedLen <= 0) return false;
                return body.Length == expectedLen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decompress-and-byte-compare ROM-backed mismatch proof for a map chipset config (#1375):
        /// LZ77-decompresses the ROM at <paramref name="configAddr"/> and compares byte-for-byte
        /// against the <c>.mapchipconfig</c> file body. READ-ONLY (never mutates the ROM), NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="configAddr">ROM byte offset of the chipset config LZ77 stream (DEREFERENCED address).</param>
        /// <param name="absInPath">Absolute path to the <c>.mapchipconfig</c> file (with required sidecar).</param>
        public static DecompAssetResult VerifyMapChipConfigAgainstRom(ROM rom, uint configAddr, string absInPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absInPath))
                    return Fail(DecompAssetStatus.BadArgs, "Input .mapchipconfig path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // Validate the .mapchipconfig file first.
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapChipConfig, absInPath);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                if (!U.isSafetyOffset(configAddr, rom))
                    return Fail(DecompAssetStatus.NotData, $"Address 0x{configAddr:X} is outside the ROM safety range");

                // Guard the FULL 4-byte LZ77 header in-bounds before getCompressedSize (which
                // reads input[offset+3]) so a last-1-3-byte configAddr surfaces as NotData, not Faulted.
                if ((long)configAddr + 4 > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 header at 0x{configAddr:X} extends beyond ROM (romSize={rom.Data.Length})");

                uint compLen = LZ77.getCompressedSize(rom.Data, configAddr);
                if (compLen == 0)
                    return Fail(DecompAssetStatus.NotData,
                        $"0x{configAddr:X} is not a valid LZ77 stream (getCompressedSize returned 0)");

                if ((long)configAddr + compLen > rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"LZ77 stream at 0x{configAddr:X} extends beyond ROM (compLen={compLen}, romSize={rom.Data.Length})");

                byte[] romBody = LZ77.decompress(rom.Data, configAddr);
                // LZ77.decompress returns an EMPTY array (not null) on failure — treat
                // null OR empty as a decompression fault.
                if (romBody == null || romBody.Length == 0)
                    return Fail(DecompAssetStatus.NotData, $"LZ77 decompression failed at 0x{configAddr:X}");

                byte[] body = File.ReadAllBytes(absInPath);
                string sidecar = absInPath + ".json";
                // The sidecar length is REQUIRED — a read/parse fault here must fail cleanly,
                // never proceed with a bogus (zero) length.
                if (!TryReadMapChipConfigLength(sidecar, out int len))
                    return Fail(DecompAssetStatus.NotData, "sidecar .mapchipconfig.json required to read length");

                // Compare lengths BEFORE byte-diff.
                if (romBody.Length != len)
                    return Fail(DecompAssetStatus.NotData,
                        $"ROM decompressed length {romBody.Length} != sidecar length {len}");

                if (romBody.Length != body.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"ROM decompressed length {romBody.Length} != file length {body.Length}");

                for (int i = 0; i < body.Length; i++)
                {
                    byte romByte = romBody[i];
                    byte fileByte = body[i];
                    if (romByte != fileByte)
                        return Fail(DecompAssetStatus.NotData,
                            $"Map chipset config mismatch at byte offset {i}: ROM=0x{romByte:X2} file=0x{fileByte:X2}");
                }

                return new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Verified map chipset config decompressed payload byte-identical to ROM ({body.Length} bytes)"
                };
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Read <c>length</c> from a <c>.mapchipconfig.json</c> sidecar. NEVER throws;
        /// returns false on any fault or a non-positive length. Public so the CLI
        /// <c>--roundtrip-asset</c> path can size the body before calling
        /// <see cref="RoundTripMapChipConfigBody"/>.
        /// </summary>
        public static bool TryReadMapChipConfigLength(string sidecarPath, out int len)
        {
            len = 0;
            try
            {
                if (string.IsNullOrEmpty(sidecarPath) || !File.Exists(sidecarPath))
                    return false;
                string json = File.ReadAllText(sidecarPath);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                if (root.TryGetProperty("length", out System.Text.Json.JsonElement l)
                    && l.ValueKind == System.Text.Json.JsonValueKind.Number)
                    len = l.GetInt32();
                return len > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Build the sidecar JSON for a map chipset config export. Hand-built to avoid serializer deps.
        /// <paramref name="addrOffset"/> is provenance ONLY — the decomp build re-compresses the
        /// decompressed body (FEBuilder's LZ77 packer is non-canonical).
        /// </summary>
        static string BuildMapChipConfigJson(int length, uint addrOffset)
        {
            // srcAddr is provenance ONLY — the decomp build re-compresses the decompressed body.
            // FEBuilder's LZ77 packer is non-canonical; the source is the DECOMPRESSED config payload.
            return $"{{\n  \"length\": {length},\n  \"srcAddr\": \"0x{addrOffset:X}\",\n  \"format\": \"febuilder-mapchipconfig-lz77\"\n}}\n";
        }

        // ---- Map tile-animation-1 per-entry RAW 4bpp GRAPHICS block (#1389) ----

        /// <summary>
        /// Export a map tile-animation-1 per-entry RAW 4bpp GRAPHICS DATA BLOCK (#1389) — a RAW
        /// UNCOMPRESSED 4bpp tile-byte block of <paramref name="length"/> bytes — to a
        /// <c>.mapanime1gfx</c> file plus a sidecar JSON. This is the structural TWIN of
        /// <see cref="ExportMapChange"/>/<see cref="ExportMapAnime2Pal"/> (a raw byte block with an
        /// explicit length descriptor), NOT the LZ77 <see cref="ExportObjTiles"/>/
        /// <see cref="ExportMapChipConfig"/> pattern: the WF read/import/rebuild paths treat this block
        /// as raw <c>ImageToByte16Tile</c> 4bpp bytes (a rebuild <c>IMG</c> block) sized by the entry's
        /// <c>+2</c> <c>u16 length</c>, NEVER an LZ77 stream.
        ///
        /// <para>It is reached by each anime-1 entry's <c>+4</c> graphics pointer (see
        /// <see cref="MapTileAnimation1Core.EntryRow.P4"/>; the inverse of anime-2, whose block pointer
        /// is at <c>+0</c>) — NOT the anime-1 ENTRY/PLIST table (pointer-per-row, no clean source owner),
        /// NOT a <c>&lt;&lt;3</c>-shifted <c>.mar</c> layout, NOT LZ77. <paramref name="length"/> is the
        /// raw byte length (the entry's <c>+2</c> field). The body is copied BYTE-FOR-BYTE from the
        /// (already-uncompressed) ROM region at <paramref name="gfxAddr"/>; <c>srcAddr</c> in the sidecar
        /// is provenance metadata ONLY (no symbol/owner is fabricated). READ-ONLY (never mutates the
        /// ROM), NEVER throws.</para>
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="gfxAddr">ROM byte offset of the raw 4bpp graphics block (anime-1 entry <c>+4</c>
        /// pointer, dereferenced).</param>
        /// <param name="length">Raw byte length of the graphics block (anime-1 entry <c>+2</c>), &gt; 0.</param>
        /// <param name="absOutPath">Absolute path for the output <c>.mapanime1gfx</c> file. A sidecar
        /// <c>&lt;path&gt;.json</c> is written at the same path with <c>.json</c> appended.</param>
        public static DecompAssetResult ExportMapAnime1Gfx(ROM rom, uint gfxAddr, int length, string absOutPath)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absOutPath))
                    return Fail(DecompAssetStatus.BadArgs, "Output .mapanime1gfx path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // The entry +2 length is a u16 (1..65535); reject non-positive / overflow.
                if (length < 1 || length > 0xFFFF)
                    return Fail(DecompAssetStatus.NotData, $"Invalid map tile-animation-1 graphics length {length} (must be 1..65535)");

                long bodyLen = length;
                // Bounds: the start must be a safe offset AND the whole body (last byte inclusive)
                // must lie inside the ROM. Use a long for the end so length cannot wrap a uint.
                if (!U.isSafetyOffset(gfxAddr, rom)
                    || gfxAddr + bodyLen > rom.Data.Length
                    || gfxAddr + bodyLen - 1 >= rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Graphics region [0x{gfxAddr:X}, 0x{gfxAddr + bodyLen:X}) is outside ROM (size 0x{rom.Data.Length:X})");

                // Copy the raw graphics body byte-for-byte from the (already-uncompressed) ROM.
                byte[] body = rom.getBinaryData(gfxAddr, (int)bodyLen);
                if (body == null || body.Length != bodyLen)
                    return Fail(DecompAssetStatus.NotData, $"Could not read {bodyLen}-byte graphics body at 0x{gfxAddr:X}");

                EnsureParentDir(absOutPath);
                File.WriteAllBytes(absOutPath, body);

                string jsonPath = absOutPath + ".json";
                string json = BuildMapAnime1GfxJson(length, gfxAddr);
                File.WriteAllText(jsonPath, json, Encoding.UTF8);

                var result = new DecompAssetResult { Status = DecompAssetStatus.Ok, Message = $"Map tile-animation-1 graphics exported ({length} bytes)" };
                result.WrittenPaths.Add(absOutPath);
                result.WrittenPaths.Add(jsonPath);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Re-import a <c>.mapanime1gfx</c> map tile-animation-1 per-entry RAW 4bpp GRAPHICS block (the
        /// inverse of <see cref="ExportMapAnime1Gfx"/>, #1389) into a RAW UNCOMPRESSED blob. The block is
        /// an IDENTITY copy — NO header prepend, NO <c>&gt;&gt;3</c> shift, NO LZ77 compression — so the
        /// output blob is the validated body written VERBATIM.
        ///
        /// <para>This method NEVER reads <see cref="CoreState.ROM"/>, NEVER LZ77-compresses, NEVER mutates
        /// the ROM, and NEVER throws. The REQUIRED sidecar <c>length</c> (validated against the body
        /// length) makes this a genuine source-backed artifact.</para>
        /// </summary>
        /// <param name="absIn">Absolute path to the input <c>.mapanime1gfx</c> file. A sidecar
        /// <c>&lt;path&gt;.json</c> (the export-side JSON) is REQUIRED.</param>
        /// <param name="absOutBlob">Absolute path for the output RAW graphics blob (identity copy of the
        /// validated body).</param>
        public static DecompAssetResult ImportMapAnime1Gfx(string absIn, string absOutBlob)
        {
            try
            {
                if (string.IsNullOrEmpty(absIn))
                    return Fail(DecompAssetStatus.BadArgs, "Input .mapanime1gfx path is null or empty");
                if (string.IsNullOrEmpty(absOutBlob))
                    return Fail(DecompAssetStatus.BadArgs, "Output blob path is null or empty");

                // Structural validation (required sidecar, format, length).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation1Graphics, absIn);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                byte[] body = File.ReadAllBytes(absIn);

                // The sidecar length is REQUIRED (the validator already errors when missing, but
                // re-read here to size-check the body before the identity copy).
                string sidecar = absIn + ".json";
                if (!TryReadMapAnime1GfxLength(sidecar, out int len))
                    return Fail(DecompAssetStatus.NotData, "sidecar .mapanime1gfx.json required to read length");

                if (body.Length != len)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != sidecar length {len}");

                // The block is an identity copy: write the validated body VERBATIM.
                EnsureParentDir(absOutBlob);
                File.WriteAllBytes(absOutBlob, body);

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Imported {len}-byte map tile-animation-1 graphics to raw blob ({body.Length} bytes)"
                };
                result.WrittenPaths.Add(absOutBlob);
                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// PURE structural round-trip proof for a map tile-animation-1 GRAPHICS BODY (#1389): true iff
        /// <paramref name="body"/> is non-null, <paramref name="expectedLen"/> is positive, and
        /// <c>body.Length == expectedLen</c>. Try/catch → false.
        ///
        /// <para>This is source-level structure-exact IDENTITY, NOT a byte-pinned ROM round-trip.
        /// For a byte-exact ROM mismatch proof use <see cref="VerifyMapAnime1GfxAgainstRom"/>.</para>
        /// </summary>
        public static bool RoundTripMapAnime1GfxBody(byte[] body, int expectedLen)
        {
            try
            {
                if (body == null) return false;
                if (expectedLen <= 0) return false;
                return body.Length == expectedLen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Byte-exact ROM-backed mismatch proof for a map tile-animation-1 GRAPHICS block (#1389): compare
        /// the raw ROM graphics region at <paramref name="gfxAddr"/> against the <c>.mapanime1gfx</c> file
        /// body byte-for-byte. This is the ONLY ROM-backed verification path (export/import never touch the
        /// ROM). The block is RAW (uncompressed) — there is NO LZ77 decompression, just a raw byte compare
        /// (the TWIN of <see cref="VerifyMapChangeAgainstRom"/>, NOT the decompress-and-compare
        /// <see cref="VerifyObjTilesAgainstRom"/>). READ-ONLY (never mutates the ROM), NEVER throws.
        /// </summary>
        /// <param name="rom">Loaded ROM. Must not be null.</param>
        /// <param name="gfxAddr">ROM byte offset of the raw 4bpp graphics block.</param>
        /// <param name="length">Raw byte length of the graphics block, &gt; 0.</param>
        /// <param name="absIn">Absolute path to the <c>.mapanime1gfx</c> file (with required sidecar).</param>
        public static DecompAssetResult VerifyMapAnime1GfxAgainstRom(ROM rom, uint gfxAddr, int length, string absIn)
        {
            try
            {
                if (rom == null)
                    return Fail(DecompAssetStatus.BadArgs, "ROM is null");
                if (string.IsNullOrEmpty(absIn))
                    return Fail(DecompAssetStatus.BadArgs, "Input .mapanime1gfx path is null or empty");
                if (rom.Data == null)
                    return Fail(DecompAssetStatus.NotData, "ROM has no data");

                // Validate the .mapanime1gfx file first (required sidecar, format, length).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAsset(AssetKind.MapTileAnimation1Graphics, absIn);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "validation failed: " + detail);
                }

                if (length < 1 || length > 0xFFFF)
                    return Fail(DecompAssetStatus.NotData, $"Invalid map tile-animation-1 graphics length {length} (must be 1..65535)");

                long bodyLen = length;
                if (!U.isSafetyOffset(gfxAddr, rom)
                    || gfxAddr + bodyLen > rom.Data.Length
                    || gfxAddr + bodyLen - 1 >= rom.Data.Length)
                    return Fail(DecompAssetStatus.NotData,
                        $"Graphics region [0x{gfxAddr:X}, 0x{gfxAddr + bodyLen:X}) is outside ROM (size 0x{rom.Data.Length:X})");

                byte[] body = File.ReadAllBytes(absIn);
                if (body.Length != bodyLen)
                    return Fail(DecompAssetStatus.NotData,
                        $"File length {body.Length} != length {bodyLen}");

                for (int i = 0; i < body.Length; i++)
                {
                    byte romByte = rom.Data[gfxAddr + i];
                    byte fileByte = body[i];
                    if (romByte != fileByte)
                        return Fail(DecompAssetStatus.NotData,
                            $"Graphics mismatch at byte offset {i}: ROM=0x{romByte:X2} file=0x{fileByte:X2}");
                }

                return new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Verified {length}-byte map tile-animation-1 graphics byte-identical to ROM"
                };
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Read <c>length</c> from a <c>.mapanime1gfx.json</c> sidecar (the export-side JSON). NEVER throws;
        /// returns false on any fault or a non-positive length. Public so the CLI <c>--roundtrip-asset</c>
        /// path can size the graphics body before calling <see cref="RoundTripMapAnime1GfxBody"/>.
        /// </summary>
        public static bool TryReadMapAnime1GfxLength(string sidecarPath, out int len)
        {
            len = 0;
            try
            {
                if (string.IsNullOrEmpty(sidecarPath) || !File.Exists(sidecarPath))
                    return false;
                string json = File.ReadAllText(sidecarPath);
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
                System.Text.Json.JsonElement root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                if (root.TryGetProperty("length", out System.Text.Json.JsonElement l)
                    && l.ValueKind == System.Text.Json.JsonValueKind.Number)
                    len = l.GetInt32();
                return len > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Build the sidecar JSON for a map tile-animation-1 graphics export. Hand-built to avoid
        /// serializer deps. <paramref name="addrOffset"/> is provenance ONLY. The body is RAW 4bpp bytes
        /// (NOT LZ77) — the source is the raw block exactly as it lives in the ROM.
        /// </summary>
        static string BuildMapAnime1GfxJson(int length, uint addrOffset)
        {
            return $"{{\n  \"length\": {length},\n  \"srcAddr\": \"0x{addrOffset:X}\",\n  \"format\": \"febuilder-mapanime1gfx-raw4bpp\"\n}}\n";
        }

        // ---- Portrait PACKAGE source-tree write-back + round-trip (#1374) ----

        /// <summary>
        /// The resolved files of a portrait PACKAGE directory (#1374): the single composite
        /// sheet PNG and its name-matched JASC <c>.pal</c> sidecar (null when absent). This is
        /// the WHOLE source-owned contract — there is NO fabricated manifest; "metadata" is
        /// exactly the sheet PNG + its matching sidecar + the slot geometry the
        /// <see cref="DecompAssetValidatorCore"/> enforces (#1350/#1353).
        /// </summary>
        public sealed class PortraitPackageFiles
        {
            /// <summary>Absolute path to the single composite sheet PNG (null when none/ambiguous).</summary>
            public string SheetPath;
            /// <summary>Absolute path to the name-matched JASC sidecar (sheet.png -&gt; sheet.pal); null when absent.</summary>
            public string SidecarPath;
            /// <summary>Number of <c>*.png</c> files found in the directory (used for the ambiguity gate).</summary>
            public int PngCount;
        }

        /// <summary>
        /// Resolve a portrait PACKAGE directory to its single sheet PNG + name-matched JASC
        /// sidecar (#1374). A package is UNAMBIGUOUS only when it contains EXACTLY ONE
        /// <c>*.png</c>; the sidecar is the one whose name matches the sheet
        /// (<c>sheet.png</c> -&gt; <c>sheet.pal</c>), mirroring the validator's matching rule.
        ///
        /// <para>Returns <c>null</c> ONLY on a fault or a null/missing directory. For a
        /// zero-or-multiple-PNG (ambiguous) directory it returns a non-null
        /// <see cref="PortraitPackageFiles"/> with <see cref="PortraitPackageFiles.PngCount"/>
        /// set and <see cref="PortraitPackageFiles.SheetPath"/> left <c>null</c> — so callers
        /// signal ambiguity via <c>result.SheetPath == null</c> while still seeing the PNG count.
        /// NEVER throws, NEVER reads the ROM.</para>
        /// </summary>
        public static PortraitPackageFiles ResolvePortraitPackage(string absDir)
        {
            try
            {
                if (string.IsNullOrEmpty(absDir) || !Directory.Exists(absDir))
                    return null;
                string[] pngs = Directory.GetFiles(absDir, "*.png");
                Array.Sort(pngs, StringComparer.Ordinal);
                var files = new PortraitPackageFiles { PngCount = pngs.Length };
                if (pngs.Length != 1)
                    return files; // ambiguous (0 or >1): caller decides; SheetPath stays null.
                files.SheetPath = pngs[0];

                // Sidecar is the one whose name matches the sheet (sheet.png -> sheet.pal).
                string expectedPal = Path.ChangeExtension(pngs[0], ".pal");
                if (File.Exists(expectedPal))
                    files.SidecarPath = expectedPal;
                return files;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Write-back / import an already-validated portrait PACKAGE (#1374): copy the source
        /// package's composite sheet PNG + its name-matched JASC sidecar VERBATIM (structure-
        /// exact identity copy) into <paramref name="absOutDir"/>, but ONLY when the
        /// destination is an UNAMBIGUOUS owner.
        ///
        /// <para>This is the portrait-package analogue of <see cref="ImportMap"/> /
        /// <see cref="ImportObjTiles"/>: it treats the package directory + the geometry/palette
        /// the <see cref="DecompAssetValidatorCore"/> enforces as SOURCE-OWNED input (NOT ROM
        /// patch data). It NEVER reads or mutates <see cref="CoreState.ROM"/> and NEVER throws
        /// (every fault becomes a typed <see cref="DecompAssetResult"/>).</para>
        ///
        /// <para>OWNERSHIP GATE (refuse-before-write — never guesses an owner):</para>
        /// <list type="bullet">
        ///   <item><description>The SOURCE must validate via
        ///     <see cref="DecompAssetValidatorCore.ValidateAssetPackage"/>; any error REFUSES
        ///     (NotData) — bad geometry, OOB slot, palette mismatch, multiple/missing sheets.</description></item>
        ///   <item><description>Destination MISSING or EMPTY → a clean new owner: write.</description></item>
        ///   <item><description>Destination already holds EXACTLY ONE same-shaped package (one PNG,
        ///     optional matching sidecar) → the existing owner: overwrite ONLY when
        ///     <paramref name="overwriteOwner"/> is set, else REFUSE (<c>OWNER_EXISTS</c>).</description></item>
        ///   <item><description>Destination holds MULTIPLE PNGs / a different layout → REFUSE
        ///     (<c>AMBIGUOUS_OWNER</c>); the owner cannot be determined, so nothing is written.</description></item>
        /// </list>
        /// </summary>
        /// <param name="absInDir">Absolute path to the SOURCE package directory.</param>
        /// <param name="absOutDir">Absolute path to the destination owner directory (must already
        /// be project-root-confined by the caller, e.g. via <see cref="ResolveSourcePath"/>).</param>
        /// <param name="allowMainOnly">When true a 96x80 main-mug-only source sheet is accepted
        /// (the validator downgrades INCOMPLETE_PACKAGE to a warning).</param>
        /// <param name="overwriteOwner">When true an existing single-package destination owner is
        /// overwritten; otherwise an existing owner is refused with <c>OWNER_EXISTS</c>.</param>
        public static DecompAssetResult ImportPortraitPackage(string absInDir, string absOutDir, bool allowMainOnly, bool overwriteOwner)
        {
            try
            {
                if (string.IsNullOrEmpty(absInDir))
                    return Fail(DecompAssetStatus.BadArgs, "Input package directory is null or empty");
                if (string.IsNullOrEmpty(absOutDir))
                    return Fail(DecompAssetStatus.BadArgs, "Output package directory is null or empty");
                if (!Directory.Exists(absInDir))
                    return Fail(DecompAssetStatus.NotData, $"Input package directory not found: {absInDir}");

                // 1) Source MUST validate (refuse on any error: geometry/OOB/palette/sheet count).
                AssetValidationResult v = DecompAssetValidatorCore.ValidateAssetPackage(
                    AssetKind.PortraitPackage, absInDir, allowMainOnly);
                if (!v.Ok)
                {
                    AssetIssue first = v.Errors.Count > 0 ? v.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "source package validation failed: " + detail);
                }

                // 2) Resolve the unambiguous SOURCE sheet + sidecar.
                PortraitPackageFiles src = ResolvePortraitPackage(absInDir);
                if (src == null || src.SheetPath == null)
                    return Fail(DecompAssetStatus.NotData,
                        "source package is ambiguous (expected exactly one *.png sheet)");

                // 3) OWNERSHIP GATE on the destination — refuse before any write.
                if (Directory.Exists(absOutDir))
                {
                    string[] destPngs;
                    try { destPngs = Directory.GetFiles(absOutDir, "*.png"); }
                    catch (Exception ex) { return Fail(DecompAssetStatus.Faulted, "cannot inspect destination: " + ex.Message); }

                    if (destPngs.Length > 1)
                        return Fail(DecompAssetStatus.PathRejected,
                            $"[AMBIGUOUS_OWNER] destination '{absOutDir}' holds {destPngs.Length} *.png files; the portrait-package owner cannot be determined. Point --out at the single owning package directory.");
                    if (destPngs.Length == 1)
                    {
                        // An existing single-package owner. Overwrite ONLY when explicitly allowed.
                        if (!overwriteOwner)
                            return Fail(DecompAssetStatus.PathRejected,
                                $"[OWNER_EXISTS] destination '{absOutDir}' already contains a portrait package ('{Path.GetFileName(destPngs[0])}'). Pass --overwrite to replace the owning package.");

                        // REPLACE the existing owner ATOMICALLY-ish: remove the old owner's PNG
                        // AND its name-matched sidecar BEFORE writing the source files. Without this,
                        // an --overwrite where the source sheet has a DIFFERENT filename would leave
                        // the old PNG behind → two PNGs → the dir becomes AMBIGUOUS right after a
                        // "successful" import (Copilot #1379 review). It also drops a stale sidecar
                        // when the source has none, so the owner contract holds in every case.
                        string oldPng = destPngs[0];
                        string oldPal = Path.ChangeExtension(oldPng, ".pal");
                        try
                        {
                            File.Delete(oldPng);
                            if (File.Exists(oldPal)) File.Delete(oldPal);
                        }
                        catch (Exception ex)
                        {
                            return Fail(DecompAssetStatus.Faulted, "cannot replace existing owner: " + ex.Message);
                        }
                    }
                    // destPngs.Length == 0 → empty (of PNGs) dir: a clean new owner.
                }
                // Directory missing → a clean new owner.

                // 4) Identity copy: sheet PNG (named after the SOURCE sheet) + matching sidecar.
                Directory.CreateDirectory(absOutDir);
                string sheetName = Path.GetFileName(src.SheetPath);
                string destSheet = Path.Combine(absOutDir, sheetName);
                File.Copy(src.SheetPath, destSheet, overwrite: true);

                var result = new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = $"Wrote portrait package '{sheetName}'" + (src.SidecarPath != null ? " + sidecar palette" : " (no sidecar palette)")
                };
                result.WrittenPaths.Add(destSheet);

                if (src.SidecarPath != null)
                {
                    string destPal = Path.Combine(absOutDir, Path.GetFileName(src.SidecarPath));
                    File.Copy(src.SidecarPath, destPal, overwrite: true);
                    result.WrittenPaths.Add(destPal);
                }

                return result;
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>
        /// Round-trip a portrait PACKAGE against an explicit BASELINE package (#1374): validate
        /// BOTH the source and the baseline, then compare the source's sheet PNG bytes to the
        /// baseline's sheet bytes AND the source's sidecar to the baseline's sidecar. The
        /// REQUIRED baseline is the ORACLE — this is NOT a self-identity copy, so a
        /// validation-valid but byte-tampered source genuinely MISMATCHES.
        ///
        /// <para>This proves "source package is byte-identical to an expected baseline package"
        /// (source-level structure-exact identity vs a supplied baseline). It is NOT a
        /// ROM-backed round-trip: no canonical ROM→128x112-sheet builder exists, so there is no
        /// preview-ROM byte-pin for a portrait package (documented residual, consistent with the
        /// issue non-goal "the preview ROM is never the source of truth"). READ-ONLY of the ROM
        /// (never touches it), NEVER throws.</para>
        /// </summary>
        /// <param name="absSrcDir">Absolute path to the source package directory.</param>
        /// <param name="absBaselineDir">Absolute path to the expected BASELINE package directory (the oracle).</param>
        /// <param name="allowMainOnly">When true a 96x80 main-mug-only sheet is accepted on BOTH sides.</param>
        public static DecompAssetResult RoundTripPortraitPackageAgainstBaseline(string absSrcDir, string absBaselineDir, bool allowMainOnly)
        {
            try
            {
                if (string.IsNullOrEmpty(absSrcDir))
                    return Fail(DecompAssetStatus.BadArgs, "Source package directory is null or empty");
                if (string.IsNullOrEmpty(absBaselineDir))
                    return Fail(DecompAssetStatus.BadArgs, "Baseline package directory is null or empty");

                // Validate BOTH sides — either invalid → fail (not a clean round-trip).
                AssetValidationResult vs = DecompAssetValidatorCore.ValidateAssetPackage(
                    AssetKind.PortraitPackage, absSrcDir, allowMainOnly);
                if (!vs.Ok)
                {
                    AssetIssue first = vs.Errors.Count > 0 ? vs.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "source package validation failed: " + detail);
                }
                AssetValidationResult vb = DecompAssetValidatorCore.ValidateAssetPackage(
                    AssetKind.PortraitPackage, absBaselineDir, allowMainOnly);
                if (!vb.Ok)
                {
                    AssetIssue first = vb.Errors.Count > 0 ? vb.Errors[0] : null;
                    string detail = first != null ? $"[{first.Code}] {first.Message}" : "unknown error";
                    return Fail(DecompAssetStatus.NotData, "baseline package validation failed: " + detail);
                }

                PortraitPackageFiles src = ResolvePortraitPackage(absSrcDir);
                PortraitPackageFiles baseline = ResolvePortraitPackage(absBaselineDir);
                if (src == null || src.SheetPath == null)
                    return Fail(DecompAssetStatus.NotData, "source package is ambiguous (expected exactly one *.png sheet)");
                if (baseline == null || baseline.SheetPath == null)
                    return Fail(DecompAssetStatus.NotData, "baseline package is ambiguous (expected exactly one *.png sheet)");

                // Sheet bytes must be byte-identical to the baseline (the oracle).
                byte[] srcSheet = File.ReadAllBytes(src.SheetPath);
                byte[] baseSheet = File.ReadAllBytes(baseline.SheetPath);
                if (!BytesEqual(srcSheet, baseSheet))
                    return Fail(DecompAssetStatus.NotData,
                        $"sheet mismatch: source '{Path.GetFileName(src.SheetPath)}' differs from baseline '{Path.GetFileName(baseline.SheetPath)}'");

                // Sidecar presence + bytes must match the baseline too.
                bool srcHasPal = src.SidecarPath != null;
                bool baseHasPal = baseline.SidecarPath != null;
                if (srcHasPal != baseHasPal)
                    return Fail(DecompAssetStatus.NotData,
                        $"sidecar presence mismatch: source {(srcHasPal ? "has" : "lacks")} a palette, baseline {(baseHasPal ? "has" : "lacks")} one");
                if (srcHasPal && baseHasPal)
                {
                    byte[] srcPal = File.ReadAllBytes(src.SidecarPath);
                    byte[] basePal = File.ReadAllBytes(baseline.SidecarPath);
                    if (!BytesEqual(srcPal, basePal))
                        return Fail(DecompAssetStatus.NotData, "sidecar palette bytes differ from baseline");
                }

                return new DecompAssetResult
                {
                    Status = DecompAssetStatus.Ok,
                    Message = "Round-trip OK (portrait package byte-identical to baseline)"
                };
            }
            catch (Exception ex)
            {
                return Fail(DecompAssetStatus.Faulted, ex.Message);
            }
        }

        /// <summary>Byte-array equality (null-safe). NEVER throws.</summary>
        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
