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
    }
}
