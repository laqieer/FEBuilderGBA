// SPDX-License-Identifier: GPL-3.0-or-later
// #950 T4 regression detector.
//
// Catches the regression class this migration closes: a plain <NumericUpDown>
// used for a STRONG entity-ID field (Unit ID / Class ID / Item ID / Text ID,
// plus the "Item N" / "Class ID N" / "Summoned Unit" forms this PR migrated)
// instead of a composite <IdFieldControl> (hyperlink label + numeric + name
// preview + Jump + Pick).
//
// Algorithm (pure XML scan over Views/*.axaml — no ROM, no Avalonia runtime):
//   For every label element (TextBlock Text="..." / Label Content="...") whose
//   text EXACTLY matches a strong entity-ID pattern, find the input control
//   ADJACENT to that label:
//     - same parent + same Grid.Row (Grid layout), OR
//     - the immediately-following sibling element (StackPanel layout).
//   If that input is a <NumericUpDown>, it MUST be on the per-control ALLOW-LIST
//   (keyed by {view, controlName} with a one-line reason). An <IdFieldControl>
//   adjacency satisfies the assertion with no allow-list entry needed.
//
// Anti-stale guard: every allow-list entry's control Name must still EXIST in
// its view (as a NumericUpDown). A removed/renamed allow-listed control fails
// the test so a stale exception cannot silently mask a future regression.
//
// Strong-positive ONLY: the label text must match the curated entity-ID set,
// so probabilities / Unknown* / coords / flags / pointers (which are NOT entity
// IDs) never trip the scanner — no allow-list churn for them.
//
// SCOPE: the scan covers the set of views in this #950 T4 migration slice
// (InScopeViews). App-wide entity-ID NumericUpDowns in OTHER editors are the
// charter of the other #950 tiers / a separate sweep — pulling them in here
// would be scope creep AND require a large speculative allow-list. The scoped
// scan still guarantees the regression contract for the migrated views: a
// future dev who re-introduces a plain NumericUpDown unit/class/item/text field
// in any of these views (or who adds a new strong-ID field without migrating
// it) trips the test. The forward-looking allow-list keeps working the same.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class IdFieldMigrationTests
    {
        private readonly ITestOutputHelper _output;
        public IdFieldMigrationTests(ITestOutputHelper output) => _output = output;

        // ---- Strong-positive entity-ID label patterns ----
        // Anchored, whitespace/colon-tolerant. Each pattern denotes a field that
        // SHOULD be an IdFieldControl (unit/class/item/text picker).
        private static readonly Regex[] StrongIdLabels = new[]
        {
            new Regex(@"^\s*Unit\s*ID\s*:?\s*$", RegexOptions.IgnoreCase),
            new Regex(@"^\s*Class\s*ID\s*\d*\s*:?\s*$", RegexOptions.IgnoreCase), // Class ID, Class ID 1..5
            new Regex(@"^\s*Item\s*ID\s*:?\s*$", RegexOptions.IgnoreCase),
            new Regex(@"^\s*Text\s*ID\s*:?\s*$", RegexOptions.IgnoreCase),
            new Regex(@"^\s*Item\s*[2-5]\s*:?\s*$", RegexOptions.IgnoreCase),   // Item 2..5 (B1..B4 item ids)
            new Regex(@"^\s*Summoned\s*Unit\s*:?\s*$", RegexOptions.IgnoreCase),
        };

        // ---- In-scope view set (#950 T4) ----
        // The views this migration slice converted. The scan asserts each of
        // these has ZERO un-migrated strong-ID NumericUpDowns. Adding a new
        // strong-ID field to any of these (or regressing a migrated one back to
        // a plain NumericUpDown) fails the test.
        private static readonly HashSet<string> InScopeViews = new(StringComparer.OrdinalIgnoreCase)
        {
            "MonsterItemViewerView.axaml",       // Tier 1 — Item 2..5 (item)
            "MonsterProbabilityViewerView.axaml",// Tier 1 — Class ID 1..5 (class)
            "SummonUnitViewerView.axaml",        // Tier 1 — Summoned Unit (unit)
            "ClassOPDemoView.axaml",             // Tier 2 — Display Weapon B14 (class)
            "OPClassDemoViewerView.axaml",       // Tier 2 — Display Weapon B14 (class)
            "OPClassDemoFE7View.axaml",          // Tier 2 — already-migrated B14 (regression guard)
            "OPClassDemoFE7UView.axaml",         // Tier 2 — already-migrated B14 (regression guard)
            "OPClassDemoFE8UView.axaml",         // Tier 2 — already-migrated B14 (regression guard)
            "EventCondView.axaml",               // Tier 3 — TALK Unit 1/2 (unit) + Chest Item (item)
        };

        // ---- ALLOW-LIST ----
        // {view file, NumericUpDown Name} → one-line reason. A strong-ID-labelled
        // NumericUpDown listed here is intentionally NOT an IdFieldControl. After
        // the #950 migration there should be (close to) zero entries — any future
        // addition needs an explicit justification here.
        //
        // NOTE: keep this EMPTY-by-default. If a legitimate exception arises, add
        // {file, name, reason}; the stale-guard below then enforces that the
        // control keeps existing as a NumericUpDown.
        private static readonly Dictionary<(string file, string name), string> AllowList =
            new()
            {
                // EventCond Tutorial-panel "Text ID:" is OUT of the T4 Tier-3
                // scope (which is TALK Unit 1/2 + OBJECT Chest Item only). It is
                // a tutorial-text id, not one of the migrated subfields; a focused
                // follow-up can migrate it to a TextViewer Jump (ShowPick=False).
                { ("EventCondView.axaml", "TextIdBox"),
                  "Tutorial Text ID — out of #950 T4 Tier-3 scope (TALK Unit/Chest only); TextViewer-Jump migration deferred." },
            };

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

        private static string? Attr(XElement e, string localName)
            => e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

        private static int AttrInt(XElement e, string localName, int fallback)
        {
            string? v = Attr(e, localName);
            return v != null && int.TryParse(v.Trim(), out int r) ? r : fallback;
        }

        // The display text of a label element: TextBlock.Text or Label.Content.
        private static string? LabelText(XElement e)
        {
            string ln = e.Name.LocalName;
            if (ln == "TextBlock") return Attr(e, "Text");
            if (ln == "Label") return Attr(e, "Content");
            return null;
        }

        private static bool IsStrongIdLabel(string text)
            => StrongIdLabels.Any(rx => rx.IsMatch(text));

        // Find the input control associated with the given label by adjacency:
        //   1) Grid layout: a sibling under the same parent with the SAME Grid.Row.
        //   2) StackPanel/inline layout: the immediately-following sibling element.
        // Returns the first NumericUpDown or IdFieldControl found.
        private static XElement? FindAdjacentInput(XElement label)
        {
            var parent = label.Parent;
            if (parent == null) return null;
            var siblings = parent.Elements().ToList();
            int idx = siblings.IndexOf(label);
            if (idx < 0) return null;

            bool IsInput(XElement e) =>
                e.Name.LocalName == "NumericUpDown" || e.Name.LocalName == "IdFieldControl";

            // 1) Same Grid.Row (Grid layout). The attached-property attribute
            //    serializes with LocalName "Grid.Row" (no xmlns binds "Grid").
            //    Column-aware: when a single row holds MULTIPLE label→input
            //    pairs (e.g. "Unit ID:" col0 + "Class ID:" col3), bind the label
            //    to the nearest input whose Grid.Column is strictly GREATER than
            //    the label's, choosing the smallest such column so each label
            //    maps to its own input — not the first input on the row.
            string? rowAttr = Attr(label, "Grid.Row");
            if (rowAttr != null && int.TryParse(rowAttr.Trim(), out int row))
            {
                int labelCol = AttrInt(label, "Grid.Column", 0);
                var sameRow = siblings
                    .Where(s => s != label && IsInput(s) && AttrInt(s, "Grid.Row", -999) == row)
                    .ToList();
                if (sameRow.Count > 0)
                {
                    var rightOf = sameRow
                        .Where(s => AttrInt(s, "Grid.Column", 0) > labelCol)
                        .OrderBy(s => AttrInt(s, "Grid.Column", 0))
                        .ToList();
                    if (rightOf.Count > 0) return rightOf[0];
                    // No input strictly to the right (single-pair row where the
                    // input shares/precedes the label column) → first on the row.
                    return sameRow[0];
                }
            }

            // 2) Immediately-following sibling (StackPanel / inline).
            for (int i = idx + 1; i < siblings.Count; i++)
            {
                if (IsInput(siblings[i])) return siblings[i];
                // Stop at the next label so we don't bind across unrelated rows.
                if (LabelText(siblings[i]) != null) break;
            }
            return null;
        }

        [Fact]
        public void EveryStrongEntityIdField_IsIdFieldControl_OrAllowListed()
        {
            string viewsDir = ViewsDir();
            Assert.True(Directory.Exists(viewsDir), $"Views dir not found: {viewsDir}");

            var offenders = new List<string>();
            // Track which allow-list entries we actually matched (anti-stale).
            var matchedAllow = new HashSet<(string, string)>();

            foreach (string path in Directory.GetFiles(viewsDir, "*.axaml", SearchOption.TopDirectoryOnly))
            {
                XDocument doc;
                string file = Path.GetFileName(path);
                if (!InScopeViews.Contains(file)) continue; // scoped to the #950 T4 slice
                try { doc = XDocument.Load(path); }
                catch { continue; } // non-XML/partial views are covered by other scanners

                foreach (var label in doc.Descendants())
                {
                    string? text = LabelText(label);
                    if (text == null || !IsStrongIdLabel(text)) continue;

                    XElement? input = FindAdjacentInput(label);
                    if (input == null) continue;                       // no adjacent input → not analyzable here
                    if (input.Name.LocalName == "IdFieldControl") continue; // migrated → OK

                    // input is a NumericUpDown adjacent to a strong-ID label.
                    string name = Attr(input, "Name") ?? "?";
                    var key = (file, name);
                    if (AllowList.ContainsKey(key))
                    {
                        matchedAllow.Add(key);
                        continue;
                    }
                    offenders.Add(
                        $"{file}: NumericUpDown '{name}' is labelled \"{text.Trim()}\" (a strong entity-ID field) " +
                        $"but is NOT an IdFieldControl and NOT allow-listed. Migrate it to <IdFieldControl> " +
                        $"or add a justified AllowList entry.");
                }
            }

            // Anti-stale: every allow-list entry must have matched a live control.
            var stale = AllowList.Keys
                .Where(k => !matchedAllow.Contains(k))
                .Select(k => $"{k.file}: allow-listed NumericUpDown '{k.name}' no longer exists " +
                             $"(stale exception — remove it or fix the control).")
                .ToList();

            if (offenders.Count > 0)
                _output.WriteLine("Un-migrated strong entity-ID NumericUpDowns:\n" + string.Join("\n", offenders));
            if (stale.Count > 0)
                _output.WriteLine("Stale allow-list entries:\n" + string.Join("\n", stale));

            Assert.True(stale.Count == 0,
                "Stale IdField migration allow-list entries (control gone/renamed):\n" + string.Join("\n", stale));
            Assert.True(offenders.Count == 0,
                "Strong entity-ID field(s) still rendered as plain NumericUpDown (should be IdFieldControl):\n" +
                string.Join("\n", offenders));
        }
    }
}
