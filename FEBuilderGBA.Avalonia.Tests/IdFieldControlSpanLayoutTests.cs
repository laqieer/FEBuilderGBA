using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #941 detection scanner. A wide composite <c>IdFieldControl</c> (Hyperlink
    /// label + NumericUpDown + name preview + Jump + Pick buttons, ~420px minimum
    /// content width) must not be boxed into a grid that hard-caps it below that
    /// width. The reported regression (bug #2 in the 2026-06-04 sweep): the three
    /// SupportTalk views laid their detail form out as
    /// <c>ColumnDefinitions="160,200"</c> (=360px, no flexible column anywhere) and
    /// spanned the IdFieldControl across BOTH columns, so the trailing Pick button
    /// (and part of the name preview) clipped off the right edge.
    ///
    /// Precise predicate (kept narrow to avoid false positives on the many host
    /// views that span a fixed PAIR inside a grid that DOES have a flexible column
    /// elsewhere — e.g. OPClassDemoFE7View `180,180,*`, EDSensekiCommentView
    /// `200,200,*`, UnitFE7View `180,200,20,180,200`): an IdFieldControl is flagged
    /// iff it
    ///   (1) spans the ENTIRE column set of its nearest ancestor Grid
    ///       (Grid.Column == 0 AND Grid.ColumnSpan == column count), AND
    ///   (2) EVERY column in that grid is a fixed pixel width (no '*' / 'Auto'), AND
    ///   (3) the total fixed width is below MinIdFieldWidth.
    /// That isolates exactly the "detail form hard-capped to a fixed narrow total
    /// width" pathology and nothing else.
    ///
    /// A complementary subset rule (<see cref="IdFieldControl_NotConfinedToNarrowFixedSubset"/>)
    /// catches the case where the control spans only a PARTIAL column range that is
    /// entirely fixed-pixel and totals below <see cref="MinSubsetWidth"/> (300px).
    /// This caught the #951 regression (OPClassDemoViewer / ClassOPDemo, 220px subset).
    ///
    /// Pure XML scan — no ROM, no Avalonia runtime — fast and deterministic.
    /// </summary>
    public class IdFieldControlSpanLayoutTests
    {
        private readonly ITestOutputHelper _output;
        public IdFieldControlSpanLayoutTests(ITestOutputHelper output) => _output = output;

        // Conservative minimum content width of an IdFieldControl:
        //   Hyperlink label (~120) + NumericUpDown MinWidth 120 + NameLabel
        //   MinWidth 80 + Jump (~38) + Pick (~45) + spacing ~16 ~= 421px.
        private const double MinIdFieldWidth = 420.0;

        // Conservative minimum for the IdFieldControl's NON-LABEL core content
        // when the label is provided by a sibling cell outside the span:
        //   NumericUpDown MinWidth 120 + NameLabel MinWidth 80 + Jump (~40) +
        //   Pick (~45) + spacing ~15 ≈ 300px.
        // Using 300px (vs 420) avoids false-positives on borderline views whose
        // subsets render acceptably: EDSensekiComment (400px), OPClassDemoFE7
        // (360px), OPClassDemoFE8U (400px), UnitFE7 (380px).
        private const double MinSubsetWidth = 300.0;

        private static string FindProjectRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                string? parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            string cwd = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12; i++)
            {
                if (File.Exists(Path.Combine(cwd, "FEBuilderGBA.sln"))) return cwd;
                string? parent = Path.GetDirectoryName(cwd);
                if (parent == null || parent == cwd) break;
                cwd = parent;
            }
            throw new InvalidOperationException("Could not find project root (FEBuilderGBA.sln)");
        }

        private static string ViewsDir()
            => Path.Combine(FindProjectRoot(), "FEBuilderGBA.Avalonia", "Views");

        private static int GetAttachedInt(XElement e, string localName, int fallback)
        {
            var a = e.Attributes().FirstOrDefault(x => x.Name.LocalName == localName);
            if (a == null) return fallback;
            return int.TryParse(a.Value.Trim(), out int v) ? v : fallback;
        }

        private static string? GetNameAttr(XElement e)
            => e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value;

        // Column-width tokens of a Grid, from either the ColumnDefinitions="..."
        // attribute or the child <Grid.ColumnDefinitions> element form. null => the
        // grid declares no column definitions.
        private static List<string>? GetColumnTokens(XElement grid)
        {
            var attr = grid.Attributes().FirstOrDefault(a => a.Name.LocalName == "ColumnDefinitions");
            if (attr != null)
            {
                return attr.Value.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToList();
            }
            var defsElem = grid.Elements()
                .FirstOrDefault(el => el.Name.LocalName == "Grid.ColumnDefinitions");
            if (defsElem != null)
            {
                var toks = defsElem.Elements()
                    .Where(el => el.Name.LocalName == "ColumnDefinition")
                    .Select(el =>
                    {
                        var w = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "Width");
                        return w?.Value.Trim() ?? "*"; // ColumnDefinition default Width is *
                    })
                    .ToList();
                if (toks.Count > 0) return toks;
            }
            return null;
        }

        private static bool IsFlexible(string token)
            => token.Contains('*') || token.Equals("Auto", StringComparison.OrdinalIgnoreCase);

        // Sum fixed-pixel tokens; false if any token is non-fixed (flexible or unparseable).
        private static bool TryFixedTotal(IEnumerable<string> tokens, out double total)
        {
            total = 0;
            foreach (var t in tokens)
            {
                if (IsFlexible(t)) return false;
                if (!double.TryParse(t, out double v)) return false;
                total += v;
            }
            return true;
        }

        [Fact]
        public void IdFieldControl_NotBoxedInFullyFixedNarrowGrid()
        {
            string viewsDir = ViewsDir();
            Assert.True(Directory.Exists(viewsDir), $"Views dir not found: {viewsDir}");

            var offenders = new List<string>();
            var unanalyzable = new List<string>();

            foreach (string path in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.TopDirectoryOnly))
            {
                XDocument doc;
                try { doc = XDocument.Load(path); }
                catch { continue; } // non-XML/partial: other scanners cover parse health

                string file = Path.GetFileName(path);

                foreach (var idf in doc.Descendants().Where(e => e.Name.LocalName == "IdFieldControl"))
                {
                    int span = GetAttachedInt(idf, "Grid.ColumnSpan", 1);
                    if (span < 2) continue;
                    int col = GetAttachedInt(idf, "Grid.Column", 0);

                    // Grid.Column/ColumnSpan apply to the DIRECT parent Grid; use the
                    // nearest ancestor Grid and require parseable column definitions.
                    XElement? grid = idf.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Grid");
                    if (grid == null)
                    {
                        unanalyzable.Add($"{file}: IdFieldControl '{GetNameAttr(idf) ?? "?"}' has ColumnSpan={span} but no ancestor Grid.");
                        continue;
                    }
                    var cols = GetColumnTokens(grid);
                    if (cols == null)
                    {
                        unanalyzable.Add($"{file}: IdFieldControl '{GetNameAttr(idf) ?? "?"}' (ColumnSpan={span}) parent Grid declares no parseable ColumnDefinitions.");
                        continue;
                    }

                    bool spansFullSet = (col == 0 && span == cols.Count);
                    if (!spansFullSet) continue;                  // subset spans are label-width dependent → out of scope
                    if (!TryFixedTotal(cols, out double total)) continue; // a flexible col exists → grid can grow
                    if (total >= MinIdFieldWidth) continue;        // wide enough → won't clip

                    offenders.Add($"{file}: IdFieldControl '{GetNameAttr(idf) ?? "?"}' spans the full fixed grid [{string.Join(",", cols)}] = {total}px (< {MinIdFieldWidth}px IdFieldControl minimum) → Pick button / name preview will clip. Give the grid a flexible column (e.g. \"160,*\").");
                }
            }

            if (unanalyzable.Count > 0)
                _output.WriteLine("Unanalyzable IdFieldControl spans:\n" + string.Join("\n", unanalyzable));
            if (offenders.Count > 0)
                _output.WriteLine("Clipping IdFieldControl layouts:\n" + string.Join("\n", offenders));

            Assert.True(unanalyzable.Count == 0,
                "IdFieldControl(s) with ColumnSpan>=2 could not be analyzed (no ancestor Grid with ColumnDefinitions). " +
                "Fix the scanner or the layout:\n" + string.Join("\n", unanalyzable));

            Assert.True(offenders.Count == 0,
                "IdFieldControl boxed into a fully-fixed narrow grid (will clip the Pick button / name preview). " +
                "Give the containing grid a flexible ('*'/'Auto') column:\n" + string.Join("\n", offenders));
        }

        /// <summary>
        /// Complement to <see cref="IdFieldControl_NotBoxedInFullyFixedNarrowGrid"/>:
        /// catches the subset-clip pathology where an <c>IdFieldControl</c> is placed
        /// in a PARTIAL column range (e.g. <c>Grid.Column="1" Grid.ColumnSpan="2"</c>
        /// in a wider grid) that is entirely fixed-pixel AND totals below
        /// <see cref="MinSubsetWidth"/> (300px).
        ///
        /// Why 300px and not 420px: when an IdFieldControl occupies a subset of
        /// columns, the label for the row is typically provided by a sibling cell
        /// outside the span, so only the NumericUpDown (~120px) + name preview (~80px)
        /// + Jump (~40px) + Pick (~45px) + spacing (~15px) core is needed — roughly
        /// 300px minimum. The 300px threshold is intentionally LOWER than the full-set
        /// 420px to stay conservative: borderline subset views that render acceptably
        /// (EDSensekiComment 400px, OPClassDemoFE7 360px, UnitFE7 380px) are NOT
        /// flagged, while an unambiguously too-narrow span like 220px IS flagged.
        ///
        /// This rule would have caught the #951 regression: OPClassDemoViewerView and
        /// ClassOPDemoView placed their Display-Weapon IdFieldControl into a 220px
        /// fixed subset (columns 1..2, widths 140+80), clipping the name preview and
        /// Pick button. The fix widened those spans to 620px and 440px respectively.
        /// </summary>
        [Fact]
        public void IdFieldControl_NotConfinedToNarrowFixedSubset()
        {
            string viewsDir = ViewsDir();
            Assert.True(Directory.Exists(viewsDir), $"Views dir not found: {viewsDir}");

            var offenders = new List<string>();
            var unanalyzable = new List<string>();

            foreach (string path in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.TopDirectoryOnly))
            {
                XDocument doc;
                try { doc = XDocument.Load(path); }
                catch { continue; } // non-XML/partial: other scanners cover parse health

                string file = Path.GetFileName(path);

                foreach (var idf in doc.Descendants().Where(e => e.Name.LocalName == "IdFieldControl"))
                {
                    int span = GetAttachedInt(idf, "Grid.ColumnSpan", 1);
                    if (span < 2) continue;
                    int col = GetAttachedInt(idf, "Grid.Column", 0);

                    XElement? grid = idf.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Grid");
                    if (grid == null)
                    {
                        // Already covered by the full-set rule's unanalyzable check;
                        // don't double-report here.
                        continue;
                    }
                    var cols = GetColumnTokens(grid);
                    if (cols == null)
                    {
                        // Same: already surfaced by the full-set rule.
                        continue;
                    }

                    // Only examine SUBSET spans — the full-set (col==0, span==all) case
                    // is already handled by IdFieldControl_NotBoxedInFullyFixedNarrowGrid.
                    bool spansFullSet = (col == 0 && span == cols.Count);
                    if (spansFullSet) continue;

                    // Out-of-bounds guard (malformed AXAML; other tests catch parse errors).
                    if (col + span > cols.Count)
                    {
                        unanalyzable.Add($"{file}: IdFieldControl '{GetNameAttr(idf) ?? "?"}' col={col} ColumnSpan={span} exceeds declared column count ({cols.Count}).");
                        continue;
                    }

                    // Extract only the columns covered by this span.
                    var spannedCols = cols.GetRange(col, span);

                    // If ANY column in the spanned range is flexible (*or Auto), the
                    // control can grow with the window → not a clip risk.
                    if (!TryFixedTotal(spannedCols, out double total)) continue;

                    // Wide enough → no clip risk.
                    if (total >= MinSubsetWidth) continue;

                    offenders.Add(
                        $"{file}: IdFieldControl '{GetNameAttr(idf) ?? "?"}' is confined to a " +
                        $"narrow fixed-pixel subset of its grid — " +
                        $"Grid.Column={col} Grid.ColumnSpan={span} over [{string.Join(",", spannedCols)}] " +
                        $"= {total}px (< {MinSubsetWidth}px subset minimum; full grid is " +
                        $"[{string.Join(",", cols)}]). " +
                        $"Widen the span so it covers more fixed columns (total >= {MinSubsetWidth}px), " +
                        $"OR add a flexible ('*') column within the span.");
                }
            }

            if (unanalyzable.Count > 0)
                _output.WriteLine("Out-of-bounds IdFieldControl column spans:\n" + string.Join("\n", unanalyzable));
            if (offenders.Count > 0)
                _output.WriteLine("Subset-clipping IdFieldControl layouts:\n" + string.Join("\n", offenders));

            Assert.True(unanalyzable.Count == 0,
                "IdFieldControl(s) with out-of-bounds ColumnSpan detected. " +
                "Fix the AXAML column span:\n" + string.Join("\n", unanalyzable));

            Assert.True(offenders.Count == 0,
                "IdFieldControl confined to a fixed-pixel subset narrower than " +
                $"{MinSubsetWidth}px (Pick button / name preview will clip). " +
                "Widen the column span or add a flexible column:\n" + string.Join("\n", offenders));
        }
    }
}
