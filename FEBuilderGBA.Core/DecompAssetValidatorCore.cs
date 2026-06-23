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
        /// <summary>
        /// Raw uncompressed u16 LE map-change overlay tile data block (#1355). NOT the .mar
        /// layout, NOT the change-record chain.
        /// </summary>
        MapChangeOverlay,
        /// <summary>
        /// Raw uncompressed u16 LE map tile-animation-2 PALETTE data block (#1360): a flat array
        /// of <c>count</c> 15-bit GBA colors reached by each anime-2 entry's <c>+0</c> pointer.
        /// NOT compressed, NOT the anime2 ENTRY/PLIST table.
        /// </summary>
        MapTileAnimation2Palette,
        /// <summary>
        /// A multi-file PORTRAIT PACKAGE directory (#1350): a single 128x112 composite
        /// portrait sheet PNG + an optional sidecar JASC .pal. Validated for sheet-slot
        /// geometry and palette consistency (PLTE-vs-JASC), NOT write-back / frame order.
        /// </summary>
        PortraitPackage,
        /// <summary>
        /// LZ77-decompressed 4bpp OBJ tile payload (#1371). The source body is the
        /// DECOMPRESSED bytes — NOT a byte-pinned LZ77 stream (FEBuilder's packer is
        /// non-canonical; the build re-compresses). Requires a sidecar
        /// <c>&lt;name&gt;.objtiles.json</c> with <c>"format": "febuilder-objtiles-lz77"</c>
        /// and <c>"length"</c>. NOT chipset TSA/config, NOT tile animations 1/2.
        /// </summary>
        ObjTiles,
        /// <summary>
        /// LZ77-decompressed map chipset TSA/CONFIG payload (#1375). The structural TWIN of
        /// <see cref="ObjTiles"/>: a single LZ77 stream reached by one dereferenced CONFIG-PLIST
        /// pointer (WF <c>ImageUtilMap.UnLZ77ChipsetData</c>). The source body is the DECOMPRESSED
        /// bytes — NOT a byte-pinned LZ77 stream (FEBuilder's packer is non-canonical; the build
        /// re-compresses). Requires a sidecar <c>&lt;name&gt;.mapchipconfig.json</c> with
        /// <c>"format": "febuilder-mapchipconfig-lz77"</c> and <c>"length"</c>. NOT the anime-1/anime-2
        /// entry tables, NOT the map-change record chain.
        /// </summary>
        MapChipConfig,
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

        // ----- Portrait composite SHEET geometry (#1350) -----
        // Canonical FE7/8 portrait composite sheet is 128x112 indexed. The slot coords
        // are cited from PortraitImportPreviewCore.cs (:60-92) and
        // PortraitRendererCore.SplitPortraitSheet (:588-602); they are NOT invented here.

        /// <summary>Full FE7/8 portrait composite sheet width (px).</summary>
        public const int SheetWidth = 128;
        /// <summary>Full FE7/8 portrait composite sheet height (px).</summary>
        public const int SheetHeight = 112;

        /// <summary>Mini/chibi face slot inside the sheet: (96,16) 32x32.</summary>
        public const int MiniX = 96, MiniY = 16, MiniW = 32, MiniH = 32;
        /// <summary>Half-closed eyes slot inside the sheet: (96,48) 32x16.</summary>
        public const int EyeHalfX = 96, EyeHalfY = 48, EyeHalfW = 32, EyeHalfH = 16;
        /// <summary>Closed eyes slot inside the sheet: (96,64) 32x16.</summary>
        public const int EyeClosedX = 96, EyeClosedY = 64, EyeClosedW = 32, EyeClosedH = 16;

        /// <summary>
        /// The 7 mouth slots (each 32x16) inside the sheet:
        /// (0,80)(32,80)(64,80)(96,80)(0,96)(32,96)(64,96).
        /// </summary>
        static readonly (int x, int y, int w, int h)[] MouthSlots =
        {
            (0, 80, 32, 16), (32, 80, 32, 16), (64, 80, 32, 16), (96, 80, 32, 16),
            (0, 96, 32, 16), (32, 96, 32, 16), (64, 96, 32, 16),
        };

        /// <summary>GBA portrait colour cap — a portrait tile is 4bpp (a single 16-color bank).</summary>
        public const int PortraitPaletteMax = 16;

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
                    case AssetKind.MapChangeOverlay:
                        ValidateMapChange(path, r);
                        break;
                    case AssetKind.MapTileAnimation2Palette:
                        ValidateMapAnime2Pal(path, r);
                        break;
                    case AssetKind.ObjTiles:
                        ValidateObjTiles(path, r);
                        break;
                    case AssetKind.MapChipConfig:
                        ValidateMapChipConfig(path, r);
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
        /// Validate a multi-file PORTRAIT PACKAGE directory (#1350). The only supported
        /// <paramref name="kind"/> is <see cref="AssetKind.PortraitPackage"/>. The directory
        /// must contain exactly one composite sheet PNG (128x112 full package, or 96x80
        /// main-mug-only) plus an optional sidecar JASC <c>.pal</c>; this entry point checks
        /// the sheet's structure (reusing <see cref="ValidatePng"/>), the 128x112 slot
        /// geometry, the 4bpp palette cap, and PLTE-vs-JASC palette consistency.
        ///
        /// When <paramref name="allowMainOnly"/> is true a 96x80 main-only sheet downgrades
        /// the <c>INCOMPLETE_PACKAGE</c> error to a warning (the package is intentionally
        /// just the main mug). NEVER reads the ROM, NEVER mutates anything, NEVER throws —
        /// every fault becomes a <c>VALIDATOR_FAULT</c> error.
        /// </summary>
        public static AssetValidationResult ValidateAssetPackage(AssetKind kind, string dirPath, bool allowMainOnly = false)
        {
            var r = new AssetValidationResult();
            try
            {
                if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
                {
                    r.Errors.Add(new AssetIssue("DIR_NOT_FOUND", $"Directory not found: {dirPath ?? "(null)"}"));
                    return r;
                }

                if (kind != AssetKind.PortraitPackage)
                {
                    r.Errors.Add(new AssetIssue("UNKNOWN_KIND", $"ValidateAssetPackage only supports portrait-package; got: {kind}"));
                    return r;
                }

                ValidatePortraitPackage(dirPath, allowMainOnly, r);
                return r;
            }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("VALIDATOR_FAULT", "Unexpected validator fault: " + ex.Message));
                return r;
            }
        }

        /// <summary>
        /// Validate a portrait-package directory: enumerate the single sheet PNG, reuse the
        /// shared <see cref="ValidatePng"/> structural checks (packageMode — no single-mug
        /// dims advisory), then add package-specific 128x112 slot-geometry / palette-cap /
        /// sidecar-palette-consistency checks. Appends every finding to <paramref name="r"/>.
        /// </summary>
        static void ValidatePortraitPackage(string dirPath, bool allowMainOnly, AssetValidationResult r)
        {
            // 1) Find the single sheet PNG. Sort deterministically so a multi-PNG dir picks a
            // stable "first" sheet for full diagnostics rather than a filesystem-order one.
            string[] pngs = Directory.GetFiles(dirPath, "*.png");
            Array.Sort(pngs, StringComparer.Ordinal);
            if (pngs.Length == 0)
            {
                r.Errors.Add(new AssetIssue("MISSING_SHEET",
                    $"No *.png sheet found in '{dirPath}'. A portrait package needs one composite sheet PNG."));
                return;
            }
            if (pngs.Length > 1)
            {
                // Report the ambiguity but still validate the first (sorted) sheet so the user
                // gets full diagnostics in one run — they then know which extra files to remove.
                r.Errors.Add(new AssetIssue("MULTIPLE_SHEETS",
                    $"Found {pngs.Length} *.png files; a portrait package needs exactly one. Validating '{Path.GetFileName(pngs[0])}' (first, sorted)."));
            }
            string sheetPath = pngs[0];

            // 2) Reuse the shared PNG structural / index-level checks (colorType 3, tile-align,
            // palette size, index range, index-0 transparency). packageMode skips the single-mug
            // PORTRAIT_DIMS advisory because a package sheet is the larger 128x112 composite.
            ValidatePng(AssetKind.Portrait, sheetPath, r, packageMode: true);

            // Re-read the parsed PNG info for the geometry/palette checks (file already read &
            // validated above — this is a cheap second parse, never reads the ROM).
            byte[] bytes;
            try { bytes = File.ReadAllBytes(sheetPath); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read sheet: " + ex.Message));
                return;
            }
            IndexedPngInfo info = IndexedPngReader.Read(bytes);
            if (!info.Ok)
                return; // ValidatePng already reported BAD_PNG; nothing further to geometry-check.

            // 3) Sheet dimensions / slot geometry.
            int w = info.Width, h = info.Height;
            if (w == SheetWidth && h == SheetHeight)
            {
                // Full package — exactly the canonical composite. OK.
            }
            else if (w == PortraitMainWidth && h == PortraitMainHeight)
            {
                // Main-mug-only (96x80): an INCOMPLETE package. Error by default; downgrade to
                // a warning when the caller explicitly allows a main-only package.
                if (allowMainOnly)
                {
                    r.Warnings.Add(new AssetIssue("INCOMPLETE_PACKAGE",
                        $"Sheet is {w}x{h} (main mug only) — mini/eye/mouth slots are absent. Accepted because --allow-main-only was set."));
                }
                else
                {
                    r.Errors.Add(new AssetIssue("INCOMPLETE_PACKAGE",
                        $"Sheet is {w}x{h} (main mug only); the full package sheet is {SheetWidth}x{SheetHeight}. Pass allow-main-only to accept a main-mug-only package."));
                }
            }
            else
            {
                // Neither canonical size. Check whether every required slot still fits; if so
                // it's a non-canonical WARN, otherwise the out-of-bounds slots are errors.
                int oob = CheckSlotBounds(w, h, r);
                if (oob == 0)
                {
                    r.Warnings.Add(new AssetIssue("SHEET_BAD_DIMS",
                        $"Sheet is {w}x{h}; expected the full {SheetWidth}x{SheetHeight} package (or {PortraitMainWidth}x{PortraitMainHeight} main-only). All slots fit but the dimensions are non-canonical."));
                }
                // else: CheckSlotBounds already emitted SHEET_TOO_SMALL per out-of-bounds slot.
            }

            // 4) Portrait palette bit-depth: a GBA portrait tile is 4bpp (a single 16-color bank).
            if (info.PaletteColorCount > PortraitPaletteMax)
            {
                r.Warnings.Add(new AssetIssue("PORTRAIT_PALETTE_GT16",
                    $"Sheet palette has {info.PaletteColorCount} colors; a GBA portrait is 4bpp (max {PortraitPaletteMax}). Indices >15 would be clipped on import."));
            }

            // 5) Sidecar palette: optional JASC .pal. The sidecar that BELONGS to this sheet
            // is the one whose name matches the sheet (sheet.png -> sheet.pal); picking the
            // first sorted *.pal could compare the sheet PLTE against an unrelated palette and
            // emit false PALETTE_* mismatches (Copilot PR #1353 review). Match by sheet name;
            // any OTHER *.pal in the dir is reported as an extra sidecar (informational warn).
            string expectedPal = Path.ChangeExtension(sheetPath, ".pal");
            string[] pals = Directory.GetFiles(dirPath, "*.pal");
            Array.Sort(pals, StringComparer.Ordinal);
            string matchedPal = null;
            foreach (string p in pals)
            {
                if (string.Equals(Path.GetFullPath(p), Path.GetFullPath(expectedPal),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    matchedPal = p;
                    break;
                }
            }

            if (matchedPal == null)
            {
                // No sidecar matching the sheet name. The sheet's embedded PLTE was checked by
                // ValidatePng above (when the PNG was indexed); a JASC sidecar matching the
                // sheet is still recommended for decomp builds. Keep the wording generic — for
                // a NON_INDEXED / invalid-PLTE sheet the embedded palette may not have validated.
                r.Warnings.Add(new AssetIssue("MISSING_PALETTE",
                    $"No sidecar '{Path.GetFileName(expectedPal)}' found in '{dirPath}'. A JASC sidecar matching the sheet name is recommended for decomp builds."));
                // Surface any unrelated *.pal so the user knows it was NOT used for comparison.
                if (pals.Length > 0)
                {
                    r.Warnings.Add(new AssetIssue("EXTRA_PALETTE",
                        $"Found {pals.Length} *.pal file(s) but none match the sheet name '{Path.GetFileNameWithoutExtension(sheetPath)}.pal'; palette consistency was NOT checked."));
                }
            }
            else
            {
                ValidatePalette(matchedPal, r);
                ComparePaletteConsistency(matchedPal, info, r);
                // Note any additional sidecars that are not the sheet's own.
                if (pals.Length > 1)
                {
                    r.Warnings.Add(new AssetIssue("EXTRA_PALETTE",
                        $"Found {pals.Length} *.pal file(s); only '{Path.GetFileName(matchedPal)}' (matching the sheet) was used for palette consistency."));
                }
            }
        }

        /// <summary>
        /// Verify every required portrait slot (mini, half/closed eyes, the 7 mouths) lies
        /// fully within a <paramref name="w"/>x<paramref name="h"/> sheet. Emits a
        /// <c>SHEET_TOO_SMALL</c> error per out-of-bounds slot (naming the slot + bound) and
        /// returns the number of out-of-bounds slots (0 = all fit).
        /// </summary>
        static int CheckSlotBounds(int w, int h, AssetValidationResult r)
        {
            int oob = 0;
            void Check(string name, int x, int y, int sw, int sh)
            {
                if (x + sw > w || y + sh > h)
                {
                    oob++;
                    r.Errors.Add(new AssetIssue("SHEET_TOO_SMALL",
                        $"Slot '{name}' ({x},{y} {sw}x{sh}) exceeds the {w}x{h} sheet (needs {x + sw}x{y + sh})."));
                }
            }
            Check("mini", MiniX, MiniY, MiniW, MiniH);
            Check("eyeHalf", EyeHalfX, EyeHalfY, EyeHalfW, EyeHalfH);
            Check("eyeClosed", EyeClosedX, EyeClosedY, EyeClosedW, EyeClosedH);
            for (int i = 0; i < MouthSlots.Length; i++)
            {
                var (mx, my, mw, mh) = MouthSlots[i];
                Check($"mouth{i + 1}", mx, my, mw, mh);
            }
            return oob;
        }

        /// <summary>
        /// Compare a sidecar JASC palette against the sheet PNG's embedded PLTE (#1350). The
        /// PLTE is stored R,G,B (PNG order) and JASC is also <c>R G B</c>, so they compare
        /// directly. A differing entry COUNT → <c>PALETTE_COUNT_MISMATCH</c>; same count but
        /// any differing triple → <c>PALETTE_COLOR_MISMATCH</c> (reporting the first differing
        /// index + both values). Skipped silently when the PLTE was not recovered or the JASC
        /// could not be parsed (the structural checks already errored). NEVER throws.
        /// </summary>
        static void ComparePaletteConsistency(string palPath, IndexedPngInfo info, AssetValidationResult r)
        {
            if (info.PaletteRgb == null || info.PaletteRgb.Length == 0)
                return; // no PLTE recovered — structural check already errored, skip comparison.
            if (!TryParseJascColors(palPath, out List<(int r, int g, int b)> jasc))
                return; // malformed JASC — ValidatePalette already errored.

            int plteCount = info.PaletteRgb.Length / 3;
            if (jasc.Count != plteCount)
            {
                r.Errors.Add(new AssetIssue("PALETTE_COUNT_MISMATCH",
                    $"Sidecar palette has {jasc.Count} colors but the sheet PLTE has {plteCount}."));
                return;
            }

            for (int i = 0; i < plteCount; i++)
            {
                int pr = info.PaletteRgb[i * 3 + 0];
                int pg = info.PaletteRgb[i * 3 + 1];
                int pb = info.PaletteRgb[i * 3 + 2];
                if (pr != jasc[i].r || pg != jasc[i].g || pb != jasc[i].b)
                {
                    r.Errors.Add(new AssetIssue("PALETTE_COLOR_MISMATCH",
                        $"Palette entry {i} differs: sidecar ({jasc[i].r} {jasc[i].g} {jasc[i].b}) vs sheet PLTE ({pr} {pg} {pb})."));
                    return; // report the first offender only.
                }
            }
        }

        /// <summary>
        /// Map a kind string ("graphics"/"palette"/"portrait"/"icon"/"map"|"maplayout"/
        /// "mapchange"|"mapchange-overlay"/"mapanime2pal"|"map-tileanime2-palette"/
        /// "portrait-package"|"portraitpackage", case-insensitive) to an
        /// <see cref="AssetKind"/>; null when unrecognized.
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
                case "mapchange":
                case "mapchange-overlay": return AssetKind.MapChangeOverlay;
                case "mapanime2pal":
                case "map-tileanime2-palette": return AssetKind.MapTileAnimation2Palette;
                case "portrait-package":
                case "portraitpackage": return AssetKind.PortraitPackage;
                case "objtiles":
                case "obj-tiles":
                case "obj": return AssetKind.ObjTiles;
                case "mapchipconfig":
                case "mapchip-config":
                case "chipconfig": return AssetKind.MapChipConfig;
                default: return null;
            }
        }

        // -------------------------------------------------------------- PNG kinds

        /// <summary>
        /// Structural + index-level checks shared by all PNG kinds. When
        /// <paramref name="packageMode"/> is true the <c>PORTRAIT_DIMS</c>/<c>ICON_DIMS</c>
        /// single-mug advisories are skipped — a portrait PACKAGE sheet is the larger
        /// 128x112 composite, so the 96x80 single-mug advisory does not apply (#1350).
        /// </summary>
        static void ValidatePng(AssetKind kind, string path, AssetValidationResult r, bool packageMode = false)
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

                // 4bpp-target warning (Finding #3): a GBA 4bpp tile uses a single 16-color
                // bank. The intended case is a PNG whose PLTE has MORE than 16 colors (so
                // INDEX_OUT_OF_RANGE does NOT fire) but that USES an index >= 16 — a 4bpp
                // import would clip it to the first bank. The previous PaletteColorCount<=16
                // condition was unreachable (any maxIndex>=16 with <=16 colors already fires
                // INDEX_OUT_OF_RANGE). So warn when the index is valid for the PLTE
                // (maxIndex < PaletteColorCount) but exceeds a single 4bpp bank.
                if ((kind == AssetKind.Graphics || kind == AssetKind.Icon)
                    && info.PaletteColorCount > 16
                    && maxIndex >= 16 && maxIndex < info.PaletteColorCount)
                {
                    r.Warnings.Add(new AssetIssue("MAX_INDEX_GT_4BPP",
                        $"Pixel index {maxIndex} exceeds a 16-color bank but the palette has {info.PaletteColorCount} colors — a 4bpp import would clip indices >15."));
                }

                // index-0 used: GBA convention treats palette index 0 as transparent
                // (Finding #4). Warn when there is no tRNS at all, OR when tRNS is present
                // but does NOT mark index 0 as fully transparent (transparency is on a
                // different index / index 0 is opaque).
                if (usesIndex0 && (!info.HasTrns || !IsTransparentIndex(info, 0)))
                {
                    r.Warnings.Add(new AssetIssue("PALETTE_ORDER",
                        !info.HasTrns
                            ? "Index 0 is used but there is no tRNS chunk — GBA convention treats palette index 0 as transparent."
                            : "Index 0 is used but the tRNS chunk does not mark index 0 as fully transparent — GBA convention treats palette index 0 as transparent."));
                }
            }
            else if (info.FiltersUnsupportedForIndexCheck)
            {
                r.Warnings.Add(new AssetIssue("FILTERS_UNSUPPORTED",
                    "PNG uses scanline filters other than None; index-level checks were skipped (structural checks still applied)."));
            }

            // Kind-specific dimension advisories. In packageMode these single-asset advisories
            // are suppressed — a portrait PACKAGE sheet is the 128x112 composite, not a 96x80 mug.
            if (!packageMode && kind == AssetKind.Portrait)
            {
                if (!(info.Width == PortraitMainWidth && info.Height == PortraitMainHeight))
                {
                    r.Warnings.Add(new AssetIssue("PORTRAIT_DIMS",
                        $"Portrait is {info.Width}x{info.Height}; the main mug is {PortraitMainWidth}x{PortraitMainHeight}. Confirm this is an intended mouth/chibi strip."));
                }
            }
            else if (!packageMode && kind == AssetKind.Icon)
            {
                if (!(info.Width == IconSize && info.Height == IconSize))
                {
                    r.Warnings.Add(new AssetIssue("ICON_DIMS",
                        $"Icon is {info.Width}x{info.Height}; the standard icon is {IconSize}x{IconSize}."));
                }
            }
        }

        /// <summary>
        /// True when <paramref name="index"/> is in the PNG's fully-transparent set
        /// (a tRNS alpha byte == 0 for that index). Used to verify index 0 is actually
        /// marked transparent (Finding #4). Indices beyond the tRNS length are opaque.
        /// </summary>
        static bool IsTransparentIndex(IndexedPngInfo info, int index)
        {
            if (info?.TransparentIndices == null) return false;
            foreach (int i in info.TransparentIndices)
                if (i == index) return true;
            return false;
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

        /// <summary>
        /// Best-effort JASC palette colour parse (#1350): reads up to the declared count of
        /// <c>R G B</c> triples after the 3-line header. Returns false (and an empty list)
        /// on a malformed header / count; otherwise true with whatever valid triples were
        /// found (a bad triple stops the scan but does not fault). Used by the package
        /// validator for PLTE-vs-JASC consistency — it does NOT replace
        /// <see cref="ValidatePalette"/>'s structural error reporting. NEVER throws.
        /// </summary>
        static bool TryParseJascColors(string path, out List<(int r, int g, int b)> colors)
        {
            colors = new List<(int, int, int)>();
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length < 3
                    || lines[0].Trim() != "JASC-PAL"
                    || lines[1].Trim() != "0100")
                    return false;
                if (!int.TryParse(lines[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                    || count < 1 || count > 256)
                    return false;

                for (int li = 3; li < lines.Length && colors.Count < count; li++)
                {
                    string line = lines[li].Trim();
                    if (line.Length == 0) continue;
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3
                        || !TryParseByte(parts[0], out int rr)
                        || !TryParseByte(parts[1], out int gg)
                        || !TryParseByte(parts[2], out int bb))
                    {
                        // A malformed triple means the JASC file is structurally invalid (and
                        // ValidatePalette already errored on it). Return FALSE so the caller
                        // SKIPS the consistency comparison rather than emitting a misleading
                        // PALETTE_COUNT_MISMATCH/PALETTE_COLOR_MISMATCH on a partial color list
                        // (Copilot PR #1353 review).
                        colors = new List<(int, int, int)>();
                        return false;
                    }
                    colors.Add((rr, gg, bb));
                }

                // Fewer triples than the header declared => also structurally invalid (the
                // declared count was not satisfied). Skip the comparison for the same reason.
                if (colors.Count < count)
                {
                    colors = new List<(int, int, int)>();
                    return false;
                }
                return true;
            }
            catch
            {
                colors = new List<(int, int, int)>();
                return false;
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

        // ------------------------------------------------ MapChangeOverlay (raw u16 LE)

        /// <summary>
        /// Validate a RAW UNCOMPRESSED map-change OVERLAY tile data block (#1355) — a flat
        /// array of <c>width*height</c> u16 LE config-descriptor indices. Unlike <see cref="ValidateMar"/>
        /// there is NO <c>&lt;&lt;3</c> low-3-bits invariant (overlay indices are raw u16, any value
        /// is valid) and almost no intrinsic structure, so the sidecar JSON is REQUIRED (it carries
        /// the dimensions AND the format declaration). Checks:
        /// <list type="bullet">
        ///   <item>sidecar <c>&lt;name&gt;.json</c> present and declares <c>format ==
        ///   "febuilder-mapchange-u16"</c> — else <c>MAPCHANGE_NO_SIDECAR</c>/<c>BAD_MAPCHANGE_FORMAT</c>;</item>
        ///   <item>dimensions readable and in u8 range 1..255 — else <c>BAD_MAPCHANGE_DIMS</c>;</item>
        ///   <item>body length even AND equal to <c>width*height*2</c> — else <c>BAD_MAPCHANGE_LENGTH</c>.</item>
        /// </list>
        /// NEVER reads the ROM, NEVER throws.
        /// </summary>
        static void ValidateMapChange(string path, AssetValidationResult r)
        {
            byte[] body;
            try { body = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read file: " + ex.Message));
                return;
            }

            // The overlay body has almost no intrinsic invariant, so the sidecar is REQUIRED
            // (it carries both the dimensions and the format declaration).
            string sidecar = path + ".json";
            if (!File.Exists(sidecar))
            {
                r.Errors.Add(new AssetIssue("MAPCHANGE_NO_SIDECAR",
                    $"Sidecar '{Path.GetFileName(sidecar)}' is required for a map-change overlay (it carries width/height and the format declaration)."));
                return;
            }

            // The sidecar MUST declare format == "febuilder-mapchange-u16".
            if (!TryReadSidecarFormat(sidecar, out string format)
                || !string.Equals(format, "febuilder-mapchange-u16", StringComparison.Ordinal))
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHANGE_FORMAT",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare format \"febuilder-mapchange-u16\"; got \"{format ?? "(missing)"}\"."));
                return;
            }

            // Dimensions are required and must fit u8 (1..255).
            if (!TryReadSidecarDims(sidecar, out int width, out int height))
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHANGE_DIMS",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare positive integer width/height."));
                return;
            }
            if (width < 1 || width > 255 || height < 1 || height > 255)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHANGE_DIMS",
                    $"Dimensions {width}x{height} are out of the u8 range 1..255."));
                return;
            }

            // Length must be even (a sequence of u16 entries).
            if (body.Length % 2 != 0)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHANGE_LENGTH",
                    $"File length {body.Length} is odd; a map-change overlay is a flat array of u16 entries."));
                return;
            }

            // Length must equal width*height*2 exactly.
            int expected = width * height * 2;
            if (body.Length != expected)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHANGE_LENGTH",
                    $"File length {body.Length} != width*height*2 ({width}*{height}*2 = {expected})."));
            }
        }

        // ------------------------------------------------ ObjTiles (LZ77-decompressed 4bpp payload)

        /// <summary>
        /// Validate a LZ77-decompressed 4bpp OBJ tile payload (#1371). The body is the DECOMPRESSED
        /// bytes — NOT a byte-pinned LZ77 stream. The sidecar JSON is REQUIRED (it carries the
        /// format declaration and the length). Checks:
        /// <list type="bullet">
        ///   <item>sidecar present and declares <c>format == "febuilder-objtiles-lz77"</c>;</item>
        ///   <item>sidecar declares a positive <c>length</c>;</item>
        ///   <item>body length equals sidecar length.</item>
        /// </list>
        /// NEVER reads the ROM, NEVER throws.
        /// </summary>
        static void ValidateObjTiles(string path, AssetValidationResult r)
        {
            byte[] body;
            try { body = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read file: " + ex.Message));
                return;
            }

            string sidecar = path + ".json";
            if (!File.Exists(sidecar))
            {
                r.Errors.Add(new AssetIssue("OBJTILES_NO_SIDECAR",
                    $"Sidecar '{Path.GetFileName(sidecar)}' is required for an OBJ tileset asset (it carries length and the format declaration)."));
                return;
            }

            if (!TryReadSidecarFormat(sidecar, out string format)
                || !string.Equals(format, "febuilder-objtiles-lz77", StringComparison.Ordinal))
            {
                r.Errors.Add(new AssetIssue("BAD_OBJTILES_FORMAT",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare format \"febuilder-objtiles-lz77\"; got \"{format ?? "(missing)"}\"."));
                return;
            }

            // Read the required length from the sidecar.
            int len = 0;
            try
            {
                string json = File.ReadAllText(sidecar);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("length", out JsonElement lProp)
                    && lProp.ValueKind == JsonValueKind.Number)
                    len = lProp.GetInt32();
            }
            catch { }

            if (len <= 0)
            {
                r.Errors.Add(new AssetIssue("BAD_OBJTILES_LENGTH",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare a positive integer 'length'."));
                return;
            }

            if (body.Length != len)
            {
                r.Errors.Add(new AssetIssue("BAD_OBJTILES_LENGTH",
                    $"File length {body.Length} != sidecar length {len}."));
            }
        }

        // ------------------------------ MapChipConfig (LZ77-decompressed chipset config payload, #1375)

        /// <summary>
        /// Validate a LZ77-decompressed map chipset TSA/CONFIG payload (#1375). The structural TWIN
        /// of <see cref="ValidateObjTiles"/>: the body is the DECOMPRESSED bytes — NOT a byte-pinned
        /// LZ77 stream. The sidecar JSON is REQUIRED (it carries the format declaration and the
        /// length). Checks:
        /// <list type="bullet">
        ///   <item>sidecar present and declares <c>format == "febuilder-mapchipconfig-lz77"</c>;</item>
        ///   <item>sidecar declares a positive <c>length</c>;</item>
        ///   <item>body length equals sidecar length.</item>
        /// </list>
        /// NEVER reads the ROM, NEVER throws.
        /// </summary>
        static void ValidateMapChipConfig(string path, AssetValidationResult r)
        {
            byte[] body;
            try { body = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read file: " + ex.Message));
                return;
            }

            string sidecar = path + ".json";
            if (!File.Exists(sidecar))
            {
                r.Errors.Add(new AssetIssue("MAPCHIPCONFIG_NO_SIDECAR",
                    $"Sidecar '{Path.GetFileName(sidecar)}' is required for a map chipset config asset (it carries length and the format declaration)."));
                return;
            }

            if (!TryReadSidecarFormat(sidecar, out string format)
                || !string.Equals(format, "febuilder-mapchipconfig-lz77", StringComparison.Ordinal))
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHIPCONFIG_FORMAT",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare format \"febuilder-mapchipconfig-lz77\"; got \"{format ?? "(missing)"}\"."));
                return;
            }

            // Read the required length from the sidecar.
            int len = 0;
            try
            {
                string json = File.ReadAllText(sidecar);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("length", out JsonElement lProp)
                    && lProp.ValueKind == JsonValueKind.Number)
                    len = lProp.GetInt32();
            }
            catch { }

            if (len <= 0)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHIPCONFIG_LENGTH",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare a positive integer 'length'."));
                return;
            }

            if (body.Length != len)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPCHIPCONFIG_LENGTH",
                    $"File length {body.Length} != sidecar length {len}."));
            }
        }

        /// <summary>
        /// Read the root-object <c>format</c> string property from a sidecar JSON. NEVER throws;
        /// returns false (and <paramref name="format"/> = null) on any fault or a missing/non-string
        /// property. Mirrors <see cref="TryReadSidecarDims"/>'s shape.
        /// </summary>
        static bool TryReadSidecarFormat(string sidecar, out string format)
        {
            format = null;
            try
            {
                string json = File.ReadAllText(sidecar);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (root.TryGetProperty("format", out JsonElement f) && f.ValueKind == JsonValueKind.String)
                {
                    format = f.GetString();
                    return format != null;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------ MapTileAnimation2Palette (raw u16 LE, #1360)

        /// <summary>
        /// Validate a map tile-animation-2 PALETTE block (#1360) — the structural TWIN of
        /// <see cref="ValidateMapChange"/> with a single <c>count</c> descriptor instead of
        /// width/height:
        /// <list type="bullet">
        ///   <item>sidecar <c>&lt;name&gt;.json</c> present and declares <c>format ==
        ///   "febuilder-mapanime2-pal-u16"</c> — else <c>MAPANIME2PAL_NO_SIDECAR</c>/<c>BAD_MAPANIME2PAL_FORMAT</c>;</item>
        ///   <item>count readable and in u8 range 1..255 — else <c>BAD_MAPANIME2PAL_COUNT</c>;</item>
        ///   <item>body length even AND equal to <c>count*2</c> — else <c>BAD_MAPANIME2PAL_LENGTH</c>.</item>
        /// </list>
        /// NEVER reads the ROM, NEVER throws.
        ///
        /// NOTE: rejecting <c>count == 0</c> is an INTENTIONAL guard for a MEANINGFUL source asset —
        /// the underlying ROM helpers CAN enumerate a zero-count empty palette list, so a 0 count is a
        /// valid data layout but NOT a useful source export. It is NOT a data-layout fact.
        /// </summary>
        static void ValidateMapAnime2Pal(string path, AssetValidationResult r)
        {
            byte[] body;
            try { body = File.ReadAllBytes(path); }
            catch (Exception ex)
            {
                r.Errors.Add(new AssetIssue("READ_FAILED", "Could not read file: " + ex.Message));
                return;
            }

            // The palette body has almost no intrinsic invariant, so the sidecar is REQUIRED
            // (it carries both the count and the format declaration).
            string sidecar = path + ".json";
            if (!File.Exists(sidecar))
            {
                r.Errors.Add(new AssetIssue("MAPANIME2PAL_NO_SIDECAR",
                    $"Sidecar '{Path.GetFileName(sidecar)}' is required for a map tile-animation-2 palette (it carries count and the format declaration)."));
                return;
            }

            // The sidecar MUST declare format == "febuilder-mapanime2-pal-u16".
            if (!TryReadSidecarFormat(sidecar, out string format)
                || !string.Equals(format, "febuilder-mapanime2-pal-u16", StringComparison.Ordinal))
            {
                r.Errors.Add(new AssetIssue("BAD_MAPANIME2PAL_FORMAT",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare format \"febuilder-mapanime2-pal-u16\"; got \"{format ?? "(missing)"}\"."));
                return;
            }

            // Count is required and must fit u8 (1..255). count==0 is rejected on purpose (see
            // the method note): an empty palette is a valid ROM layout but not a meaningful source asset.
            if (!TryReadSidecarCount(sidecar, out int count))
            {
                r.Errors.Add(new AssetIssue("BAD_MAPANIME2PAL_COUNT",
                    $"Sidecar '{Path.GetFileName(sidecar)}' must declare a positive integer count."));
                return;
            }
            if (count < 1 || count > 255)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPANIME2PAL_COUNT",
                    $"Count {count} is out of the u8 range 1..255."));
                return;
            }

            // Length must be even (a sequence of u16 colors).
            if (body.Length % 2 != 0)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPANIME2PAL_LENGTH",
                    $"File length {body.Length} is odd; a map tile-animation-2 palette is a flat array of u16 colors."));
                return;
            }

            // Length must equal count*2 exactly.
            int expected = count * 2;
            if (body.Length != expected)
            {
                r.Errors.Add(new AssetIssue("BAD_MAPANIME2PAL_LENGTH",
                    $"File length {body.Length} != count*2 ({count}*2 = {expected})."));
            }
        }

        /// <summary>
        /// Read the root-object <c>count</c> Number property from a sidecar JSON. NEVER throws;
        /// returns false (and <paramref name="count"/> = 0) on any fault or a missing/non-positive
        /// property. Mirrors <see cref="TryReadSidecarDims"/>'s shape (#1360).
        /// </summary>
        static bool TryReadSidecarCount(string sidecar, out int count)
        {
            count = 0;
            try
            {
                string json = File.ReadAllText(sidecar);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                if (root.TryGetProperty("count", out JsonElement c) && c.ValueKind == JsonValueKind.Number)
                    count = c.GetInt32();
                return count > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
