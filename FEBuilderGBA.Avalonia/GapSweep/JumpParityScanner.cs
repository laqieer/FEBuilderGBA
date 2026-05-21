// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374) — Phase 4: headless jump/navigation parity.
//
// The legacy WinForms GUI navigates between editors via
// `InputFormRef.JumpForm<TForm>(addressOrId)` callsites in form code-behind.
// The Avalonia GUI uses `WindowManager.Navigate<TView>(addr)` instead, but the
// migration was "vibe coded" and several jumps were dropped, mis-wired, or
// pass the wrong address (issues #359, #360, #362, #363, #365).
//
// This scanner produces a parity report by:
//
//   1. Roslyn-scanning `FEBuilderGBA/**/*.cs` (the WinForms project ONLY) for
//      every `InputFormRef.JumpForm<T>(…)` or `JumpFormLow<T>(…)` callsite.
//      For each, the enclosing class declaration name is the source form, and
//      the generic type argument identifies the target form.
//
//   2. Reflecting over the currently-loaded `FEBuilderGBA.Avalonia` assembly
//      for every concrete class implementing `INavigationTargetSource`. The
//      scanner instantiates each (parameterless ctor only — wrapped in
//      try/catch to survive VM-side construction failures) and reads its
//      declarative jump manifest.
//
//   3. Cross-referencing the two sides via `ListParityHelper.GetMapping(name)`
//      to map AV view names to their WF form counterparts.
//
// The output is a `| Source | Target WF | Target AV | Status | Issue |` table
// that surfaces:
//
//   - `Match`              — WF callsite exists AND AV manifest exists; healthy.
//   - `MissingAvManifest`  — WF callsite has no matching AV manifest row; the
//                            biggest backlog signal — these are the jumps
//                            still pending in the AV migration.
//   - `NoWfCallsite`       — AV manifest declares a jump WF doesn't have; usually
//                            fine (richer AV UX) but worth a manual look.
//   - `KnownGap`           — AV manifest has a non-null IssueRef; tracked-broken
//                            jumps are intentionally NOT regressions yet.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.GapSweep
{
    /// <summary>
    /// Classification of one jump-parity row. The naming is deliberately neutral
    /// (no "OK" / "BROKEN") because the same WF callsite might be intentionally
    /// dropped from AV (e.g. an FE7-only menu that AV consolidated), and the
    /// same AV manifest entry might be a richer feature WF never had.
    /// </summary>
    public enum JumpRowStatus
    {
        /// <summary>WF callsite + AV manifest BOTH exist for the same (source, target) pair.</summary>
        Match,
        /// <summary>WF callsite exists but no matching AV manifest entry — the backlog.</summary>
        MissingAvManifest,
        /// <summary>AV manifest declares a jump that has no matching WF callsite.</summary>
        NoWfCallsite,
        /// <summary>AV manifest row carries an `IssueRef` — tracked-broken.</summary>
        KnownGap,
    }

    /// <summary>
    /// One row of the jump-parity report. SourceForm/SourceView is the editor
    /// originating the jump; TargetWfType/TargetAvType is the target editor.
    /// One side of each pair may be empty when the row's Status is
    /// MissingAvManifest (no TargetAvType) or NoWfCallsite (no TargetWfType).
    /// </summary>
    /// <param name="SourceForm">
    /// WinForms enclosing class name (e.g. "ItemForm") — the form that calls
    /// JumpForm&lt;T&gt;. Empty string when the row originates from an AV
    /// manifest with no matching WF callsite.
    /// </param>
    /// <param name="SourceView">
    /// Avalonia View class name (e.g. "ItemEditorView") — the View whose
    /// code-behind hosts the corresponding `WindowManager.Navigate&lt;T&gt;`
    /// call. Empty when the row originates from a WF callsite that has no
    /// AV counterpart (orphan WF form).
    /// </param>
    /// <param name="Command">
    /// VM-supplied stable command identifier (e.g. "JumpToMoveCost"). Empty
    /// for MissingAvManifest rows that have no AV manifest entry.
    /// </param>
    /// <param name="TargetWfType">
    /// WinForms target form class name (e.g. "MoveCostForm"). Empty when the
    /// row originates from an AV manifest with no WF callsite.
    /// </param>
    /// <param name="TargetAvType">
    /// Avalonia target View class name (e.g. "MoveCostEditorView"). Empty when
    /// the row originates from a WF callsite with no matching AV manifest
    /// entry — Phase 4's biggest backlog signal.
    /// </param>
    /// <param name="Status">Classification — see <see cref="JumpRowStatus"/>.</param>
    /// <param name="IssueRef">Issue tag for known-gap rows; null otherwise.</param>
    public record JumpRow(
        string SourceForm,
        string SourceView,
        string Command,
        string TargetWfType,
        string TargetAvType,
        JumpRowStatus Status,
        string? IssueRef);

    /// <summary>
    /// One WinForms `InputFormRef.JumpForm&lt;T&gt;(...)` callsite extracted by
    /// the Roslyn scan. Documentation-only; never persisted across runs.
    /// </summary>
    /// <param name="SourceForm">Enclosing class declaration name.</param>
    /// <param name="TargetForm">Generic type argument (e.g. "ClassForm").</param>
    /// <param name="HasAddressArgument">
    /// True when the call passes at least one argument (the address / item id).
    /// False for parameterless calls like `JumpForm&lt;PatchForm&gt;()` which
    /// open the editor without preselecting a row.
    /// </param>
    public record WfJumpCallsite(string SourceForm, string TargetForm, bool HasAddressArgument);

    /// <summary>
    /// One AV manifest entry extracted by the reflection scan. SourceView is
    /// the VM type's paired View (derived by suffix convention or via the
    /// ListParityHelper map). TargetView is the manifest's declared
    /// TargetViewType. Documentation-only; never persisted across runs.
    /// </summary>
    public record AvManifestEntry(
        string SourceVm,
        string SourceView,
        string Command,
        string TargetView,
        string? IssueRef);

    /// <summary>
    /// Phase 4 scanner: cross-references WinForms `InputFormRef.JumpForm` callsites
    /// against Avalonia `INavigationTargetSource` manifests and emits parity rows.
    /// </summary>
    public static class JumpParityScanner
    {
        /// <summary>
        /// Run the full scan against <paramref name="repoRoot"/>. Returns a
        /// deterministically-ordered list of <see cref="JumpRow"/>s suitable
        /// for both the markdown report and the xunit Theory data source.
        /// </summary>
        public static IReadOnlyList<JumpRow> Scan(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot))
                throw new ArgumentException("repoRoot must be non-empty", nameof(repoRoot));
            // Missing repo root is a soft failure — we still scan the AV side
            // (via reflection on the loaded assembly) and emit those rows.
            // The WF callsites simply come up empty.
            IReadOnlyList<WfJumpCallsite> wfCallsites = Directory.Exists(repoRoot)
                ? ScanWfCallsites(Path.Combine(repoRoot, "FEBuilderGBA"))
                : Array.Empty<WfJumpCallsite>();

            IReadOnlyList<AvManifestEntry> avManifests = ScanAvManifests(typeof(INavigationTargetSource).Assembly);

            return ComputeJumpRows(wfCallsites, avManifests);
        }

        /// <summary>
        /// Roslyn-scan every `.cs` file under <paramref name="wfProjectRoot"/>
        /// for `InputFormRef.JumpForm&lt;T&gt;(…)` or `JumpFormLow&lt;T&gt;(…)`
        /// invocations. Returns one entry per callsite (duplicates are kept —
        /// the caller's pair logic de-dupes by (Source, Target) key).
        ///
        /// We walk up the syntax tree to find the enclosing class declaration
        /// for the source-form attribution. Partial classes split across files
        /// still attribute correctly because the class name comes from the
        /// declaration, not the file name.
        /// </summary>
        public static IReadOnlyList<WfJumpCallsite> ScanWfCallsites(string wfProjectRoot)
        {
            if (!Directory.Exists(wfProjectRoot))
                return Array.Empty<WfJumpCallsite>();

            var callsites = new ConcurrentBag<WfJumpCallsite>();
            // We're CPU-bound (Roslyn parse) and per-file independent — parallel
            // is safe.
            string[] files = Directory.GetFiles(wfProjectRoot, "*.cs", SearchOption.AllDirectories);
            Parallel.ForEach(files, file =>
            {
                try
                {
                    string code = File.ReadAllText(file);
                    foreach (var c in ExtractCallsitesFromSource(code))
                        callsites.Add(c);
                }
                catch
                {
                    // File-level scan failures are tolerated — a single
                    // unparseable file shouldn't kill the whole sweep.
                }
            });
            return callsites
                .OrderBy(c => c.SourceForm, StringComparer.Ordinal)
                .ThenBy(c => c.TargetForm, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Extract `InputFormRef.JumpForm&lt;T&gt;` and `JumpFormLow&lt;T&gt;`
        /// callsites from in-memory source. Exposed for unit testing without
        /// touching the file system.
        /// </summary>
        public static IReadOnlyList<WfJumpCallsite> ExtractCallsitesFromSource(string sourceCode)
        {
            if (string.IsNullOrEmpty(sourceCode))
                return Array.Empty<WfJumpCallsite>();

            var list = new List<WfJumpCallsite>();
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
            SyntaxNode root = tree.GetRoot();

            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                // Recognise:
                //   InputFormRef.JumpForm<TargetForm>(...)
                //   InputFormRef.JumpFormLow<TargetForm>(...)
                // Reject bare `JumpForm<T>(...)` (no receiver) because the only
                // declaration site has the InputFormRef receiver. The reject
                // keeps the scanner narrow.
                if (inv.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;
                string methodName = memberAccess.Name switch
                {
                    GenericNameSyntax gn => gn.Identifier.Text,
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => "",
                };
                if (methodName != "JumpForm" && methodName != "JumpFormLow")
                    continue;

                // Receiver must be `InputFormRef` (qualified or unqualified).
                string receiverName = memberAccess.Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                    _ => "",
                };
                if (receiverName != "InputFormRef")
                    continue;

                // Extract the generic type argument.
                if (memberAccess.Name is not GenericNameSyntax gname)
                    continue;
                if (gname.TypeArgumentList.Arguments.Count == 0)
                    continue;
                string targetForm = ExtractTypeName(gname.TypeArgumentList.Arguments[0]);
                if (string.IsNullOrEmpty(targetForm))
                    continue;

                // Walk up to find the enclosing class declaration.
                string sourceForm = FindEnclosingClassName(inv);
                if (string.IsNullOrEmpty(sourceForm))
                    continue;

                bool hasAddress = inv.ArgumentList.Arguments.Count > 0;
                list.Add(new WfJumpCallsite(sourceForm, targetForm, hasAddress));
            }
            return list;
        }

        /// <summary>
        /// Walk up the syntax tree until we hit a ClassDeclarationSyntax — that
        /// node's identifier is the source-form name. Returns empty string when
        /// the invocation is not inside any class declaration (top-level
        /// statements, etc.) which would be impossible for legitimate WF
        /// callsites but is defensive against parse errors.
        /// </summary>
        static string FindEnclosingClassName(SyntaxNode node)
        {
            for (SyntaxNode? cur = node.Parent; cur != null; cur = cur.Parent)
            {
                if (cur is ClassDeclarationSyntax cls)
                    return cls.Identifier.Text;
            }
            return "";
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
        /// Reflect over <paramref name="avAssembly"/> for every concrete class
        /// implementing <see cref="INavigationTargetSource"/>. Instantiates each
        /// with a parameterless constructor and reads its manifest. VMs whose
        /// constructor throws (e.g. they access CoreState.ROM which might be
        /// null) are silently skipped — the scanner is supposed to be robust
        /// against incomplete VM construction.
        ///
        /// Conventionally, the SourceView name is derived from the VM type:
        /// <c>ClassEditorViewModel</c> → <c>ClassEditorView</c>. This convention
        /// matches the codebase's actual naming (see Views/*.axaml.cs). VMs
        /// that violate the convention get an empty SourceView and surface as
        /// an inspectable diagnostic row rather than crashing.
        /// </summary>
        public static IReadOnlyList<AvManifestEntry> ScanAvManifests(Assembly avAssembly)
        {
            if (avAssembly == null) throw new ArgumentNullException(nameof(avAssembly));
            var entries = new List<AvManifestEntry>();

            Type[] allTypes;
            try
            {
                allTypes = avAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Some types might fail to load (missing dependencies in test
                // environments) — work with what we got.
                allTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            foreach (Type t in allTypes)
            {
                if (t.IsAbstract || t.IsInterface)
                    continue;
                if (!typeof(INavigationTargetSource).IsAssignableFrom(t))
                    continue;

                object? instance = TryInstantiateVm(t);
                if (instance is not INavigationTargetSource src)
                    continue;

                IReadOnlyList<NavigationTarget>? targets = null;
                try { targets = src.GetNavigationTargets(); }
                catch
                {
                    // Manifest enumeration shouldn't fail, but if it does we
                    // just skip the VM — same rationale as ctor failure.
                    continue;
                }
                if (targets == null || targets.Count == 0)
                    continue;

                string sourceView = DeriveViewNameFromVmName(t.Name);
                foreach (var nt in targets)
                {
                    entries.Add(new AvManifestEntry(
                        SourceVm: t.Name,
                        SourceView: sourceView,
                        Command: nt.CommandName ?? "",
                        TargetView: nt.TargetViewType?.Name ?? "",
                        IssueRef: nt.IssueRef));
                }
            }

            return entries
                .OrderBy(e => e.SourceView, StringComparer.Ordinal)
                .ThenBy(e => e.Command, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Best-effort VM instantiation. The Phase 4 manifest contract requires
        /// implementers to construct without side effects, so we use the
        /// parameterless ctor only and wrap the whole call in a try/catch — a
        /// VM whose construction blows up gets silently skipped. This is the
        /// "graceful degradation" the manifest contract calls out.
        /// </summary>
        static object? TryInstantiateVm(Type t)
        {
            try
            {
                ConstructorInfo? ctor = t.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (ctor == null)
                    return null;
                return ctor.Invoke(null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Map a VM type name to its conventional paired View name. The mapping
        /// is `XxxViewModel` → `XxxView`. Names that don't end in `ViewModel`
        /// fall through and return the input unchanged (a defensive diagnostic
        /// signal — that VM doesn't follow the convention and the cross-ref
        /// will surface it as either a NoWfCallsite row or no row at all).
        /// </summary>
        public static string DeriveViewNameFromVmName(string vmTypeName)
        {
            if (string.IsNullOrEmpty(vmTypeName))
                return "";
            const string suffix = "ViewModel";
            if (vmTypeName.EndsWith(suffix, StringComparison.Ordinal))
            {
                string baseName = vmTypeName.Substring(0, vmTypeName.Length - suffix.Length);
                return baseName + "View";
            }
            return vmTypeName;
        }

        /// <summary>
        /// Compute the cross-product of WF callsites × AV manifests and emit
        /// the classified rows. Exposed for unit testing — production callers
        /// go through <see cref="Scan"/>.
        ///
        /// Pairing strategy:
        /// <list type="bullet">
        ///   <item>Each WF callsite has a SourceForm (e.g. "ItemForm") and
        ///     TargetForm (e.g. "ItemEffectivenessForm"). We look up the AV
        ///     counterpart via <see cref="ListParityHelper"/>'s inverse map
        ///     (form→view) — concretely, by finding any mapped editor whose
        ///     WF form name equals the source/target.</item>
        ///   <item>Each AV manifest has SourceView (e.g. "ItemEditorView") and
        ///     TargetView. We look up the WF counterpart via
        ///     <see cref="ListParityHelper.GetMapping"/>.</item>
        ///   <item>A pair MATCHes when (source-view, target-view) appear on
        ///     both sides — regardless of which view was the "primary" key.</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<JumpRow> ComputeJumpRows(
            IReadOnlyList<WfJumpCallsite> wfCallsites,
            IReadOnlyList<AvManifestEntry> avManifests)
        {
            // Build inverse WF-form → AV-view name map by walking the
            // ListParityHelper full mapping list. The helper's authoritative
            // direction is AV-view → WF-form; we invert here once.
            // Note: multiple AV views can map to the same WF form
            // (e.g. ClassForm ↔ {ClassEditorView, ClassFE6View}); we collect
            // ALL such views so callers can find the right one when needed.
            var formToViews = BuildWfFormToAvViewsMap();

            // Index AV manifests by source-view for quick lookup during the
            // cross-ref pass. One source view may have many entries (one per
            // exposed jump).
            var avRowsBySourceView = new Dictionary<string, List<AvManifestEntry>>(StringComparer.Ordinal);
            foreach (var av in avManifests)
            {
                if (!avRowsBySourceView.TryGetValue(av.SourceView, out var list))
                {
                    list = new List<AvManifestEntry>();
                    avRowsBySourceView[av.SourceView] = list;
                }
                list.Add(av);
            }

            // Expand each WF callsite into one or more AV-coordinate pairs.
            // (One WF form can have many AV view counterparts and vice-versa.)
            // For each WF (sourceForm, targetForm) pair, we emit one expanded
            // row per (sourceView, targetView) combination — that row starts
            // life as MissingAvManifest and may be upgraded to Match or
            // KnownGap if a matching AV manifest row is found.
            var wfExpandedRows = new List<JumpRow>();
            foreach (var wf in wfCallsites)
            {
                IReadOnlyList<string> sourceViews = formToViews.TryGetValue(wf.SourceForm, out var sv)
                    ? sv
                    : (IReadOnlyList<string>)new[] { "" };
                IReadOnlyList<string> targetViews = formToViews.TryGetValue(wf.TargetForm, out var tv)
                    ? tv
                    : (IReadOnlyList<string>)new[] { "" };

                foreach (string sourceView in sourceViews)
                foreach (string targetView in targetViews)
                {
                    wfExpandedRows.Add(new JumpRow(
                        SourceForm: wf.SourceForm,
                        SourceView: sourceView,
                        Command: "",
                        TargetWfType: wf.TargetForm,
                        TargetAvType: targetView,
                        Status: JumpRowStatus.MissingAvManifest, // may upgrade below
                        IssueRef: null));
                }
            }

            // Cross-reference: for each WF expanded row, look for an AV
            // manifest entry with the same (sourceView, targetView). Track
            // matched AV entries by identity so the AV-only second pass below
            // doesn't double-count them.
            var matchedWfIndices = new HashSet<int>();
            var matchedAvEntries = new HashSet<AvManifestEntry>();
            var rows = new List<JumpRow>();
            for (int i = 0; i < wfExpandedRows.Count; i++)
            {
                var wfRow = wfExpandedRows[i];
                if (!avRowsBySourceView.TryGetValue(wfRow.SourceView, out var avForSource))
                    continue;
                for (int j = 0; j < avForSource.Count; j++)
                {
                    var av = avForSource[j];
                    if (av.TargetView != wfRow.TargetAvType)
                        continue;
                    // Match. If the AV side carries an IssueRef, surface it
                    // as KnownGap — the WF callsite agrees on the (source,
                    // target) pair but the AV behavior is still tracked-broken.
                    var matchedRow = wfRow with
                    {
                        Command = av.Command,
                        IssueRef = av.IssueRef,
                        Status = av.IssueRef != null
                            ? JumpRowStatus.KnownGap
                            : JumpRowStatus.Match,
                    };
                    rows.Add(matchedRow);
                    matchedWfIndices.Add(i);
                    matchedAvEntries.Add(av);
                }
            }

            // Add remaining WF callsites as MissingAvManifest rows.
            for (int i = 0; i < wfExpandedRows.Count; i++)
            {
                if (matchedWfIndices.Contains(i))
                    continue;
                rows.Add(wfExpandedRows[i]);
            }

            // Add remaining AV manifest entries (those with no WF cross-ref) as
            // KnownGap (if IssueRef) or NoWfCallsite rows.
            foreach (var av in avManifests)
            {
                if (matchedAvEntries.Contains(av))
                    continue;
                // Look up the WF form name that maps to av.TargetView (if any).
                string wfTarget = "";
                foreach (var kv in formToViews)
                {
                    if (kv.Value.Contains(av.TargetView))
                    {
                        wfTarget = kv.Key;
                        break;
                    }
                }
                var status = av.IssueRef != null
                    ? JumpRowStatus.KnownGap
                    : JumpRowStatus.NoWfCallsite;
                string sourceForm = LookupWfFormForAvView(av.SourceView);
                rows.Add(new JumpRow(
                    SourceForm: sourceForm,
                    SourceView: av.SourceView,
                    Command: av.Command,
                    TargetWfType: wfTarget,
                    TargetAvType: av.TargetView,
                    Status: status,
                    IssueRef: av.IssueRef));
            }

            // Deterministic order: Status ascending (Match < MissingAvManifest <
            // NoWfCallsite < KnownGap so review reads "good rows first, then
            // the backlog"), then by source view, then by target.
            return rows
                .OrderBy(r => (int)r.Status)
                .ThenBy(r => r.SourceView ?? "", StringComparer.Ordinal)
                .ThenBy(r => r.SourceForm ?? "", StringComparer.Ordinal)
                .ThenBy(r => r.TargetAvType ?? "", StringComparer.Ordinal)
                .ThenBy(r => r.TargetWfType ?? "", StringComparer.Ordinal)
                .ThenBy(r => r.Command ?? "", StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Build a WF-form-name → list-of-AV-view-names map. Used to expand WF
        /// callsites whose (source, target) form pair has multiple AV view
        /// counterparts (e.g. ClassForm ↔ ClassEditorView AND ClassFE6View).
        /// Uses <see cref="ListParityHelper.GetAllMappedEditors"/> as the
        /// authoritative source — no reflection / no string parsing.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildWfFormToAvViewsMap()
        {
            var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string avName in ListParityHelper.GetAllMappedEditors())
            {
                var mapping = ListParityHelper.GetMapping(avName);
                if (mapping is not { } m)
                    continue;
                if (!map.TryGetValue(m.FormType, out var list))
                {
                    list = new List<string>();
                    map[m.FormType] = list;
                }
                if (!list.Contains(avName))
                    list.Add(avName);
            }
            return map.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value,
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Inverse lookup: given an AV view name, return the WF form name that
        /// maps to it (or empty string if no mapping exists). Used to surface
        /// the WF source-form column for AV-only rows.
        /// </summary>
        static string LookupWfFormForAvView(string avViewName)
        {
            var m = ListParityHelper.GetMapping(avViewName);
            return m is { } mapping ? mapping.FormType : "";
        }

        /// <summary>
        /// Format the markdown body of the jumps report (sans front-matter —
        /// the caller adds it via <see cref="ReportWriter"/>). Layout:
        ///
        /// <list type="number">
        ///   <item>Methodology header</item>
        ///   <item>Summary table with counts per Status</item>
        ///   <item>Top-of-report KnownGap table (issue links)</item>
        ///   <item>MissingAvManifest backlog table (the actionable rows)</item>
        ///   <item>NoWfCallsite table (less common, smaller)</item>
        ///   <item>Match table (for completeness, smaller)</item>
        /// </list>
        ///
        /// Uses LF line endings throughout (no CRLF) so committed reports
        /// don't churn on Windows checkouts.
        /// </summary>
        public static string FormatReport(IReadOnlyList<JumpRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            var sb = new StringBuilder();
            sb.Append("# Avalonia vs WinForms — Jump/Navigation Parity Sweep\n\n");
            sb.Append("This report cross-references WinForms `InputFormRef.JumpForm<T>(addr)`\n");
            sb.Append("callsites against Avalonia `INavigationTargetSource` manifests to\n");
            sb.Append("surface every cross-editor navigation gap.\n\n");
            sb.Append("**Methodology:**\n\n");
            sb.Append("- WinForms callsites: Roslyn scans `FEBuilderGBA/**/*.cs` for\n");
            sb.Append("  `InputFormRef.JumpForm<T>(…)` / `JumpFormLow<T>(…)` invocations.\n");
            sb.Append("  Each match records (enclosing-class, target-type) for cross-ref.\n");
            sb.Append("- Avalonia manifests: Reflection over the loaded Avalonia assembly\n");
            sb.Append("  for every concrete `INavigationTargetSource`. Each VM is instantiated\n");
            sb.Append("  via its parameterless constructor (wrapped in try/catch); the\n");
            sb.Append("  `GetNavigationTargets()` result feeds the cross-ref.\n");
            sb.Append("- Pairing: `ListParityHelper.GetMapping(name)` maps AV view names ↔\n");
            sb.Append("  WF form names so the two sides align without manual lookups.\n\n");
            sb.Append("**Status legend:**\n\n");
            sb.Append("- `Match` — WF callsite + AV manifest agree on the (source, target) pair.\n");
            sb.Append("- `MissingAvManifest` — WF has the jump, AV does not (the backlog).\n");
            sb.Append("- `NoWfCallsite` — AV manifest declares a jump WF doesn't have.\n");
            sb.Append("- `KnownGap` — AV manifest row carries an open-issue `IssueRef`.\n\n");
            sb.Append("Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/JumpParityScanner.cs`.\n");
            sb.Append("Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-jumps --out=<path>`.\n\n");

            // ---- Summary ----
            int totalRows = rows.Count;
            int countMatch = rows.Count(r => r.Status == JumpRowStatus.Match);
            int countMissing = rows.Count(r => r.Status == JumpRowStatus.MissingAvManifest);
            int countNoWf = rows.Count(r => r.Status == JumpRowStatus.NoWfCallsite);
            int countKnown = rows.Count(r => r.Status == JumpRowStatus.KnownGap);
            sb.Append("## Summary\n\n");
            sb.Append("| Metric | Count |\n");
            sb.Append("|---|---:|\n");
            sb.Append("| Total rows | ").Append(totalRows).Append(" |\n");
            sb.Append("| Match | ").Append(countMatch).Append(" |\n");
            sb.Append("| MissingAvManifest (backlog) | ").Append(countMissing).Append(" |\n");
            sb.Append("| NoWfCallsite | ").Append(countNoWf).Append(" |\n");
            sb.Append("| KnownGap (issue-tagged) | ").Append(countKnown).Append(" |\n\n");

            // ---- KnownGap rows ----
            AppendStatusTable(sb,
                rows.Where(r => r.Status == JumpRowStatus.KnownGap),
                heading: "## Known Gaps (tracked by open issues)",
                emptyMessage: "_None._",
                renderIssueColumn: true);

            // ---- MissingAvManifest rows ----
            AppendStatusTable(sb,
                rows.Where(r => r.Status == JumpRowStatus.MissingAvManifest),
                heading: "## Missing AV Manifest (backlog — WF has the jump, AV does not)",
                emptyMessage: "_None._",
                renderIssueColumn: false);

            // ---- NoWfCallsite rows ----
            AppendStatusTable(sb,
                rows.Where(r => r.Status == JumpRowStatus.NoWfCallsite),
                heading: "## No WinForms Callsite (AV-only manifest entries)",
                emptyMessage: "_None._",
                renderIssueColumn: false);

            // ---- Match rows ----
            AppendStatusTable(sb,
                rows.Where(r => r.Status == JumpRowStatus.Match),
                heading: "## Matches (WF + AV agree)",
                emptyMessage: "_None._",
                renderIssueColumn: false);

            // Trim trailing blank lines.
            while (sb.Length >= 2 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\n')
                sb.Length--;
            return sb.ToString();
        }

        /// <summary>
        /// Render a section table for one Status group. Empty groups still get
        /// a heading + "_None._" so the report layout stays stable across runs.
        /// </summary>
        static void AppendStatusTable(
            StringBuilder sb,
            IEnumerable<JumpRow> rows,
            string heading,
            string emptyMessage,
            bool renderIssueColumn)
        {
            sb.Append(heading).Append('\n').Append('\n');
            var list = rows.ToList();
            if (list.Count == 0)
            {
                sb.Append(emptyMessage).Append('\n').Append('\n');
                return;
            }
            if (renderIssueColumn)
            {
                sb.Append("| Source Form | Source View | Command | Target WF | Target AV | Issue |\n");
                sb.Append("|---|---|---|---|---|---|\n");
                foreach (var r in list)
                {
                    sb.Append("| `").Append(EscapeMd(r.SourceForm)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.SourceView)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.Command)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.TargetWfType)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.TargetAvType)).Append("` ")
                      .Append("| ").Append(RenderIssueLink(r.IssueRef)).Append(" |\n");
                }
            }
            else
            {
                sb.Append("| Source Form | Source View | Command | Target WF | Target AV |\n");
                sb.Append("|---|---|---|---|---|\n");
                foreach (var r in list)
                {
                    sb.Append("| `").Append(EscapeMd(r.SourceForm)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.SourceView)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.Command)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.TargetWfType)).Append("` ")
                      .Append("| `").Append(EscapeMd(r.TargetAvType)).Append("` |\n");
                }
            }
            sb.Append('\n');
        }

        /// <summary>
        /// Escape a string for safe inclusion inside a markdown inline-code
        /// span. Empty strings render as a literal em-dash so the table cell
        /// isn't visually empty.
        /// </summary>
        static string EscapeMd(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "—";
            // Inside a single-backtick span we just need to avoid embedded
            // backticks. None of our identifiers should contain them, but be
            // defensive — replace with the closest visual equivalent.
            return value.Replace("`", "'");
        }

        /// <summary>
        /// Render an issue reference as a clickable GitHub link. Accepts the
        /// canonical "#NNN" form; bare numbers also work for forgiveness.
        /// </summary>
        static string RenderIssueLink(string? issueRef)
        {
            if (string.IsNullOrEmpty(issueRef))
                return "—";
            string trimmed = issueRef.TrimStart('#');
            if (!int.TryParse(trimmed, out _))
                return EscapeMd(issueRef);
            return $"[#{trimmed}](https://github.com/laqieer/FEBuilderGBA/issues/{trimmed})";
        }
    }
}
