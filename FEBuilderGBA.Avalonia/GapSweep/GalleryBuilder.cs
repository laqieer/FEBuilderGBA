// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374) — Phase 3: side-by-side screenshot gallery.
//
// Phase 1 (density) and Phase 2 (labels) produce QUANTITATIVE and QUALITATIVE
// signals from static analysis. Phase 3 is the VISUAL follow-up: drive both
// apps' existing `--screenshot-all` flags against the same ROM and produce a
// side-by-side Markdown gallery so layout / control-omission / portrait /
// font-rendering deltas surface alongside the density and label signals.
//
// The two existing runners use parallel filenames:
//
//   WinForms (FEBuilderGBA/ScreenshotAllRunner.cs):
//     WinForms_{ViewName}_{RomVersion}.png       e.g. WinForms_UnitEditorView_FE8U.png
//   Avalonia (FEBuilderGBA.Avalonia/Views/MainWindow.axaml.cs:RunScreenshotAll):
//     Avalonia_{ViewName}_{RomVersion}.png       e.g. Avalonia_UnitEditorView_FE8U.png
//
// Crucially, ScreenshotFormRegistry uses Avalonia view-names as keys, so the
// inner `{ViewName}` segment is IDENTICAL on both sides. Pairing is therefore
// a simple "strip the literal prefix `WinForms_` / `Avalonia_`, strip the
// literal trailing `_{RomTag}.png`, match by what remains" exercise. This
// scanner does NOT split on `_` because some valid view names contain
// underscores (e.g. `ToolWorkSupport_SelectUPSView` — flagged by Copilot
// during Phase 3 plan review).
//
// Asymmetric captures are categorised explicitly:
//   - `Pairs`               — both sides captured (the side-by-side table)
//   - `AvOnly`              — only Avalonia captured (likely no WinForms
//                             counterpart in ScreenshotFormRegistry)
//   - `WfOnly`              — only WinForms captured (likely AV editor missing
//                             from GetAllEditorFactories or the AV capture
//                             failed silently)
//   - `MissingFromExpected` — expected editors that neither side captured;
//                             expected list is parsed from
//                             `docs/avalonia-gui-forms.md` so the gallery
//                             cross-checks against the project's own coverage
//                             tracker.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FEBuilderGBA.Avalonia.GapSweep
{
    /// <summary>
    /// One row of the side-by-side gallery. Either image path may be null when
    /// only one side has a capture — the markdown formatter renders "—" in that
    /// case. Paths are kept absolute internally and made relative when the
    /// markdown is emitted (so the report renders correctly from
    /// `docs/avalonia-gaps/<date>-screenshots/<rom>/index.md` regardless of the
    /// absolute path the scanner saw at generation time).
    /// </summary>
    public record GalleryEntry(string EditorName, string? WfImagePath, string? AvImagePath);

    /// <summary>
    /// The classified output of one gallery build. Phase 3 reports each list
    /// in its own section so reviewers can immediately see which editors have
    /// asymmetric capture coverage.
    /// </summary>
    public record GalleryReport(
        string RomTag,
        IReadOnlyList<GalleryEntry> Pairs,
        IReadOnlyList<string> AvOnly,
        IReadOnlyList<string> WfOnly,
        IReadOnlyList<string> MissingFromExpected);

    /// <summary>
    /// Pair PNG screenshots emitted by the two `--screenshot-all` runners and
    /// produce a side-by-side Markdown gallery. Pure file-system + string
    /// transforms; no Avalonia runtime, no reflection.
    /// </summary>
    public static class GalleryBuilder
    {
        const string WfPrefix = "WinForms_";
        const string AvPrefix = "Avalonia_";

        /// <summary>
        /// Enumerate `*.png` in <paramref name="wfDir"/> and <paramref name="avDir"/>,
        /// pair by normalised basename, and classify the result.
        ///
        /// <paramref name="romTag"/> is the trailing suffix that gets stripped from
        /// each filename to recover the bare editor name (e.g. "FE8U" turns
        /// "WinForms_UnitEditorView_FE8U.png" into "UnitEditorView"). Pass the
        /// EXACT ROM tag the runners used — both `--screenshot-all` flows
        /// derive it from `Program.ROM.RomInfo.VersionToFilename`
        /// (binary-signature-detected version, NOT the ROM filename). When the
        /// passed tag does not match the runner-derived tag, files with a
        /// different `_{tag}.png` suffix are silently skipped — the gallery
        /// will look empty. `scripts/make-screenshots.ps1` infers the tag from
        /// the captured PNG filenames after running the runners; pass an
        /// explicit `--rom-tag=` when invoking the CLI directly.
        ///
        /// <paramref name="expectedEditors"/> is the optional coverage source
        /// (typically `LoadExpectedEditorsFromDoc(repoRoot)`). When non-null /
        /// non-empty, the report's `MissingFromExpected` list is populated with
        /// any expected editor that neither side captured; otherwise the list
        /// is empty.
        ///
        /// Missing directories are tolerated (treated as empty) so a non-Windows
        /// host that can't run the WinForms runner still produces an AV-only
        /// gallery.
        /// </summary>
        public static GalleryReport BuildGallery(
            string wfDir,
            string avDir,
            string romTag,
            IReadOnlyList<string>? expectedEditors = null)
        {
            if (string.IsNullOrEmpty(romTag))
                throw new ArgumentException("romTag must be non-empty", nameof(romTag));

            var wfMap = EnumerateAndStrip(wfDir, WfPrefix, romTag);
            var avMap = EnumerateAndStrip(avDir, AvPrefix, romTag);

            var pairs = new List<GalleryEntry>();
            var avOnly = new List<string>();
            var wfOnly = new List<string>();

            // Use Ordinal comparison: editor names are ASCII identifiers, no
            // culture-sensitive collation needed (and case-sensitive matching is
            // safer since the runner-generated names are stable).
            var allNames = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var k in wfMap.Keys) allNames.Add(k);
            foreach (var k in avMap.Keys) allNames.Add(k);

            foreach (string name in allNames)
            {
                bool hasWf = wfMap.TryGetValue(name, out string? wfPath);
                bool hasAv = avMap.TryGetValue(name, out string? avPath);

                if (hasWf && hasAv)
                    pairs.Add(new GalleryEntry(name, wfPath, avPath));
                else if (hasAv)
                    avOnly.Add(name);
                else if (hasWf)
                    wfOnly.Add(name);
            }

            // MissingFromExpected = expected \ (Pairs ∪ AvOnly ∪ WfOnly)
            var missing = new List<string>();
            if (expectedEditors is { Count: > 0 })
            {
                var captured = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in pairs) captured.Add(e.EditorName);
                foreach (var n in avOnly) captured.Add(n);
                foreach (var n in wfOnly) captured.Add(n);
                foreach (string expected in expectedEditors)
                {
                    if (!captured.Contains(expected))
                        missing.Add(expected);
                }
                missing.Sort(StringComparer.Ordinal);
            }

            return new GalleryReport(romTag, pairs, avOnly, wfOnly, missing);
        }

        /// <summary>
        /// Enumerate <paramref name="dir"/> for *.png files, strip the literal
        /// <paramref name="prefix"/> from the front and the literal
        /// `_{romTag}.png` from the back. Returns a map from the bare editor
        /// name to its absolute file path. Returns an empty map when the
        /// directory does not exist (tolerated for non-Windows hosts that skip
        /// the WinForms runner).
        ///
        /// Files that don't match the expected `prefix_*_romTag.png` shape are
        /// silently skipped — they're either unrelated PNGs or captures with
        /// a different ROM tag.
        /// </summary>
        static Dictionary<string, string> EnumerateAndStrip(string dir, string prefix, string romTag)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return result;

            string suffix = "_" + romTag + ".png";
            foreach (string path in Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(path);

                // Both prefix and suffix must match exactly. We deliberately do
                // NOT split on `_` (Copilot review concern) because some valid
                // view names embed underscores (e.g. `ToolWorkSupport_SelectUPSView`).
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
                    continue;

                int innerLen = fileName.Length - prefix.Length - suffix.Length;
                if (innerLen <= 0)
                    continue;
                string innerName = fileName.Substring(prefix.Length, innerLen);
                if (string.IsNullOrEmpty(innerName))
                    continue;

                // Conflict-tolerant: last writer wins. Multiple captures of the
                // same editor are not expected from the runners but we don't
                // want a crash if the user manually populated the directory.
                result[innerName] = path;
            }
            return result;
        }

        /// <summary>
        /// Render the gallery report as a Markdown body (sans front-matter — the
        /// caller wraps it with `ReportWriter.WriteReport`).
        ///
        /// <paramref name="wfRelDir"/> and <paramref name="avRelDir"/> are the
        /// relative-path roots used in `![](...)` image links. They should be
        /// the path FROM where `index.md` ends up TO where the PNGs live. For
        /// the canonical layout (`docs/avalonia-gaps/&lt;date&gt;-screenshots/&lt;rom&gt;/index.md`
        /// with PNGs at `.../wf/` and `.../av/`), the caller passes `"wf"` and
        /// `"av"` respectively.
        ///
        /// All newlines are LF — never CRLF — so committed reports diff cleanly
        /// across Windows/Linux/macOS checkouts. (Phase 0 / Phase 1 / Phase 2
        /// reports use the same policy; Copilot enforced it during PR #375
        /// re-review.)
        /// </summary>
        public static string FormatIndexMarkdown(
            GalleryReport report,
            string wfRelDir,
            string avRelDir)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            wfRelDir ??= "wf";
            avRelDir ??= "av";

            var sb = new StringBuilder();
            sb.Append("# Avalonia vs WinForms — Side-by-side Screenshot Gallery\n\n");
            sb.Append("> **Note:** PNG images live in the same per-ROM folder as this `index.md` and\n");
            sb.Append("> are gitignored (see `docs/avalonia-gaps/README.md` for the report-churn\n");
            sb.Append("> policy). The committed `index.md` is a *manifest*; regenerate the images\n");
            sb.Append("> locally with `scripts/make-screenshots.ps1` to view the gallery. The image\n");
            sb.Append("> links below resolve only when the PNGs are present alongside this file.\n\n");
            sb.Append("ROM tag: **").Append(EscapeMd(report.RomTag)).Append("**\n\n");

            // ---- Summary ----
            sb.Append("## Summary\n\n");
            sb.Append("| Metric | Count |\n");
            sb.Append("|---|---:|\n");
            sb.Append("| Paired editors (both sides captured) | ").Append(report.Pairs.Count).Append(" |\n");
            sb.Append("| Avalonia-only captures | ").Append(report.AvOnly.Count).Append(" |\n");
            sb.Append("| WinForms-only captures | ").Append(report.WfOnly.Count).Append(" |\n");
            sb.Append("| Expected but not captured | ").Append(report.MissingFromExpected.Count).Append(" |\n");
            sb.Append('\n');

            // ---- Side-by-side table ----
            sb.Append("## Side-by-side gallery (paired editors)\n\n");
            if (report.Pairs.Count == 0)
            {
                sb.Append("_No paired editors captured. Both runners may have failed or the ROM-tag did not match the saved filenames._\n\n");
            }
            else
            {
                sb.Append("| Editor | WinForms | Avalonia |\n");
                sb.Append("|---|---|---|\n");
                foreach (var entry in report.Pairs)
                {
                    string wfLink = BuildImageLink(entry.WfImagePath, wfRelDir, "WF");
                    string avLink = BuildImageLink(entry.AvImagePath, avRelDir, "AV");
                    sb.Append("| `").Append(EscapeMd(entry.EditorName)).Append("` | ")
                      .Append(wfLink).Append(" | ").Append(avLink).Append(" |\n");
                }
                sb.Append('\n');
            }

            // ---- AvOnly ----
            sb.Append("## Avalonia-only captures\n\n");
            sb.Append("Editors that Avalonia captured but `ScreenshotFormRegistry` (WinForms side)\n");
            sb.Append("does not map. Some are legitimately Avalonia-only (no WinForms counterpart);\n");
            sb.Append("others are gaps in the registry that should be added to surface side-by-side\n");
            sb.Append("captures in future gallery runs.\n\n");
            if (report.AvOnly.Count == 0)
                sb.Append("_(none)_\n\n");
            else
                AppendBulletList(sb, report.AvOnly, avRelDir, "Avalonia", report.RomTag);

            // ---- WfOnly ----
            sb.Append("## WinForms-only captures\n\n");
            sb.Append("Editors that WinForms captured but Avalonia did not. Either the AV editor\n");
            sb.Append("does not exist in `GetAllEditorFactories` or its capture failed silently.\n\n");
            if (report.WfOnly.Count == 0)
                sb.Append("_(none)_\n\n");
            else
                AppendBulletList(sb, report.WfOnly, wfRelDir, "WinForms", report.RomTag);

            // ---- MissingFromExpected ----
            sb.Append("## Expected editors not captured\n\n");
            sb.Append("Editors listed in `docs/avalonia-gui-forms.md` that neither runner captured.\n");
            sb.Append("Indicates a documentation/coverage drift or a runtime failure in the capture\n");
            sb.Append("loop. Empty section means the gallery captured the full expected set.\n\n");
            if (report.MissingFromExpected.Count == 0)
                sb.Append("_(none)_\n\n");
            else
            {
                foreach (string name in report.MissingFromExpected)
                    sb.Append("- `").Append(EscapeMd(name)).Append("`\n");
                sb.Append('\n');
            }

            // Trim trailing blank lines so the body ends in a single \n.
            // ReportWriter then appends at most one terminal newline, matching
            // the discipline enforced in PR #375's follow-up commit e803555e6.
            while (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
                sb.Length--;
            return sb.ToString();
        }

        /// <summary>
        /// Append a bullet list with image links for one-sided captures.
        /// <paramref name="romTag"/> is needed to reconstruct the filename that
        /// the runner originally wrote (we strip it during pairing; bullet
        /// links want it back).
        /// </summary>
        static void AppendBulletList(
            StringBuilder sb,
            IReadOnlyList<string> names,
            string relDir,
            string sideLabel,
            string romTag)
        {
            // Sort defensively even though the caller hands us a sorted list —
            // makes the report stable when consumers re-order things upstream.
            var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
            foreach (string name in sorted)
            {
                sb.Append("- `").Append(EscapeMd(name)).Append("`")
                  .Append(" — ![").Append(sideLabel).Append("](")
                  .Append(BuildRelImagePath(relDir, name, sideLabel, romTag)).Append(")\n");
            }
            sb.Append('\n');
        }

        /// <summary>
        /// Render the inline-Markdown image cell for the side-by-side table.
        /// Falls back to "—" when the path is null (one-sided capture).
        /// </summary>
        static string BuildImageLink(string? absPath, string relDir, string altSuffix)
        {
            if (string.IsNullOrEmpty(absPath))
                return "—";
            string fileName = Path.GetFileName(absPath);
            return "![" + altSuffix + "](" + UrlEscapePath(relDir + "/" + fileName) + ")";
        }

        /// <summary>
        /// Reconstruct the expected filename from <paramref name="innerName"/>
        /// + <paramref name="sideLabel"/> + the report's ROM tag. Used for
        /// AvOnly / WfOnly bullets where we don't have an absolute path to
        /// inspect — but we DO know the runners' naming convention
        /// (`{prefix}{innerName}_{romTag}.png`), so we can construct a working
        /// link by hand.
        /// </summary>
        static string BuildRelImagePath(string relDir, string innerName, string sideLabel, string romTag)
        {
            string prefix = sideLabel == "WinForms" ? WfPrefix : AvPrefix;
            return UrlEscapePath(relDir + "/" + prefix + innerName + "_" + romTag + ".png");
        }

        /// <summary>
        /// URL-escape a relative file path for safe embedding in a Markdown
        /// image link. Replaces space → %20 and any backslash → forward-slash
        /// so the link works on both POSIX-style and Windows-style file refs.
        /// We deliberately do NOT use Uri.EscapeDataString here: that escapes
        /// path separators too, which would break the link inside an editor
        /// previewing the markdown locally.
        /// </summary>
        static string UrlEscapePath(string path)
        {
            string normalised = path.Replace("\\", "/");
            return normalised.Replace(" ", "%20");
        }

        /// <summary>
        /// Backtick-safe rendering of an arbitrary identifier in markdown. Most
        /// of our names are ASCII identifiers, but a few WinForms editors
        /// embed underscores (we already handle those) — none currently embed
        /// backticks. We keep this defensive in case someone manually drops a
        /// weird filename into the directory.
        /// </summary>
        static string EscapeMd(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            // Replace embedded backticks with a Unicode lookalike so the inline
            // code span we wrap this in stays valid. The exact character (U+02CB,
            // MODIFIER LETTER GRAVE ACCENT) renders nearly identically and never
            // appears in a real editor name.
            return value.Replace("`", "ˋ");
        }

        // =====================================================================
        // LoadExpectedEditorsFromDoc — parses docs/avalonia-gui-forms.md
        // =====================================================================

        /// <summary>
        /// Parse `docs/avalonia-gui-forms.md` and return the union of view names
        /// listed in the markdown tables. Returns an empty list when the file
        /// does not exist, when no rows parse, or when the path is invalid — by
        /// design we never fail the gallery build just because the coverage
        /// tracker is missing.
        ///
        /// The doc's tables use the shape:
        ///   | # | View | E2E Status | Data Verified | Aligned |
        ///   |---|------|-----------|---------------|---------|
        ///   | 1 | UnitEditorView | ... |
        ///
        /// We extract column 2 (the View column) of every data row. Rows whose
        /// View column contains a markup-extension placeholder, "—", "?", or
        /// non-identifier characters are skipped silently.
        /// </summary>
        public static IReadOnlyList<string> LoadExpectedEditorsFromDoc(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot))
                return Array.Empty<string>();
            string docPath = Path.Combine(repoRoot, "docs", "avalonia-gui-forms.md");
            return LoadExpectedEditorsFromFile(docPath);
        }

        /// <summary>
        /// Test-friendly variant that takes the absolute path of the doc directly,
        /// so unit tests can drive the parser with a fixture file in a temp dir.
        /// </summary>
        public static IReadOnlyList<string> LoadExpectedEditorsFromFile(string docPath)
        {
            if (string.IsNullOrEmpty(docPath) || !File.Exists(docPath))
                return Array.Empty<string>();

            var names = new SortedSet<string>(StringComparer.Ordinal);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(docPath);
            }
            catch
            {
                return Array.Empty<string>();
            }

            // Identifier shape: starts with a letter, followed by letters, digits,
            // or underscores. Length-bounded to avoid runaway matches on doc text.
            var identifierRx = new Regex(@"^[A-Za-z][A-Za-z0-9_]{0,80}$", RegexOptions.Compiled);

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd();
                // Skip non-table lines; we only care about markdown rows that
                // start with `|`. The separator row (e.g. `|---|---|`) is also
                // a `|`-prefixed line, but its cells are all dashes and won't
                // pass the identifier regex below.
                if (line.Length < 5 || line[0] != '|')
                    continue;
                string[] cells = line.Split('|');
                if (cells.Length < 4)
                    continue;
                // cells[0] is empty (text before the first `|`), cells[1] is the
                // first table column (typically `#` for data rows or `# ` for
                // header rows), cells[2] is the View column. Trim each.
                string firstCell = cells[1].Trim();
                string viewCell = cells[2].Trim();
                if (string.IsNullOrEmpty(viewCell))
                    continue;
                // Reject the header row. Header rows carry either a literal
                // column name (`# `, `View`, `Editor`, etc.) instead of a row
                // index. Data rows always have an integer in cells[1].
                if (!int.TryParse(firstCell, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    continue;
                // Strip backticks and surrounding markdown formatting if any.
                viewCell = viewCell.Trim('`', ' ');
                if (!identifierRx.IsMatch(viewCell))
                    continue;
                // The doc usually ends View column entries with "View" — we
                // accept any identifier that the regex matches, but add this
                // belt-and-braces check so headings like "View" or random
                // identifiers don't pollute the list. Length > 4 excludes the
                // bare word "View" itself.
                if (!viewCell.EndsWith("View", StringComparison.Ordinal) || viewCell.Length <= 4)
                    continue;
                names.Add(viewCell);
            }
            return names.ToList();
        }
    }
}
