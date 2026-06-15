using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>
    /// The asset kinds <see cref="DecompAssetValidatorCore"/> can validate (#1150),
    /// matching the <c>--export-asset</c> kinds plus portrait/icon graphics specializations.
    /// </summary>
    public enum AssetKind
    {
        /// <summary>Indexed tile graphics PNG (multiple of 8 dims).</summary>
        Graphics,
        /// <summary>JASC .pal palette text file.</summary>
        Palette,
        /// <summary>Portrait mug PNG (96x80 main mug).</summary>
        Portrait,
        /// <summary>Icon PNG (16x16).</summary>
        Icon,
        /// <summary>.mar tile layout binary (validated against its sidecar JSON).</summary>
        MapLayout,
    }

    /// <summary>One validation finding (error or warning): a stable CODE + a message.</summary>
    public sealed class AssetIssue
    {
        /// <summary>Stable machine code (e.g. "NON_INDEXED"). Never null.</summary>
        public string Code;

        /// <summary>Human-readable detail. Never null.</summary>
        public string Message;

        /// <summary>Construct an issue; null fields are coerced to "".</summary>
        public AssetIssue(string code, string message)
        {
            Code = code ?? "";
            Message = message ?? "";
        }
    }

    /// <summary>
    /// Result of validating one asset: <see cref="Ok"/> is true when there are no
    /// errors (warnings are allowed). Both lists are always non-null.
    /// </summary>
    public sealed class AssetValidationResult
    {
        /// <summary>Hard errors that make the asset unsuitable for the decomp pipeline.</summary>
        public List<AssetIssue> Errors = new List<AssetIssue>();

        /// <summary>Non-fatal advisories (dimensions, palette order, missing sidecar).</summary>
        public List<AssetIssue> Warnings = new List<AssetIssue>();

        /// <summary>True when there are zero errors.</summary>
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// READ-ONLY, never-throwing validator for decomp-pipeline IMPORT assets (#1150).
    ///
    /// It reads candidate import files FROM DISK and checks their structure + index-level
    /// invariants BEFORE a contributor wires them into a build — catching the common
    /// failure modes (non-indexed PNG, non-tile-aligned dimensions, out-of-range palette
    /// indices, a .mar whose length / <c>&lt;&lt;3</c> shift invariant is broken). It is a
    /// STRUCTURAL + index-level check, not a full portrait-package validator.
    ///
    /// CRITICAL: it NEVER reads <see cref="CoreState.ROM"/> — it only reads the file path
    /// it is given — and NEVER throws (every fault becomes a validation result).
    /// </summary>
    public static class DecompAssetValidatorCore
    {
        /// <summary>Main mug portrait dimensions (px) — the only auto-accepted portrait size.</summary>
        public const int PortraitMainWidth = 96;
        public const int PortraitMainHeight = 80;

        /// <summary>Icon dimensions (px).</summary>
        public const int IconSize = 16;

        /// <summary>
        /// Validate an asset file. On a missing file returns a single
        /// <c>FILE_NOT_FOUND</c> error. Dispatches by <paramref name="kind"/>. NEVER
        /// reads the ROM. NEVER throws.
        /// </summary>
        public static AssetValidationResult ValidateAsset(AssetKind kind, string path)
        {
            var r = new AssetValidationResult();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    r.Errors.Add(new AssetIssue("FILE_NOT_FOUND", $"File not found: {path ?? "(null)"}"));
                    return r;
                }

                switch (kind)
                {
                    case AssetKind.Palette:
                        ValidatePalette(path, r);
                        break;
                    case AssetKind.MapLayout:
                        ValidateMar(path, r);
                        break;
                    case AssetKind.Graphics:
                    case AssetKind.Portrait:
                    case AssetKind.Icon:
                        ValidatePng(kind, path, r);
                        break;
                    default:
                        r.Errors.Add(new AssetIssue("UNKNOWN_KIND", $"Unknown asset kind: {kind}"));
                        break;
                }
                return r;
            }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("VALIDATOR_FAULT", "Unexpected validator fault: " + ex.Message));
                return r;
            }
        }

        /// <summary>
        /// Map a kind string ("graphics"/"palette"/"portrait"/"icon"/"map"|"maplayout",
        /// case-insensitive) to an <see cref="AssetKind"/>; null when unrecognized.
        /// </summary>
        public static AssetKind? ParseKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "graphics": return AssetKind.Graphics;
                case "palette": return AssetKind.Palette;
                case "portrait": return AssetKind.Portrait;
                case "icon": return AssetKind.Icon;
                case "map":
                case "maplayout": return AssetKind.MapLayout;
                default: return null;
            }
        }

        // -------------------------------------------------------------- PNG kinds

        static void ValidatePng(AssetKind kind, string path, AssetValidationResult r)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read file: " + ex.Message));
                return;
            }

            IndexedPngInfo info = IndexedPngReader.Read(bytes);
            if (!info.Ok)
            {
                r.Errors.Add(new AssetIssue("BAD_PNG", "Not a valid PNG: " + info.Error));
                return;
            }

            // Color type must be 3 (indexed) for the decomp tile pipeline.
            if (info.ColorType != 3)
            {
                r.Errors.Add(new AssetIssue("NON_INDEXED",
                    $"PNG color type is {info.ColorType}; expected 3 (indexed/palette). Re-export as an indexed PNG."));
                // continue: still report dimension issues so the user fixes everything at once.
            }

            // Tile alignment.
            if (info.Width % 8 != 0 || info.Height % 8 != 0)
            {
                r.Errors.Add(new AssetIssue("NOT_TILE_ALIGNED",
                    $"Dimensions {info.Width}x{info.Height} are not multiples of 8 (tile-aligned)."));
            }

            // Palette size.
            if (info.PaletteColorCount > 256)
            {
                r.Errors.Add(new AssetIssue("PALETTE_TOO_LARGE",
                    $"Palette has {info.PaletteColorCount} colors; max 256 (GBA palettes are <= 256)."));
            }

            // Index-level checks (only when indices were recoverable).
            if (info.IndicesAvailable && info.Indices != null && info.Indices.Length > 0)
            {
                int maxIndex = 0;
                bool usesIndex0 = false;
                for (int i = 0; i < info.Indices.Length; i++)
                {
                    int idx = info.Indices[i];
                    if (idx > maxIndex) maxIndex = idx;
                    if (idx == 0) usesIndex0 = true;
                }

                if (info.PaletteColorCount > 0 && maxIndex >= info.PaletteColorCount)
                {
                    r.Errors.Add(new AssetIssue("INDEX_OUT_OF_RANGE",
                        $"Pixel index {maxIndex} >= palette size {info.PaletteColorCount}."));
                }

                // 4bpp-target warning: only when the palette indicates a single 16-color bank.
                if ((kind == AssetKind.Graphics || kind == AssetKind.Icon)
                    && info.PaletteColorCount > 0 && info.PaletteColorCount <= 16
                    && maxIndex >= 16)
                {
                    r.Warnings.Add(new AssetIssue("MAX_INDEX_GT_4BPP",
                        $"Pixel index {maxIndex} exceeds 15 but the palette is a single 16-color bank — a 4bpp import would clip it."));
                }

                // index-0 used but no tRNS: GBA convention is index 0 = transparent.
                if (usesIndex0 && !info.HasTrns)
                {
                    r.Warnings.Add(new AssetIssue("PALETTE_ORDER",
                        "Index 0 is used but there is no tRNS chunk — GBA convention treats palette index 0 as transparent."));
                }
            }
            else if (info.FiltersUnsupportedForIndexCheck)
            {
                r.Warnings.Add(new AssetIssue("FILTERS_UNSUPPORTED",
                    "PNG uses scanline filters other than None; index-level checks were skipped (structural checks still applied)."));
            }

            // Kind-specific dimension advisories.
            if (kind == AssetKind.Portrait)
            {
                if (!(info.Width == PortraitMainWidth && info.Height == PortraitMainHeight))
                {
                    r.Warnings.Add(new AssetIssue("PORTRAIT_DIMS",
                        $"Portrait is {info.Width}x{info.Height}; the main mug is {PortraitMainWidth}x{PortraitMainHeight}. Confirm this is an intended mouth/chibi strip."));
                }
            }
            else if (kind == AssetKind.Icon)
            {
                if (!(info.Width == IconSize && info.Height == IconSize))
                {
                    r.Warnings.Add(new AssetIssue("ICON_DIMS",
                        $"Icon is {info.Width}x{info.Height}; the standard icon is {IconSize}x{IconSize}."));
                }
            }
        }

        // -------------------------------------------------------------- Palette (.pal JASC)

        static void ValidatePalette(string path, AssetValidationResult r)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read file: " + ex.Message));
                return;
            }

            if (lines.Length < 3
                || lines[0].Trim() != "JASC-PAL"
                || lines[1].Trim() != "0100")
            {
                r.Errors.Add(new AssetIssue("BAD_PALETTE_HEADER",
                    "JASC palette must start with 'JASC-PAL' then '0100'."));
                return;
            }

            if (!int.TryParse(lines[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || count < 1 || count > 256)
            {
                r.Errors.Add(new AssetIssue("BAD_PALETTE_COUNT",
                    $"Palette color count '{lines[2].Trim()}' must be 1..256."));
                return;
            }

            int colorsParsed = 0;
            for (int li = 3; li < lines.Length && colorsParsed < count; li++)
            {
                string line = lines[li].Trim();
                if (line.Length == 0) continue;
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3
                    || !TryParseByte(parts[0], out _)
                    || !TryParseByte(parts[1], out _)
                    || !TryParseByte(parts[2], out _))
                {
                    r.Errors.Add(new AssetIssue("BAD_PALETTE_COLOR",
                        $"Line {li + 1}: '{line}' is not a valid 'R G B' triple (each 0..255)."));
                    return;
                }
                colorsParsed++;
            }

            if (colorsParsed < count)
            {
                r.Errors.Add(new AssetIssue("BAD_PALETTE_COUNT",
                    $"Header declares {count} colors but only {colorsParsed} 'R G B' lines were found."));
            }
        }

        static bool TryParseByte(string s, out int v)
        {
            v = 0;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return false;
            if (parsed < 0 || parsed > 255)
                return false;
            v = parsed;
            return true;
        }

        // -------------------------------------------------------------- MapLayout (.mar)

        static void ValidateMar(string path, AssetValidationResult r)
        {
            byte[] body;
            try { body = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read file: " + ex.Message));
                return;
            }

            // Look for the sidecar <path>.json (ExportMap writes "<file>.mar.json", i.e.
            // the .mar path with ".json" appended).
            string sidecar = path + ".json";
            bool haveSidecar = File.Exists(sidecar);

            int width = 0, height = 0;
            if (haveSidecar)
            {
                if (!TryReadSidecarDims(sidecar, out width, out height))
                {
                    r.Warnings.Add(new AssetIssue("MAR_NO_SIDECAR",
                        $"Sidecar '{Path.GetFileName(sidecar)}' present but its width/height could not be read — falling back to length-only checks."));
                    haveSidecar = false;
                }
            }
            else
            {
                r.Warnings.Add(new AssetIssue("MAR_NO_SIDECAR",
                    $"No sidecar '{Path.GetFileName(sidecar)}' found — only a length sanity check (even, multiple of 2) is applied."));
            }

            // Length must be even (a sequence of u16 entries).
            if (body.Length % 2 != 0)
            {
                r.Errors.Add(new AssetIssue("BAD_MAR_LENGTH",
                    $"File length {body.Length} is odd; a .mar is a flat array of u16 entries."));
                // can't check shift on a half-entry tail reliably; still continue with whole entries.
            }

            if (haveSidecar && width > 0 && height > 0)
            {
                int expected = width * height * 2;
                if (body.Length != expected)
                {
                    r.Errors.Add(new AssetIssue("BAD_MAR_LENGTH",
                        $"File length {body.Length} != width*height*2 ({width}*{height}*2 = {expected})."));
                }
            }

            // The <<3 invariant: every u16 entry's low 3 bits must be 0.
            int entries = body.Length / 2;
            for (int i = 0; i < entries; i++)
            {
                int u16 = body[i * 2] | (body[i * 2 + 1] << 8);
                if ((u16 & 0x7) != 0)
                {
                    r.Errors.Add(new AssetIssue("BAD_MAR_SHIFT",
                        $"Entry at byte offset {i * 2} (value 0x{u16:X4}) has non-zero low 3 bits — the <<3 .mar invariant is broken."));
                    break; // report the first offender only
                }
            }
        }

        /// <summary>Read width/height from a .mar.json sidecar. NEVER throws.</summary>
        static bool TryReadSidecarDims(string sidecar, out int width, out int height)
        {
            width = 0; height = 0;
            try
            {
                string json = File.ReadAllText(sidecar);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (root.TryGetProperty("width", out JsonElement w) && w.ValueKind == JsonValueKind.Number)
                    width = w.GetInt32();
                if (root.TryGetProperty("height", out JsonElement h) && h.ValueKind == JsonValueKind.Number)
                    height = h.GetInt32();
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
