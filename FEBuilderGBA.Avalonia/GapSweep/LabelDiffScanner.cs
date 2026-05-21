// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374) — Phase 2: field-label diff.
//
// Phase 1 (ControlDensityScanner) gives a QUANTITATIVE gap signal: "this
// WinForms form has 183 controls and its Avalonia counterpart has 54 — that
// is a -70 % density delta, almost certainly missing fields". The density
// signal is necessary but not sufficient — it tells us WHERE to look, not
// WHAT is missing.
//
// Phase 2 (this scanner) is the QUALITATIVE follow-up: for every paired
// editor, extract the human-readable label literals from both sides, run a
// normalised set-difference, and produce a per-pair list of:
//
//   WF-only labels  — strong candidates for missing fields in Avalonia
//   AV-only labels  — usually fine (different layout, language polish, etc.)
//   Common labels   — count only (signal that some coverage exists)
//
// The output is the backlog seed for the actual gap-fix PRs that follow.
//
// WinForms side: Roslyn parses *Form.Designer.cs files (the VS Designer auto-
// generates `this.foo.Text = "..."` assignments for every Label/Button/etc.).
// We collect string-literal assignments to `.Text` whose enclosing object-
// creation type is in a known control allow-list, plus property-initialiser
// syntax `new Label { Text = "..." }` for hand-coded forms. Designers that
// use VS's localisation-aware emit mode (`resources.GetString("key")`) are
// resolved by parsing the sibling *.resx file.
//
// Avalonia side: System.Xml.Linq parses *View.axaml. We walk every element
// and harvest literal values from `Text`, `Content`, `Header`, `ToolTip`,
// `ToolTip.Tip` (the attached-property form, which this codebase uses ~55
// times), `Watermark` attributes. The implementation reads ATTRIBUTES only,
// not property-element forms (`<Button.Content>Save</Button.Content>`); the
// repo's views never use that syntax so handling it would add complexity
// for no real signal. Values that start with `{` are markup extensions
// (`{Binding ...}`, `{StaticResource ...}`, `{DynamicResource ...}`,
// `{x:Static ...}`, etc.) and are skipped. Elements inside templating
// containers (Style/Styles/DataTemplate/ControlTemplate/Design.DataContext)
// are also skipped — they describe templates, not the realised layout.
//
// Normalisation strips trailing colons (UI convention "Name:" vs AXAML "Name"),
// collapses whitespace, lowercases, and removes mnemonic markers (`&` for
// WinForms, `_` for Avalonia) so equivalent labels match across platforms.
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
    /// <summary>
    /// One paired editor's label-diff result. WfOnlyLabels are the labels we
    /// found in the WinForms designer that have no normalised counterpart in
    /// the Avalonia view — the qualitative gap-finding signal.
    /// AvOnlyLabels are the reverse (usually fine, often just a re-worded UI).
    /// CommonLabels are labels that survived normalisation on both sides.
    /// All three lists preserve the ORIGINAL casing/punctuation of the first
    /// occurrence — the normalisation is used only as the diff key.
    /// </summary>
    public record LabelDiffRow(
        EditorPair Pair,
        IReadOnlyList<string> WfOnlyLabels,
        IReadOnlyList<string> AvOnlyLabels,
        IReadOnlyList<string> CommonLabels);

    /// <summary>
    /// Phase 2: extract label literals from both sides of every paired editor
    /// and emit a per-pair set-difference report. Methodology complements the
    /// Phase 1 density scan — Phase 1 says "this pair has 70 % fewer controls",
    /// Phase 2 says "and here are the specific labels Avalonia is missing".
    /// </summary>
    public static class LabelDiffScanner
    {
        /// <summary>
        /// WinForms control type identifiers whose `.Text = "..."` assignments
        /// we treat as labels. We deliberately limit this to controls that
        /// actually render their `Text` property as user-facing label text:
        ///   - Label / GroupBox: pure labels
        ///   - Button / CheckBox / RadioButton: caption text
        ///   - TabPage: tab caption
        /// Excludes types whose `Text` is data, not a label (TextBox, ComboBox,
        /// NumericUpDown, RichTextBox, MaskedTextBox, …).
        /// </summary>
        static readonly HashSet<string> WfLabelHostTypes = new(StringComparer.Ordinal)
        {
            "Label",
            "GroupBox",
            "Button",
            "CheckBox",
            "RadioButton",
            "TabPage",
        };

        /// <summary>
        /// AXAML attribute local-names we treat as literal-label sources. Only the
        /// attribute form is harvested — property-element syntax
        /// (`&lt;Button.Content&gt;Save&lt;/Button.Content&gt;`) is rare across
        /// this codebase's views (a `grep -r '\.Content&gt;' Views/` finds zero
        /// matches), so the implementation deliberately skips that path to keep
        /// the scanner simple.
        ///
        /// `ToolTip.Tip` is included for Avalonia's attached-property form
        /// `ToolTip.Tip="..."` — the codebase has ~55 such usages across
        /// ClassEditorView / ItemEditorView / etc. XDocument exposes the
        /// attribute's local-name verbatim including the dot, so we match
        /// `ToolTip.Tip` literally (NOT just `Tip`, which would over-match any
        /// element with a `Tip` attribute).
        /// </summary>
        static readonly HashSet<string> AvLabelAttributes = new(StringComparer.Ordinal)
        {
            "Text", "Content", "Header", "ToolTip", "ToolTip.Tip", "Watermark",
        };

        /// <summary>
        /// Template containers — same set as ControlDensityScanner — so the
        /// label diff stays consistent with the density count. Elements nested
        /// under any of these are SKIPPED because they describe templates
        /// (e.g. per-row DataTemplate) rather than the realised layout, and
        /// would otherwise inflate the AV side with synthetic literals that
        /// never appear in the actual UI.
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
        /// Normalise a label for set-membership comparison. The goal is that
        /// every reasonable cross-platform encoding of the SAME label collides
        /// to the same key:
        ///
        ///   "Name:"           // WF Designer
        ///   "Name"            // AV plain
        ///   " name "          // AV with whitespace
        ///   "&Name"           // WF with mnemonic
        ///   "_Name"           // AV with mnemonic
        ///   "  Name  :  "     // pathological
        ///
        /// all map to "name". Mnemonic markers (`&` for WF, `_` for AV) are
        /// stripped so cross-platform-equivalent labels don't show up as
        /// spurious WF-only hits. The trailing colon is stripped because the
        /// WF designer convention is `"Field Name:"` whereas the AV convention
        /// is `"Field Name"`. Internal whitespace is collapsed to single
        /// spaces. The result is then lowercased.
        ///
        /// The original casing/punctuation IS preserved in the report — the
        /// normalised key is only the dictionary lookup. Callers see the first
        /// occurrence's original spelling in the WfOnly/AvOnly/Common lists.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;
            // Strip mnemonic markers (& for WinForms, _ for Avalonia). We
            // strip BOTH markers from BOTH sides because the goal is to match
            // labels across the platform-specific marker convention.
            // Note: WinForms also uses "&&" as a literal ampersand — for the
            // normalisation pass we just strip all `&`/`_` since the report
            // preserves the original anyway and the data we deal with is
            // 99.9 % field labels (rarely literal ampersands).
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (c == '&' || c == '_')
                    continue;
                sb.Append(c);
            }
            string stripped = sb.ToString();
            // Trim outer whitespace, strip trailing colon, collapse internal
            // whitespace to single spaces, lowercase. We do the collapse via
            // a manual walk because Regex would be overkill here.
            stripped = stripped.Trim();
            while (stripped.EndsWith(":", StringComparison.Ordinal))
                stripped = stripped.Substring(0, stripped.Length - 1).TrimEnd();
            // Collapse internal whitespace runs to single spaces.
            var collapsed = new StringBuilder(stripped.Length);
            bool prevSpace = false;
            foreach (char c in stripped)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!prevSpace)
                        collapsed.Append(' ');
                    prevSpace = true;
                }
                else
                {
                    collapsed.Append(c);
                    prevSpace = false;
                }
            }
            return collapsed.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// Extract label literals from a WinForms form's *Form.Designer.cs (or
        /// the *Form.cs itself if no Designer.cs sibling exists — some forms
        /// are hand-coded without the designer). Returns the original-cased
        /// label strings in the order they appear. Duplicates ARE returned
        /// because a designer can legitimately emit the same label twice
        /// (e.g. two "Name:" group headers for distinct sections); the
        /// caller's normalised set takes care of de-duplication.
        ///
        /// Some designer files use `resources.GetString("label1.Text")`
        /// instead of inline literals. We resolve those by parsing the sibling
        /// .resx file when present so the WF inventory isn't silently under-
        /// counted for forms that route through ComponentResourceManager (the
        /// VS Designer's localisation-aware emit mode).
        /// </summary>
        public static IReadOnlyList<string> ExtractWfLabels(string designerCsPath)
        {
            try
            {
                if (!File.Exists(designerCsPath))
                    return Array.Empty<string>();
                string code = File.ReadAllText(designerCsPath);
                // Look up the sibling .resx so resources.GetString(...) calls
                // resolve to concrete strings. The .resx sits next to the
                // *Form.Designer.cs OR next to the *Form.cs (one of the two);
                // try both. Missing .resx is fine — the resolver just returns
                // an empty dictionary and `resources.GetString` calls produce
                // no labels.
                Dictionary<string, string>? resx = TryLoadSiblingResx(designerCsPath);
                return ExtractWfLabelsFromSource(code, resx);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Locate a sibling .resx file for a *.cs designer file and parse it
        /// into a name→value dictionary. Designer convention is the .resx
        /// shares the form's base name (e.g. `Foo.Designer.cs` ↔ `Foo.resx`).
        /// Returns null if no .resx exists.
        /// </summary>
        static Dictionary<string, string>? TryLoadSiblingResx(string csPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(csPath) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(csPath);
                // Strip ".Designer" if present so "Foo.Designer.cs" → "Foo".
                if (baseName.EndsWith(".Designer", StringComparison.Ordinal))
                    baseName = baseName.Substring(0, baseName.Length - ".Designer".Length);
                string candidate = Path.Combine(dir, baseName + ".resx");
                if (!File.Exists(candidate))
                    return null;
                var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                XDocument doc = XDocument.Load(candidate);
                foreach (XElement data in doc.Descendants("data"))
                {
                    XAttribute? nameAttr = data.Attribute("name");
                    XElement? valueEl = data.Element("value");
                    if (nameAttr == null || valueEl == null)
                        continue;
                    dict[nameAttr.Value] = valueEl.Value;
                }
                return dict;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// In-memory variant for unit tests — same logic as
        /// <see cref="ExtractWfLabels"/> but takes a source string. If
        /// <paramref name="resourceLookup"/> is provided, `resources.GetString("k")`
        /// callsites resolve to `resourceLookup["k"]` so .resx-backed
        /// localisations participate in the inventory.
        /// </summary>
        public static IReadOnlyList<string> ExtractWfLabelsFromSource(
            string sourceCode,
            IReadOnlyDictionary<string, string>? resourceLookup = null)
        {
            if (string.IsNullOrEmpty(sourceCode))
                return Array.Empty<string>();
            var labels = new List<string>();
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
            SyntaxNode root = tree.GetRoot();

            // Map: local-variable name (e.g. "this.label1") → host type name (e.g. "Label").
            // We build it by walking ObjectCreationExpressionSyntax and matching the
            // adjacent assignment / variable-declarator that anchored it. That way we
            // can later filter `xxx.Text = "..."` by whether `xxx` was constructed as a
            // recognised label-host type.
            var variableToHostType = BuildWfVariableHostMap(root);

            // ---- 1. Pattern: `this.foo.Text = "Hello";` (designer-style) ----
            //    or  `foo.Text = "Hello";` (rare but legal)
            // ---- 1b. Pattern: `this.foo.Text = resources.GetString("foo.Text");` ----
            //    (VS Designer's localisation-aware emit mode — resolved via the
            //     sibling .resx when one is provided.)
            // We collect string-literal assignments and resolve the LHS variable's
            // host type via the map.
            foreach (var assign in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!assign.OperatorToken.IsKind(SyntaxKind.EqualsToken))
                    continue;
                if (assign.Left is not MemberAccessExpressionSyntax memberAccess)
                    continue;
                if (memberAccess.Name.Identifier.Text != "Text")
                    continue;

                string? owner = ExtractWfOwnerVariable(memberAccess.Expression);
                if (owner == null)
                    continue;
                if (!variableToHostType.TryGetValue(owner, out string? hostType))
                    continue;
                if (!WfLabelHostTypes.Contains(hostType))
                    continue;

                // Case A: RHS is a string literal.
                if (assign.Right is LiteralExpressionSyntax literal &&
                    literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    string text = (string?)literal.Token.Value ?? "";
                    if (text.Length > 0)
                        labels.Add(text);
                    continue;
                }

                // Case B: RHS is `resources.GetString("key")` — look up in the
                // provided dictionary. We accept any callable whose method-name
                // identifier is `GetString` (handles both ComponentResourceManager
                // and ResourceManager subclasses) and a single string-literal arg.
                if (resourceLookup != null &&
                    assign.Right is InvocationExpressionSyntax inv &&
                    TryGetResourceKey(inv) is { } key &&
                    resourceLookup.TryGetValue(key, out string? resolved) &&
                    !string.IsNullOrEmpty(resolved))
                {
                    labels.Add(resolved);
                }
            }

            // ---- 2. Pattern: `new Label { Text = "Hello" }` (object initialiser) ----
            // Some hand-coded forms construct controls inline rather than
            // through the designer. We harvest those directly.
            foreach (var oce in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                string typeName = ExtractTypeName(oce.Type);
                if (!WfLabelHostTypes.Contains(typeName))
                    continue;
                if (oce.Initializer == null)
                    continue;
                foreach (var expr in oce.Initializer.Expressions)
                {
                    if (expr is not AssignmentExpressionSyntax init)
                        continue;
                    if (init.Left is not IdentifierNameSyntax id)
                        continue;
                    if (id.Identifier.Text != "Text")
                        continue;
                    if (init.Right is not LiteralExpressionSyntax lit)
                        continue;
                    if (!lit.IsKind(SyntaxKind.StringLiteralExpression))
                        continue;
                    string text = (string?)lit.Token.Value ?? "";
                    if (text.Length == 0)
                        continue;
                    labels.Add(text);
                }
            }

            return labels;
        }

        /// <summary>
        /// Walk the syntax tree once and build a map from declared variable
        /// names ("this.foo" or "foo") to the trailing identifier of the type
        /// they were constructed as. Used by the `.Text = "..."` filter so we
        /// only pick up labels on actual label-host controls (and not, e.g.,
        /// TextBox's Text-as-data).
        /// </summary>
        static Dictionary<string, string> BuildWfVariableHostMap(SyntaxNode root)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);

            // Pattern A: field declaration with initializer
            //   private System.Windows.Forms.Label label1 = new System.Windows.Forms.Label();
            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                string fieldType = ExtractTypeName(field.Declaration.Type);
                if (!WfLabelHostTypes.Contains(fieldType))
                {
                    // Even if the declared type isn't a recognised host, the initializer
                    // may still be one (rare) — fall through to expression-level scan.
                }
                foreach (var v in field.Declaration.Variables)
                {
                    string varName = v.Identifier.Text;
                    if (WfLabelHostTypes.Contains(fieldType))
                    {
                        // Designer-style: the declared type IS the host. Record both
                        // `name` and `this.name` so member-access on either spelling
                        // matches.
                        map[varName] = fieldType;
                        map["this." + varName] = fieldType;
                    }
                }
            }

            // Pattern B: assignment of `new X(...)` to a member-access target
            //   this.label1 = new System.Windows.Forms.Label();
            foreach (var assign in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!assign.OperatorToken.IsKind(SyntaxKind.EqualsToken))
                    continue;
                if (assign.Right is not ObjectCreationExpressionSyntax oce)
                    continue;
                string ctorType = ExtractTypeName(oce.Type);
                // Don't gate on WfLabelHostTypes here — we want a complete map so
                // later filters can decide. (Non-label types get filtered at lookup
                // time.)
                string? owner = ExtractWfOwnerVariable(assign.Left);
                if (owner == null)
                    continue;
                map[owner] = ctorType;
                // Also register the bare-identifier form so `foo.Text = "..."`
                // resolves the same as `this.foo.Text = "..."`.
                if (owner.StartsWith("this.", StringComparison.Ordinal))
                    map[owner.Substring("this.".Length)] = ctorType;
                else
                    map["this." + owner] = ctorType;
            }

            // Pattern C: local variable declaration with initializer
            //   var label = new Label { ... };
            foreach (var loc in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                foreach (var v in loc.Declaration.Variables)
                {
                    if (v.Initializer == null) continue;
                    if (v.Initializer.Value is not ObjectCreationExpressionSyntax oce) continue;
                    string ctorType = ExtractTypeName(oce.Type);
                    string varName = v.Identifier.Text;
                    map[varName] = ctorType;
                }
            }

            return map;
        }

        /// <summary>
        /// Render the LHS of a member-access (`this.foo`, `foo`) as its
        /// canonical string-key form so we can look it up in the host map.
        /// Returns null for anything more exotic (chained method-call results,
        /// element access, …) which we deliberately decline to handle.
        /// </summary>
        static string? ExtractWfOwnerVariable(ExpressionSyntax expr)
        {
            return expr switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax m when m.Expression is ThisExpressionSyntax
                    => "this." + m.Name.Identifier.Text,
                MemberAccessExpressionSyntax m when m.Expression is IdentifierNameSyntax
                    => ((IdentifierNameSyntax)m.Expression).Identifier.Text + "." + m.Name.Identifier.Text,
                _ => null,
            };
        }

        /// <summary>
        /// Recognise `resources.GetString("key")` (or any other
        /// invocation whose method-name is `GetString` and which carries a
        /// single string-literal argument) and return the literal key. The
        /// receiver-name match (`resources`) would be too brittle — the
        /// designer sometimes uses a different identifier — so we go by the
        /// method-name + single-string-literal-arg signature instead. Returns
        /// null if the invocation doesn't fit.
        /// </summary>
        static string? TryGetResourceKey(InvocationExpressionSyntax inv)
        {
            string methodName = inv.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => "",
            };
            if (methodName != "GetString")
                return null;
            if (inv.ArgumentList.Arguments.Count != 1)
                return null;
            if (inv.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax lit)
                return null;
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression))
                return null;
            return (string?)lit.Token.Value;
        }

        /// <summary>Pull the trailing identifier out of a TypeSyntax (e.g. <c>System.Windows.Forms.Button</c> → <c>Button</c>).</summary>
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
        /// Extract label literals from an Avalonia .axaml file.
        /// Returns the original-cased labels in document order. Skips:
        ///   - elements inside template containers (Style/DataTemplate/...)
        ///   - attribute values starting with `{` (markup extensions — bindings, resources)
        /// </summary>
        public static IReadOnlyList<string> ExtractAvLabels(string axamlPath)
        {
            try
            {
                if (!File.Exists(axamlPath))
                    return Array.Empty<string>();
                XDocument doc = XDocument.Load(axamlPath);
                return ExtractAvLabelsFromDocument(doc);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// In-memory variant for unit tests — same logic as
        /// <see cref="ExtractAvLabels"/> but takes an XDocument directly.
        /// </summary>
        public static IReadOnlyList<string> ExtractAvLabelsFromDocument(XDocument doc)
        {
            var labels = new List<string>();
            if (doc.Root == null)
                return labels;
            foreach (XElement el in doc.Descendants())
            {
                if (IsInsideTemplate(el))
                    continue;
                foreach (XAttribute attr in el.Attributes())
                {
                    if (!AvLabelAttributes.Contains(attr.Name.LocalName))
                        continue;
                    string value = attr.Value;
                    if (string.IsNullOrEmpty(value))
                        continue;
                    // Skip markup extensions: any value starting with `{` is a
                    // binding, resource lookup, or static reference. Avalonia
                    // also supports literal `{}` escape — e.g. `{}{actual text}`
                    // — but those are vanishingly rare in this codebase; we
                    // exclude them too rather than parse the escape, because
                    // for the gap-finding purpose they're never the human-
                    // readable label we're trying to harvest.
                    if (value[0] == '{')
                        continue;
                    labels.Add(value);
                }
            }
            return labels;
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
        /// Run the label diff over <paramref name="pairs"/>. Only pairs whose
        /// WfPath AND AvPath are both non-null AND exist on disk participate —
        /// orphans and pairing artifacts contribute no signal here (they're
        /// already surfaced in the density report's unmatched-counterparts
        /// sections).
        ///
        /// Returns rows ordered by `(WfOnlyLabels.Count desc, WfFormName asc)`
        /// so the biggest candidate-missing-field counts surface first.
        /// </summary>
        public static IReadOnlyList<LabelDiffRow> Scan(IReadOnlyList<EditorPair> pairs)
        {
            if (pairs == null) throw new ArgumentNullException(nameof(pairs));

            var rows = new ConcurrentBag<LabelDiffRow>();
            Parallel.ForEach(pairs, pair =>
            {
                if (pair.WfPath == null || pair.AvPath == null)
                    return;
                if (!File.Exists(pair.WfPath) || !File.Exists(pair.AvPath))
                    return;

                // Prefer the *Form.Designer.cs sibling — that's where the VS
                // designer emits the bulk of `.Text = "..."` initialisers.
                // Fall back to the *Form.cs itself when there's no designer
                // file (some forms are hand-coded).
                string? designerPath = TryFindDesignerSibling(pair.WfPath);
                IReadOnlyList<string> wfLabels;
                if (designerPath != null && File.Exists(designerPath))
                {
                    var fromDesigner = ExtractWfLabels(designerPath);
                    var fromForm = ExtractWfLabels(pair.WfPath);
                    // Concatenate — duplicates are OK; the normalisation map
                    // de-dupes them. Include both because hand-coded controls
                    // in the public partial complement the designer's set.
                    var combined = new List<string>(fromDesigner.Count + fromForm.Count);
                    combined.AddRange(fromDesigner);
                    combined.AddRange(fromForm);
                    wfLabels = combined;
                }
                else
                {
                    wfLabels = ExtractWfLabels(pair.WfPath);
                }
                IReadOnlyList<string> avLabels = ExtractAvLabels(pair.AvPath);

                var row = ComputeDiff(pair, wfLabels, avLabels);
                rows.Add(row);
            });

            return rows
                .OrderByDescending(r => r.WfOnlyLabels.Count)
                .ThenBy(r => r.Pair.WfFormName ?? r.Pair.AvViewName ?? "", StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Compute the diff for a single pair from already-extracted label
        /// lists. Exposed for unit-test convenience — production callers go
        /// through <see cref="Scan"/>.
        /// </summary>
        public static LabelDiffRow ComputeDiff(
            EditorPair pair,
            IReadOnlyList<string> wfLabels,
            IReadOnlyList<string> avLabels)
        {
            // Maps `normalised key -> first original-cased occurrence`. We
            // record the first occurrence so the report preserves casing /
            // punctuation rather than the lowercase-no-colon normalised form.
            var wfMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string lbl in wfLabels)
            {
                string key = Normalize(lbl);
                if (key.Length == 0)
                    continue;
                if (!wfMap.ContainsKey(key))
                    wfMap[key] = lbl;
            }
            var avMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string lbl in avLabels)
            {
                string key = Normalize(lbl);
                if (key.Length == 0)
                    continue;
                if (!avMap.ContainsKey(key))
                    avMap[key] = lbl;
            }

            var wfOnly = wfMap
                .Where(kv => !avMap.ContainsKey(kv.Key))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value)
                .ToList();
            var avOnly = avMap
                .Where(kv => !wfMap.ContainsKey(kv.Key))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value)
                .ToList();
            var common = wfMap
                .Where(kv => avMap.ContainsKey(kv.Key))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value)
                .ToList();

            return new LabelDiffRow(pair, wfOnly, avOnly, common);
        }

        /// <summary>Locate the *.Designer.cs file that sits next to a *Form.cs (or null).</summary>
        static string? TryFindDesignerSibling(string formCsPath)
        {
            string dir = Path.GetDirectoryName(formCsPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(formCsPath);
            string candidate = Path.Combine(dir, baseName + ".Designer.cs");
            return File.Exists(candidate) ? candidate : null;
        }

        /// <summary>
        /// Format the markdown body of the labels report (sans front-matter — that
        /// is added by <see cref="ReportWriter"/>). Layout:
        ///
        ///  1. Methodology header
        ///  2. Summary table with totals + top-20 WF-only counts
        ///  3. Per-pair `### &lt;FormName&gt;` sections (only rows with WfOnly.Count > 0,
        ///     sorted by WfOnly count descending)
        ///
        /// If <paramref name="densityRows"/> is supplied, each per-pair section
        /// cross-links to that pair's density verdict (provides quantitative
        /// context for the qualitative label list).
        ///
        /// <paramref name="densityReportLink"/> is the path (relative to this
        /// report's location) of the latest density baseline to cross-link from
        /// the "Top 20" section. Pass null/empty to suppress the link entirely;
        /// callers normally derive this from `FindLatestDensityReport(outDir)`.
        /// </summary>
        public static string FormatReport(
            IReadOnlyList<LabelDiffRow> rows,
            IReadOnlyList<DensityRow>? densityRows = null,
            string? densityReportLink = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            // Density lookup keyed by (WfFormName, AvViewName) so each labels
            // row can cite its density verdict / counts. Keyed by both to
            // disambiguate forms that pair with multiple views (e.g.
            // ClassForm ↔ {ClassEditorView, ClassFE6View}).
            var densityLookup = new Dictionary<(string?, string?), DensityRow>();
            if (densityRows != null)
            {
                foreach (var d in densityRows)
                    densityLookup[(d.Pair.WfFormName, d.Pair.AvViewName)] = d;
            }

            var sb = new StringBuilder();
            sb.Append("# Avalonia vs WinForms — Field Label Diff Sweep\n\n");
            sb.Append("This report extracts label literals from paired WinForms ↔ Avalonia\n");
            sb.Append("editors and lists, per pair, the labels present in the WinForms designer\n");
            sb.Append("but missing from the Avalonia counterpart. These are strong candidates\n");
            sb.Append("for **missing fields in the Avalonia migration** — qualitative follow-up\n");
            sb.Append("to the Phase 1 control-density sweep.\n\n");
            sb.Append("WinForms side: Roslyn extracts `.Text = \"...\"` assignments on\n");
            sb.Append("`Label`, `GroupBox`, `Button`, `CheckBox`, `RadioButton`, `TabPage`\n");
            sb.Append("controls (plus property-initialiser syntax for hand-coded forms, and\n");
            sb.Append("`resources.GetString(\"key\")` calls resolved via the sibling .resx).\n");
            sb.Append("Avalonia side: `XDocument` parses every view, harvests literal values from\n");
            sb.Append("`Text` / `Content` / `Header` / `ToolTip` / `ToolTip.Tip` / `Watermark`\n");
            sb.Append("attributes, skipping markup-extension values (`{Binding ...}`,\n");
            sb.Append("`{StaticResource ...}`) and elements nested inside template containers\n");
            sb.Append("(`Style`, `DataTemplate`, ...).\n\n");
            sb.Append("Normalisation collapses whitespace, strips trailing colons, removes mnemonic\n");
            sb.Append("markers (`&` for WF, `_` for AV), and lowercases — so `Name:` / `&Name` /\n");
            sb.Append("`_Name` / `Name` all collide to the same set key. Original casing is preserved\n");
            sb.Append("in the report's WF-only / AV-only / Common lists.\n\n");
            sb.Append("Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/LabelDiffScanner.cs`.\n");
            sb.Append("Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-labels --out=<path>`.\n\n");

            // ---- Summary ----
            int totalPairs = rows.Count;
            int totalWfOnly = rows.Sum(r => r.WfOnlyLabels.Count);
            int totalAvOnly = rows.Sum(r => r.AvOnlyLabels.Count);
            int totalCommon = rows.Sum(r => r.CommonLabels.Count);
            int pairsWithGap = rows.Count(r => r.WfOnlyLabels.Count > 0);
            sb.Append("## Summary\n\n");
            sb.Append("| Metric | Count |\n");
            sb.Append("|---|---:|\n");
            sb.Append("| Pairs scanned (both files exist) | ").Append(totalPairs).Append(" |\n");
            sb.Append("| Pairs with ≥1 WF-only label | ").Append(pairsWithGap).Append(" |\n");
            sb.Append("| Total WF-only labels | ").Append(totalWfOnly).Append(" |\n");
            sb.Append("| Total AV-only labels | ").Append(totalAvOnly).Append(" |\n");
            sb.Append("| Total common labels | ").Append(totalCommon).Append(" |\n");
            sb.Append('\n');

            // ---- Top-20 candidate-missing-field counts ----
            sb.Append("## Top 20 Forms by WF-only Label Count\n\n");
            sb.Append("Each row's WF-only count is the upper bound on missing fields in the AV view.\n");
            if (!string.IsNullOrEmpty(densityReportLink))
            {
                sb.Append("Cross-link to the [density sweep](")
                  .Append(densityReportLink)
                  .Append(") for quantitative context.\n\n");
            }
            else
            {
                sb.Append("See the latest density baseline (under `docs/avalonia-gaps/`) for quantitative context.\n\n");
            }
            sb.Append("| Rank | WF Form | AV View | WF-only | AV-only | Common |\n");
            sb.Append("|---:|---|---|---:|---:|---:|\n");
            int rank = 0;
            foreach (var r in rows
                .Where(r => r.WfOnlyLabels.Count > 0)
                .OrderByDescending(r => r.WfOnlyLabels.Count)
                .ThenBy(r => r.Pair.WfFormName ?? "", StringComparer.Ordinal)
                .Take(20))
            {
                rank++;
                sb.Append("| ").Append(rank)
                  .Append(" | `").Append(r.Pair.WfFormName ?? "—").Append('`')
                  .Append(" | `").Append(r.Pair.AvViewName ?? "—").Append('`')
                  .Append(" | ").Append(r.WfOnlyLabels.Count)
                  .Append(" | ").Append(r.AvOnlyLabels.Count)
                  .Append(" | ").Append(r.CommonLabels.Count)
                  .Append(" |\n");
            }
            sb.Append('\n');

            // ---- Per-pair sections (only those with WfOnly.Count > 0) ----
            sb.Append("## Per-pair WF-only Labels (gaps)\n\n");
            sb.Append("Sections sorted by WF-only count descending. Each label is rendered as a\n");
            sb.Append("backticked literal preserving the original casing/punctuation. Use these as\n");
            sb.Append("the per-form backlog for follow-up gap-fix PRs.\n\n");

            foreach (var r in rows
                .Where(r => r.WfOnlyLabels.Count > 0)
                .OrderByDescending(r => r.WfOnlyLabels.Count)
                .ThenBy(r => r.Pair.WfFormName ?? "", StringComparer.Ordinal))
            {
                string formName = r.Pair.WfFormName ?? r.Pair.AvViewName ?? "?";
                sb.Append("### ").Append(formName).Append('\n');
                sb.Append("WF labels: **").Append(r.WfOnlyLabels.Count + r.CommonLabels.Count)
                  .Append("** · AV labels: **").Append(r.AvOnlyLabels.Count + r.CommonLabels.Count)
                  .Append("** · WF-only: **").Append(r.WfOnlyLabels.Count)
                  .Append("** · AV-only: **").Append(r.AvOnlyLabels.Count)
                  .Append("** · Common: **").Append(r.CommonLabels.Count)
                  .Append("**");
                if (densityLookup.TryGetValue((r.Pair.WfFormName, r.Pair.AvViewName), out var d))
                {
                    sb.Append(" · Density verdict: **").Append(d.Verdict)
                      .Append("** (WF ").Append(d.WfControlCount)
                      .Append(" / AV ").Append(d.AvControlCount).Append(')');
                }
                sb.Append("\n\n");

                sb.Append("WF-only labels (candidates for missing fields in AV):\n\n");
                foreach (string lbl in r.WfOnlyLabels)
                {
                    sb.Append("- ").Append(RenderLabelLiteral(lbl)).Append('\n');
                }
                sb.Append('\n');

                if (r.AvOnlyLabels.Count > 0)
                {
                    sb.Append("AV-only labels (usually fine — layout polish or rewording):\n\n");
                    foreach (string lbl in r.AvOnlyLabels)
                    {
                        sb.Append("- ").Append(RenderLabelLiteral(lbl)).Append('\n');
                    }
                    sb.Append('\n');
                }
            }

            // Trim any trailing blank lines so the body ends in a single `\n`.
            // ReportWriter then adds at most one terminal newline, giving a
            // clean EOF with no `git diff --check`-flagged extra blank lines.
            // (Phase 1's density report had the same issue — fixed in #375's
            // follow-up commit `e803555e6`; we apply the same discipline here.)
            while (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
                sb.Length--;
            return sb.ToString();
        }

        /// <summary>
        /// Render a label literal as inline-code markdown safely.
        ///
        /// Three concerns:
        ///   1. Embedded backticks would break a single-backtick code span.
        ///      Use double-backticks with leading/trailing space per CommonMark.
        ///   2. Embedded CR/LF would break the markdown bullet (the renderer
        ///      sees the second line as a sibling bullet or paragraph). We
        ///      escape `\r` and `\n` as literal `\\r` / `\\n` sequences so the
        ///      whole label stays on one line. The original label is still
        ///      identifiable; readers can recover it by reversing the escape.
        ///   3. Very long labels (≥ 200 chars) bloat the report; we truncate
        ///      with an explicit ellipsis marker so the report stays scannable
        ///      while still telling the reader "there's a long explanatory
        ///      label here, dig into the designer file if you need the full
        ///      text".
        /// </summary>
        static string RenderLabelLiteral(string label)
        {
            // Step 1: escape embedded CR/LF so the bullet stays on one line.
            string escaped = label.Replace("\r", "\\r").Replace("\n", "\\n");

            // Step 2: truncate very long labels (these are typically
            // explanatory paragraphs, not field labels).
            const int MaxLen = 200;
            if (escaped.Length > MaxLen)
                escaped = escaped.Substring(0, MaxLen) + "… (truncated; see designer file)";

            // Step 3: choose the safe code-fence (double-backtick with padding
            // if the value itself contains a backtick).
            if (escaped.IndexOf('`') < 0)
                return "`" + escaped + "`";
            return "`` " + escaped + " ``";
        }

        /// <summary>
        /// Pick the latest density-sweep report (by filename date prefix) sitting
        /// next to <paramref name="labelsReportPath"/>. Returns the file name
        /// (no directory) so the link is portable across worktree paths. Returns
        /// null when no density report exists.
        ///
        /// Convention: reports are named `YYYY-MM-DD-{type}-sweep.md`. We pick
        /// the lexicographic max of `*-density-sweep.md` files because ISO-8601
        /// dates sort naturally as strings.
        /// </summary>
        public static string? FindLatestDensityReport(string labelsReportPath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(Path.GetFullPath(labelsReportPath));
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return null;
                string? latest = null;
                foreach (string path in Directory.EnumerateFiles(dir, "*-density-sweep.md", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(path);
                    if (latest == null || string.CompareOrdinal(name, latest) > 0)
                        latest = name;
                }
                return latest;
            }
            catch
            {
                return null;
            }
        }
    }
}
