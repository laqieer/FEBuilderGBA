// SPDX-License-Identifier: GPL-3.0-or-later
// FEBuilderGBA gap-sweep tooling (#374).
//
// This file declares the common types used by every gap-sweep scanner:
//   - the EditorPair record (one paired WinForms form + Avalonia view),
//   - the MatchMethod / Confidence enums that classify each pair,
//   - the PairMatcher entry point that discovers all pairs in the worktree.
//
// PairMatcher is intentionally pure and side-effect free: it does file-system
// globbing and string transforms only. No ROM, no Avalonia runtime, no
// reflection beyond reading ListParityHelper's already-public static API.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.GapSweep
{
    /// <summary>
    /// How a pair was discovered. Authoritative seeds from ListParityHelper get the
    /// highest confidence; heuristic fallbacks are flagged Medium; anything left
    /// over is an Orphan with Low confidence.
    /// </summary>
    public enum MatchMethod
    {
        /// <summary>Seed from ListParityHelper.GetAllMappedEditors().</summary>
        ListParityHelper,
        /// <summary>Strip-suffix + candidate-name fallback.</summary>
        Heuristic,
        /// <summary>One side present, the other missing.</summary>
        Orphan,
    }

    /// <summary>How much to trust the pair-match decision.</summary>
    public enum Confidence
    {
        High,
        Medium,
        Low,
    }

    /// <summary>
    /// A paired (or half-paired) editor discovered by PairMatcher.
    /// Either side may be null for orphan rows; never both at once.
    /// </summary>
    /// <param name="WfFormName">Bare class name (e.g. "UnitForm"), or null if no WinForms side.</param>
    /// <param name="WfPath">Absolute path to the WinForms *Form.cs (the public partial, not Designer.cs), or null.</param>
    /// <param name="AvViewName">Bare AXAML view name (e.g. "UnitEditorView"), or null if no Avalonia side.</param>
    /// <param name="AvPath">Absolute path to the *View.axaml file, or null.</param>
    /// <param name="Match">How the pair was discovered.</param>
    /// <param name="Confidence">How much to trust the pairing.</param>
    public record EditorPair(
        string? WfFormName,
        string? WfPath,
        string? AvViewName,
        string? AvPath,
        MatchMethod Match,
        Confidence Confidence);

    /// <summary>
    /// Discovers WinForms ↔ Avalonia editor pairs by combining the authoritative
    /// ListParityHelper map with file-system discovery plus a heuristic fallback.
    /// </summary>
    public static class PairMatcher
    {
        /// <summary>
        /// Main forms are skipped: they are top-level shells, not the per-domain
        /// editors we want to compare. Match by base class name (no extension).
        /// </summary>
        static readonly HashSet<string> ExcludedFormNames = new(StringComparer.Ordinal)
        {
            "MainFE0Form", "MainFE6Form", "MainFE7Form", "MainFE8Form", "MainSimpleMenuForm",
        };

        /// <summary>
        /// MainWindow.axaml is the Avalonia shell, not a per-domain editor.
        /// </summary>
        static readonly HashSet<string> ExcludedViewNames = new(StringComparer.Ordinal)
        {
            "MainWindow",
        };

        /// <summary>
        /// Walk the repo root and produce every WinForms ↔ Avalonia editor pair.
        /// The pair list is deterministic (ordered by primary name) so reports
        /// diff cleanly between runs.
        /// </summary>
        public static IReadOnlyList<EditorPair> DiscoverAll(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot))
                throw new ArgumentException("repoRoot must be non-empty", nameof(repoRoot));
            if (!Directory.Exists(repoRoot))
                throw new DirectoryNotFoundException($"repo root not found: {repoRoot}");

            // ---- 1. Glob the WinForms form files ----
            // We pair each *Form.cs with its *Form.Designer.cs (if any). The
            // Designer.cs files are NOT separate forms — they are sibling files
            // for the same partial class.
            var wfForms = new Dictionary<string, string>(StringComparer.Ordinal);
            string wfRoot = Path.Combine(repoRoot, "FEBuilderGBA");
            if (Directory.Exists(wfRoot))
            {
                foreach (string path in Directory.EnumerateFiles(wfRoot, "*Form.cs", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(path);
                    // Skip *.Designer.cs — they live alongside the partial class and
                    // are not standalone forms.
                    if (fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string formName = Path.GetFileNameWithoutExtension(fileName);
                    if (ExcludedFormNames.Contains(formName))
                        continue;

                    wfForms[formName] = path;
                }
            }

            // ---- 2. Glob the Avalonia view files ----
            var avViews = new Dictionary<string, string>(StringComparer.Ordinal);
            string avRoot = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views");
            if (Directory.Exists(avRoot))
            {
                foreach (string path in Directory.EnumerateFiles(avRoot, "*View.axaml", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(path);
                    string viewName = Path.GetFileNameWithoutExtension(fileName);
                    if (ExcludedViewNames.Contains(viewName))
                        continue;
                    avViews[viewName] = path;
                }
            }

            // Track which side has already been consumed so each form/view appears
            // in exactly one pair.
            var consumedForms = new HashSet<string>(StringComparer.Ordinal);
            var consumedViews = new HashSet<string>(StringComparer.Ordinal);
            var pairs = new List<EditorPair>();

            // ---- 3. Seed authoritative pairs from ListParityHelper ----
            foreach (string avName in ListParityHelper.GetAllMappedEditors())
            {
                if (ExcludedViewNames.Contains(avName))
                    continue;

                var mapping = ListParityHelper.GetMapping(avName);
                if (mapping is not { } m)
                    continue;
                string formName = m.FormType;
                if (ExcludedFormNames.Contains(formName))
                    continue;

                wfForms.TryGetValue(formName, out string? wfPath);
                avViews.TryGetValue(avName, out string? avPath);

                pairs.Add(new EditorPair(
                    WfFormName: formName,
                    WfPath: wfPath,
                    AvViewName: avName,
                    AvPath: avPath,
                    Match: MatchMethod.ListParityHelper,
                    Confidence: Confidence.High));

                consumedForms.Add(formName);
                consumedViews.Add(avName);
            }

            // ---- 4. Heuristic fallback for unmatched WinForms ----
            foreach ((string formName, string formPath) in wfForms.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (consumedForms.Contains(formName))
                    continue;

                string? matchedAvName = TryHeuristicMatch(formName, avViews, consumedViews);
                if (matchedAvName != null)
                {
                    pairs.Add(new EditorPair(
                        WfFormName: formName,
                        WfPath: formPath,
                        AvViewName: matchedAvName,
                        AvPath: avViews[matchedAvName],
                        Match: MatchMethod.Heuristic,
                        Confidence: Confidence.Medium));
                    consumedForms.Add(formName);
                    consumedViews.Add(matchedAvName);
                }
            }

            // ---- 5. WinForms-side orphans ----
            foreach ((string formName, string formPath) in wfForms.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (consumedForms.Contains(formName))
                    continue;

                pairs.Add(new EditorPair(
                    WfFormName: formName,
                    WfPath: formPath,
                    AvViewName: null,
                    AvPath: null,
                    Match: MatchMethod.Orphan,
                    Confidence: Confidence.Low));
                consumedForms.Add(formName);
            }

            // ---- 6. Avalonia-side orphans ----
            foreach ((string viewName, string viewPath) in avViews.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (consumedViews.Contains(viewName))
                    continue;

                pairs.Add(new EditorPair(
                    WfFormName: null,
                    WfPath: null,
                    AvViewName: viewName,
                    AvPath: viewPath,
                    Match: MatchMethod.Orphan,
                    Confidence: Confidence.Low));
                consumedViews.Add(viewName);
            }

            // Stable ordering: paired rows first (by WF form name), then orphans.
            return pairs
                .OrderBy(p => p.Match == MatchMethod.Orphan ? 1 : 0)
                .ThenBy(p => p.WfFormName ?? p.AvViewName ?? "", StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Strip the "Form" suffix from a WinForms form name and try matching against
        /// the Avalonia view set with each conventional view suffix in turn.
        /// Returns the first un-consumed match, or null.
        /// </summary>
        static string? TryHeuristicMatch(
            string formName,
            IReadOnlyDictionary<string, string> avViews,
            HashSet<string> consumedViews)
        {
            string baseName = formName.EndsWith("Form", StringComparison.Ordinal)
                ? formName.Substring(0, formName.Length - "Form".Length)
                : formName;

            // Order matters: more-specific suffixes first, so "ItemForm" → "ItemEditorView"
            // takes precedence over "ItemView" if both happen to exist.
            string[] candidates =
            {
                baseName + "EditorView",
                baseName + "ViewerView",
                baseName + "View",
            };

            foreach (string candidate in candidates)
            {
                if (consumedViews.Contains(candidate))
                    continue;
                if (avViews.ContainsKey(candidate))
                    return candidate;
            }
            return null;
        }
    }
}
