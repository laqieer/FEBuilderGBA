// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374) — Phase 1: control-density delta.
//
// For every paired WinForms ↔ Avalonia editor, count the "visible" UI controls
// each side ships and emit a ranked report of the deltas. The hypothesis is
// that a large (>50 %) density gap is a strong proxy for missing fields in
// the Avalonia migration.
//
// WinForms side: Roslyn parses *Form.Designer.cs and counts ObjectCreationExpressionSyntax
// nodes whose Type identifier is in the allow-list (Button, TextBox, …).
// Designer.cs is what the Visual Studio Forms Designer auto-generates: every
// dragged-in control is `new System.Windows.Forms.{Type}(…)`. Roslyn lets us
// match the unqualified Type identifier so the WF System.Windows.Forms qualifier
// is irrelevant.
//
// Avalonia side: System.Xml.Linq parses *View.axaml. We walk the descendants,
// ignore anything inside a Style / DataTemplate / ControlTemplate / Design.DataContext
// (those are templates, not actual realised controls), and count local-names in
// the allow-list. The Avalonia xmlns ("https://github.com/avaloniaui") is the
// primary; we also accept the WPF-compatible default for completeness.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FEBuilderGBA.Avalonia.GapSweep
{
    /// <summary>Three-bucket "how big is the density gap" verdict.</summary>
    public enum Verdict
    {
        /// <summary>|Δ%| &lt; 25 — close enough that we don't flag.</summary>
        Low,
        /// <summary>|Δ%| in [25, 50) — worth a manual look.</summary>
        Medium,
        /// <summary>|Δ%| ≥ 50 — strong gap signal, gets a triage subsection.</summary>
        High,
    }

    /// <summary>
    /// One pair × its scanned counts. DeltaPct is positive when AV has more
    /// controls, negative when AV has fewer. <see cref="double.PositiveInfinity"/>
    /// when WfControlCount==0 && AvControlCount>0; rows with both==0 are filtered out
    /// upstream because they carry no signal.
    /// </summary>
    public record DensityRow(
        EditorPair Pair,
        int WfControlCount,
        int AvControlCount,
        double DeltaPct,
        Verdict Verdict);

    /// <summary>Phase 1: scan and rank every paired editor by control-count delta.</summary>
    public static class ControlDensityScanner
    {
        /// <summary>
        /// Allow-list of WinForms control type identifiers we count. These are the
        /// concrete classes the VS Designer emits as `new System.Windows.Forms.{Type}(…)`.
        /// We deliberately omit container-only types (FlowLayoutPanel, TableLayoutPanel,
        /// Panel) and decorative-only types (Separator, ToolStripSeparator) so the
        /// count tracks "user-facing widgets" rather than layout scaffolding.
        /// </summary>
        static readonly HashSet<string> WfControlTypes = new(StringComparer.Ordinal)
        {
            "Button", "TextBox", "NumericUpDown", "ComboBox", "CheckBox", "RadioButton",
            "Label", "DataGridView", "GroupBox", "TabPage", "ListBox", "PictureBox",
        };

        /// <summary>
        /// Allow-list of Avalonia XAML element local-names. We translate the WF
        /// allow-list to the equivalent Avalonia controls (Label → TextBlock,
        /// DataGridView → DataGrid, GroupBox → Expander, PictureBox → Image, TabPage → TabItem)
        /// so the counts are directly comparable.
        /// </summary>
        static readonly HashSet<string> AvControlTypes = new(StringComparer.Ordinal)
        {
            "Button", "TextBox", "NumericUpDown", "ComboBox", "CheckBox", "RadioButton",
            "Label", "TextBlock", "DataGrid", "ListBox", "Image", "Expander", "TabItem",
        };

        /// <summary>
        /// Container elements that hold *templates* (not actual realised controls).
        /// Anything nested under one of these is excluded from the count to avoid
        /// double-counting items rendered per-row in lists, etc.
        /// </summary>
        static readonly HashSet<string> AvTemplateContainers = new(StringComparer.Ordinal)
        {
            "Design.DataContext",
            "Style",
            "Styles",
            "DataTemplate",
            "ControlTemplate",
            "ItemTemplate",
            "ItemsPanelTemplate",
            "HierarchicalDataTemplate",
        };

        /// <summary>
        /// HIGH gap threshold (≥ 50 % absolute delta). Tuned to the example deltas
        /// from the plan: UnitForm (-70 %), ItemForm (-77 %), ImagePortraitForm (-79 %),
        /// MapSettingForm (-55 %) — all four land in HIGH.
        /// </summary>
        const double HighDeltaPctThreshold = 50.0;
        /// <summary>MEDIUM gap threshold (25 % ≤ |Δ%| &lt; 50 %).</summary>
        const double MediumDeltaPctThreshold = 25.0;

        /// <summary>
        /// Run the scan over <paramref name="pairs"/>. <paramref name="repoRoot"/>
        /// is used only to resolve sibling Designer.cs files when WfPath is set.
        /// Pairs with both counts == 0 are dropped (they carry no signal — e.g.
        /// a stub view paired with a stub form).
        /// </summary>
        public static IReadOnlyList<DensityRow> Scan(IReadOnlyList<EditorPair> pairs, string repoRoot)
        {
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));
            if (string.IsNullOrEmpty(repoRoot)) throw new ArgumentException("repoRoot required", nameof(repoRoot));

            var rows = new ConcurrentBag<DensityRow>();
            // Parallel.ForEach because Roslyn parsing is CPU-bound and per-file
            // independent; for ~290 designer files this brings the scan from
            // ~6 s sequential to ~1.5 s on a 12-core machine.
            Parallel.ForEach(pairs, pair =>
            {
                int wfCount = pair.WfPath != null ? CountWfControls(pair.WfPath) : 0;
                int avCount = pair.AvPath != null ? CountAvControls(pair.AvPath) : 0;

                // Skip 100% empty pairs — they only happen when both sides exist
                // but neither has any visible controls (or scanning failed for
                // both), and they don't tell us anything.
                if (wfCount == 0 && avCount == 0)
                    return;

                double deltaPct;
                if (wfCount == 0)
                    deltaPct = double.PositiveInfinity;
                else
                    deltaPct = (avCount - wfCount) / (double)wfCount * 100.0;

                Verdict verdict = ClassifyVerdict(deltaPct);
                rows.Add(new DensityRow(pair, wfCount, avCount, deltaPct, verdict));
            });

            // Rank by |Δ%| descending so the biggest gaps surface at the top.
            // Infinity (WF=0, AV>0) sorts above everything finite, which is what
            // we want — those are AV-only views with no WF counterpart at all.
            return rows
                .OrderByDescending(r => Math.Abs(r.DeltaPct))
                .ThenBy(r => r.Pair.WfFormName ?? r.Pair.AvViewName ?? "", StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Map signed delta to the three-bucket verdict.</summary>
        static Verdict ClassifyVerdict(double deltaPct)
        {
            double abs = Math.Abs(deltaPct);
            if (abs >= HighDeltaPctThreshold) return Verdict.High;
            if (abs >= MediumDeltaPctThreshold) return Verdict.Medium;
            return Verdict.Low;
        }

        /// <summary>
        /// Count UI controls in a WinForms form. We parse the sibling
        /// <c>*Form.Designer.cs</c> (where the designer auto-generates control
        /// instantiations); if it doesn't exist, we fall back to parsing the
        /// *Form.cs itself (some hand-coded forms instantiate controls inline).
        /// </summary>
        static int CountWfControls(string formCsPath)
        {
            try
            {
                string? designerPath = TryFindDesignerSibling(formCsPath);
                int count = 0;
                if (designerPath != null && File.Exists(designerPath))
                    count += CountObjectCreationsInFile(designerPath);
                // Some forms instantiate controls in code-behind (not just designer);
                // include both files when both exist, so we don't miss hand-coded
                // controls.
                count += CountObjectCreationsInFile(formCsPath);
                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Locate the *.Designer.cs file that sits next to a *Form.cs. Returns
        /// null if there isn't one (some forms are pure code, no designer).
        /// </summary>
        static string? TryFindDesignerSibling(string formCsPath)
        {
            string dir = Path.GetDirectoryName(formCsPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(formCsPath);
            string candidate = Path.Combine(dir, baseName + ".Designer.cs");
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>
        /// Roslyn-count <c>new {Type}(…)</c> expressions whose Type identifier
        /// matches the allow-list. We match the trailing identifier only, so
        /// <c>System.Windows.Forms.Button</c>, <c>SWF.Button</c>, and plain
        /// <c>Button</c> all match.
        /// </summary>
        public static int CountObjectCreationsInFile(string path)
        {
            string code = File.ReadAllText(path);
            return CountObjectCreationsInSource(code);
        }

        /// <summary>
        /// Extracted for testability: counts WF controls in an in-memory source
        /// string (no file I/O).
        /// </summary>
        public static int CountObjectCreationsInSource(string sourceCode)
        {
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
            SyntaxNode root = tree.GetRoot();
            int count = 0;
            foreach (var node in root.DescendantNodes())
            {
                if (node is not ObjectCreationExpressionSyntax oce)
                    continue;
                string typeName = ExtractTypeName(oce.Type);
                if (WfControlTypes.Contains(typeName))
                    count++;
            }
            return count;
        }

        /// <summary>Pull the trailing identifier out of a TypeSyntax.</summary>
        static string ExtractTypeName(TypeSyntax type)
        {
            return type switch
            {
                QualifiedNameSyntax q => q.Right.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                AliasQualifiedNameSyntax aqn => aqn.Name.Identifier.Text,
                GenericNameSyntax gn => gn.Identifier.Text,
                _ => type.ToString(),
            };
        }

        /// <summary>
        /// Count UI controls in an Avalonia view (.axaml). We use System.Xml.Linq
        /// because the file is well-formed XML; no need for the heavier Avalonia
        /// XAML loader (which would require runtime services we don't have here).
        /// </summary>
        static int CountAvControls(string axamlPath)
        {
            try
            {
                XDocument doc = XDocument.Load(axamlPath);
                return CountAvControlsInDocument(doc);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Extracted for testability: counts Avalonia controls in an in-memory
        /// XDocument. Excludes anything nested inside a known template container.
        /// </summary>
        public static int CountAvControlsInDocument(XDocument doc)
        {
            if (doc.Root == null)
                return 0;
            int count = 0;
            foreach (XElement el in doc.Descendants())
            {
                if (!AvControlTypes.Contains(el.Name.LocalName))
                    continue;
                if (IsInsideTemplate(el))
                    continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Walk the ancestor chain looking for a known template container.
        /// Returns true if the element is nested inside one (and therefore is
        /// part of a template rather than the realised layout).
        /// </summary>
        static bool IsInsideTemplate(XElement el)
        {
            for (XElement? cur = el.Parent; cur != null; cur = cur.Parent)
            {
                if (AvTemplateContainers.Contains(cur.Name.LocalName))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Format the markdown body of the density report (sans front-matter — that
        /// is added by <see cref="ReportWriter"/>). Layout:
        ///
        ///  1. Summary table of HIGH / MEDIUM / LOW counts.
        ///  2. Ranked table of every paired row, sorted by |Δ%| desc.
        ///  3. Top-20 HIGH-verdict triage subsections (heading per form).
        ///  4. AV-only and WF-only orphans list.
        /// </summary>
        public static string FormatReport(IReadOnlyList<DensityRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Avalonia vs WinForms — Control Density Sweep");
            sb.AppendLine();
            sb.AppendLine("This report ranks every paired editor by the absolute % delta between the");
            sb.AppendLine("WinForms-designer control count and the Avalonia .axaml control count.");
            sb.AppendLine("A large negative delta is a strong proxy for **missing fields in the");
            sb.AppendLine("Avalonia migration** — the WinForms side has UI for inputs the Avalonia");
            sb.AppendLine("counterpart does not expose. Use the top-20 HIGH subsections below as the");
            sb.AppendLine("backlog seed for follow-up gap-fix PRs.");
            sb.AppendLine();
            sb.AppendLine("Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/ControlDensityScanner.cs`.");
            sb.AppendLine("Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-density --out=<path>`.");
            sb.AppendLine();

            // ---- Summary ----
            int highCount = rows.Count(r => r.Verdict == Verdict.High);
            int medCount = rows.Count(r => r.Verdict == Verdict.Medium);
            int lowCount = rows.Count(r => r.Verdict == Verdict.Low);
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine("| Verdict | Threshold | Count |");
            sb.AppendLine("|---|---|---|");
            sb.AppendFormat("| HIGH | |Δ%| ≥ {0:0} | {1} |", HighDeltaPctThreshold, highCount).AppendLine();
            sb.AppendFormat("| MEDIUM | {0:0} ≤ |Δ%| < {1:0} | {2} |", MediumDeltaPctThreshold, HighDeltaPctThreshold, medCount).AppendLine();
            sb.AppendFormat("| LOW | |Δ%| < {0:0} | {1} |", MediumDeltaPctThreshold, lowCount).AppendLine();
            sb.AppendLine();

            // ---- Ranked table ----
            // The "ranked" section only includes rows where BOTH sides have at
            // least one recognised control (WF count > 0 AND AV count > 0).
            // Rows with WF==0 or AV==0 are either (a) ListParityHelper mappings
            // whose WF file was renamed / removed (typo), or (b) scan failures
            // (file missing, AXAML parse error). Either way they're not "missing
            // AV fields in a real migration" — they live in the dedicated
            // Unmatched-Counterparts sections below.
            //
            // Sort by signed Δ% ascending so the most-negative deltas (biggest gaps
            // where AV is missing controls relative to WF) bubble to the top.
            sb.AppendLine("## Ranked Density Deltas");
            sb.AppendLine();
            sb.AppendLine("Negative `Δ%` = Avalonia has *fewer* controls than WinForms (probable gap).");
            sb.AppendLine("Positive = Avalonia has *more* (often fine — refactoring or richer UI).");
            sb.AppendLine("Rows are sorted by signed `Δ%` ascending so the biggest gaps come first.");
            sb.AppendLine();
            sb.AppendLine("Rows with WF=0 (no on-disk Designer.cs for the named form) live in");
            sb.AppendLine("[Unmatched WinForms Counterparts](#unmatched-winforms-counterparts) below;");
            sb.AppendLine("rows with AV=0 (no on-disk .axaml for the named view, or AXAML parse failure)");
            sb.AppendLine("live in [Unmatched Avalonia Counterparts](#unmatched-avalonia-counterparts).");
            sb.AppendLine("Both represent pairing artifacts rather than real migration gaps.");
            sb.AppendLine();
            sb.AppendLine("| Verdict | WF Form | AV View | WF | AV | Δ | Δ% | Match |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|");
            var rankableRows = rows
                .Where(r => r.Pair.Match != MatchMethod.Orphan)
                .Where(r => r.WfControlCount > 0 && r.AvControlCount > 0)
                .OrderBy(r => r.DeltaPct)
                .ThenBy(r => r.Pair.WfFormName ?? r.Pair.AvViewName ?? "", StringComparer.Ordinal)
                .ToList();
            foreach (var r in rankableRows)
            {
                sb.Append("| ").Append(r.Verdict)
                  .Append(" | `").Append(r.Pair.WfFormName ?? "—").Append("`")
                  .Append(" | `").Append(r.Pair.AvViewName ?? "—").Append("`")
                  .Append(" | ").Append(r.WfControlCount)
                  .Append(" | ").Append(r.AvControlCount)
                  .Append(" | ").Append(r.AvControlCount - r.WfControlCount)
                  .Append(" | ").Append(FormatDelta(r.DeltaPct))
                  .Append(" | ").Append(r.Pair.Match)
                  .AppendLine(" |");
            }
            sb.AppendLine();

            // ---- Pseudo-orphan rows: WF count = 0 (AV view exists, WF file missing) ----
            var unmatchedWfRows = rows
                .Where(r => r.Pair.Match != MatchMethod.Orphan)
                .Where(r => r.WfControlCount == 0 && r.AvControlCount > 0)
                .OrderByDescending(r => r.AvControlCount)
                .ThenBy(r => r.Pair.WfFormName ?? r.Pair.AvViewName ?? "", StringComparer.Ordinal)
                .ToList();
            sb.AppendLine("## Unmatched WinForms Counterparts");
            sb.AppendLine();
            sb.AppendLine("Paired by name/heuristic but WF Designer.cs file not found (renamed, removed,");
            sb.AppendLine("or a typo in `ListParityHelper`). Not a real migration gap — the AV side just");
            sb.AppendLine("happens to lack a directly-named WF counterpart on disk.");
            sb.AppendLine();
            sb.AppendLine("| WF Form (claimed) | AV View | AV controls | Match |");
            sb.AppendLine("|---|---|---:|---|");
            foreach (var r in unmatchedWfRows)
            {
                sb.Append("| `").Append(r.Pair.WfFormName ?? "—").Append("`")
                  .Append(" | `").Append(r.Pair.AvViewName ?? "—").Append("`")
                  .Append(" | ").Append(r.AvControlCount)
                  .Append(" | ").Append(r.Pair.Match)
                  .AppendLine(" |");
            }
            sb.AppendLine();

            // ---- Pseudo-orphan rows: AV count = 0 (WF form exists, AV view missing or parse-failed) ----
            var unmatchedAvRows = rows
                .Where(r => r.Pair.Match != MatchMethod.Orphan)
                .Where(r => r.WfControlCount > 0 && r.AvControlCount == 0)
                .OrderByDescending(r => r.WfControlCount)
                .ThenBy(r => r.Pair.WfFormName ?? r.Pair.AvViewName ?? "", StringComparer.Ordinal)
                .ToList();
            sb.AppendLine("## Unmatched Avalonia Counterparts");
            sb.AppendLine();
            sb.AppendLine("Paired by name/heuristic but the AV .axaml is either missing on disk or failed");
            sb.AppendLine("to parse (System.Xml.Linq returned no controls). Not a real density gap — the");
            sb.AppendLine("scanner could not measure the AV side at all. Investigate the AV file before");
            sb.AppendLine("treating these as actual migration deficits.");
            sb.AppendLine();
            sb.AppendLine("| WF Form | AV View (claimed) | WF controls | Match |");
            sb.AppendLine("|---|---|---:|---|");
            foreach (var r in unmatchedAvRows)
            {
                sb.Append("| `").Append(r.Pair.WfFormName ?? "—").Append("`")
                  .Append(" | `").Append(r.Pair.AvViewName ?? "—").Append("`")
                  .Append(" | ").Append(r.WfControlCount)
                  .Append(" | ").Append(r.Pair.Match)
                  .AppendLine(" |");
            }
            sb.AppendLine();

            // ---- Top-20 HIGH subsections ----
            // "Top-20" = top 20 negative-delta HIGH rows where BOTH sides have a
            // scannable file with at least one recognised control. AV==0 rows are
            // explicitly excluded (they're either missing .axaml or parse failures —
            // not real migration gaps; the dedicated Unmatched-AV-Counterparts section
            // catches them). This guarantees every triage subsection points at a
            // pair where a concrete WF→AV control delta is the actual finding.
            sb.AppendLine("## Top-20 HIGH Gaps — Triage Notes");
            sb.AppendLine();
            sb.AppendLine("Manual notes below each heading describe what specific labels / controls");
            sb.AppendLine("appear in the WinForms form but are missing from the Avalonia view. Fill");
            sb.AppendLine("each section by grepping the Designer.cs for `.Text = \"…\"` initialisers and");
            sb.AppendLine("cross-checking against the .axaml literals.");
            sb.AppendLine();
            int triaged = 0;
            foreach (var r in rows
                .Where(r => r.Verdict == Verdict.High && r.Pair.Match != MatchMethod.Orphan)
                .Where(r => r.WfControlCount > 0 && r.AvControlCount > 0 && r.DeltaPct < 0)
                .Where(r => r.Pair.WfPath != null && r.Pair.AvPath != null)
                .OrderBy(r => r.DeltaPct))
            {
                if (triaged++ >= 20)
                    break;
                string formName = r.Pair.WfFormName ?? r.Pair.AvViewName ?? "?";
                sb.Append("### ").Append(formName).AppendLine();
                sb.AppendFormat(
                    "WF count: **{0}** · AV count: **{1}** · Δ: **{2:+0;-0;0}** ({3}).",
                    r.WfControlCount, r.AvControlCount,
                    r.AvControlCount - r.WfControlCount,
                    FormatDelta(r.DeltaPct)).AppendLine();
                sb.AppendLine();
                sb.AppendLine("<!-- Triage: list specific missing fields here. Suggested probe:");
                if (r.Pair.WfPath != null)
                {
                    string rel = MakeRepoRelative(r.Pair.WfPath);
                    sb.Append("  grep -E '\\.Text\\s*=' ")
                      .Append(rel.Replace(".cs", ".Designer.cs"))
                      .AppendLine();
                }
                if (r.Pair.AvPath != null)
                {
                    string rel = MakeRepoRelative(r.Pair.AvPath);
                    sb.Append("  grep -E '(Text|Content|Header)=' ")
                      .Append(rel)
                      .AppendLine();
                }
                sb.AppendLine("-->");
                sb.AppendLine();
            }

            // ---- Orphans ----
            sb.AppendLine("## Unpaired Orphans");
            sb.AppendLine();
            sb.AppendLine("These editors have only one side; they need manual triage to decide whether");
            sb.AppendLine("the missing counterpart is expected (e.g., FE6-only forms not yet ported) or");
            sb.AppendLine("a genuine gap.");
            sb.AppendLine();

            var wfOrphans = rows.Where(r => r.Pair.Match == MatchMethod.Orphan && r.Pair.WfFormName != null && r.Pair.AvViewName == null).ToList();
            var avOrphans = rows.Where(r => r.Pair.Match == MatchMethod.Orphan && r.Pair.WfFormName == null && r.Pair.AvViewName != null).ToList();

            sb.AppendFormat("### WinForms-only ({0})", wfOrphans.Count).AppendLine();
            sb.AppendLine();
            sb.AppendLine("| WF Form | WF controls |");
            sb.AppendLine("|---|---:|");
            foreach (var r in wfOrphans.OrderBy(r => r.Pair.WfFormName, StringComparer.Ordinal))
            {
                sb.Append("| `").Append(r.Pair.WfFormName).Append("` | ").Append(r.WfControlCount).AppendLine(" |");
            }
            sb.AppendLine();

            sb.AppendFormat("### Avalonia-only ({0})", avOrphans.Count).AppendLine();
            sb.AppendLine();
            sb.AppendLine("| AV View | AV controls |");
            sb.AppendLine("|---|---:|");
            foreach (var r in avOrphans.OrderBy(r => r.Pair.AvViewName, StringComparer.Ordinal))
            {
                sb.Append("| `").Append(r.Pair.AvViewName).Append("` | ").Append(r.AvControlCount).AppendLine(" |");
            }
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>Render a delta percentage compactly. Infinity becomes "+∞".</summary>
        static string FormatDelta(double pct)
        {
            if (double.IsPositiveInfinity(pct)) return "+∞%";
            if (double.IsNegativeInfinity(pct)) return "-∞%";
            return pct.ToString("+0.0;-0.0;0.0") + "%";
        }

        /// <summary>
        /// Strip any leading directory prefix from <paramref name="absolute"/> so the
        /// path printed in the report is relative to the repo root (independent of
        /// where the user generated it from).
        /// </summary>
        static string MakeRepoRelative(string absolute)
        {
            // Find the first "FEBuilderGBA" segment and slice from there.
            int idx = absolute.Replace('\\', '/').LastIndexOf("/FEBuilderGBA", StringComparison.Ordinal);
            if (idx < 0)
                return absolute.Replace('\\', '/');
            return absolute.Substring(idx + 1).Replace('\\', '/');
        }
    }
}
