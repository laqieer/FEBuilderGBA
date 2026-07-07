// SPDX-License-Identifier: GPL-3.0-or-later
// #1891 — Anti-drift guard between the desktop MainWindow editor body and the shared
// EditorCatalog the single-view launcher renders. It parses MainWindow.axaml's body
// <Expander> buttons, resolves each Click handler in MainWindow.axaml.cs to the editor
// View type(s) it opens (all open verbs), and asserts every desktop-body editor type is
// present in EditorCatalog — except the documented, single-view-incompatible exclusions.
// If a future editor is added to the desktop home page but not the catalog, this fails.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class EditorCatalogParityTests
{
    // The ONLY desktop-body editors intentionally absent from the catalog: the 6 Window-derived
    // EventTemplate editors (EventTemplate{1..6}View : EventTemplateViewBase : TranslatedWindow)
    // throw NotSupportedException on a single-view (wasm/Android) host, so they stay desktop-only.
    static readonly HashSet<string> ExcludedDesktopEditors = new(StringComparer.Ordinal)
    {
        "EventTemplate1View", "EventTemplate2View", "EventTemplate3View",
        "EventTemplate4View", "EventTemplate5View", "EventTemplate6View",
    };

    // Body buttons that are actions, not editor-open handlers (verified: they open no View).
    static readonly HashSet<string> NonEditorHandlers = new(StringComparer.Ordinal)
    {
        "Lint_Click",
    };

    [Fact]
    public void Every_desktop_body_editor_maps_to_the_matching_catalog_entry()
    {
        string axaml = ReadRepoFile(Path.Combine("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml"));
        string cs = ReadRepoFile(Path.Combine("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

        // Collect (Content label, Click handler) for every button inside a body <Expander>.
        var buttons = new List<(string Content, string Click)>();
        foreach (Match exp in Regex.Matches(axaml, @"<Expander\b.*?</Expander>", RegexOptions.Singleline))
        {
            foreach (Match btn in Regex.Matches(exp.Value, @"<Button\b[^>]*?/?>", RegexOptions.Singleline))
            {
                var content = Regex.Match(btn.Value, @"\bContent=""([^""]+)""");
                var click = Regex.Match(btn.Value, @"\bClick=""([A-Za-z0-9_]+)""");
                if (content.Success && click.Success)
                    buttons.Add((content.Groups[1].Value, click.Groups[1].Value));
            }
        }
        Assert.True(buttons.Count > 150, $"Expected the full desktop editor body; found only {buttons.Count} buttons.");

        // Per desktop button: resolve its handler to the editor View type(s) it opens, and pair that
        // (order-independent) type set with the button's label. This is a PER-BUTTON check — a catalog
        // entry that opened the wrong view for a given label would fail, not just a missing type.
        var desktopPairs = new HashSet<(string Label, string Types)>();
        foreach (var (content, click) in buttons)
        {
            if (NonEditorHandlers.Contains(click))
                continue;

            string? body = ExtractMethodBody(cs, click);
            Assert.True(body != null, $"Body button handler {click} not found in MainWindow.axaml.cs.");

            var types = Regex.Matches(body!, @"\b(?:Open|Navigate|OpenModal|PickFromEditor|OpenAsTopLevel)<\s*([A-Za-z0-9_]+)")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            // Fail loud: an unresolved body button is a hole in the anti-drift net.
            Assert.True(types.Count >= 1,
                $"Body button '{content}' (handler {click}) does not resolve to any editor type — the parity scanner cannot verify it. " +
                "If it opens an editor, use a recognized open verb; if it is a non-editor action, add it to NonEditorHandlers.");

            // Skip buttons whose entire view set is intentionally excluded (the Window-derived
            // EventTemplate editors). Any partially-excluded set would (correctly) fail below.
            if (types.All(t => ExcludedDesktopEditors.Contains(t)))
                continue;

            desktopPairs.Add((content, string.Join(",", types)));
        }

        var catalogPairs = EditorCatalog.AllEntries
            .Select(e => (Label: e.Label, Types: string.Join(",", e.Views.Select(t => t.Name).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal))))
            .ToHashSet();

        // Every desktop editor button (label + exact view set) must have a matching catalog entry.
        var missing = desktopPairs.Except(catalogPairs).OrderBy(p => p.Label, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            "Desktop body editors missing from / mismatched in EditorCatalog (label -> views): "
            + string.Join("; ", missing.Select(p => $"{p.Label} -> {p.Types}")));

        // And the catalog must not carry an editor entry the desktop body does not (no orphans /
        // wrong label). Entries are typeof(...)-referenced, so a deleted view already fails the build;
        // this catches a mislabeled or fabricated entry.
        var orphan = catalogPairs.Except(desktopPairs).OrderBy(p => p.Label, StringComparer.Ordinal).ToList();
        Assert.True(orphan.Count == 0,
            "EditorCatalog entries with no matching desktop body button (label -> views): "
            + string.Join("; ", orphan.Select(p => $"{p.Label} -> {p.Types}")));

        // The exclusions must be real desktop-body editors (so a stale exclusion is caught).
        var allDesktopTypes = buttons
            .Where(b => !NonEditorHandlers.Contains(b.Click))
            .SelectMany(b => Regex.Matches(ExtractMethodBody(cs, b.Click) ?? "", @"\b(?:Open|Navigate|OpenModal|PickFromEditor|OpenAsTopLevel)<\s*([A-Za-z0-9_]+)").Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var excluded in ExcludedDesktopEditors)
            Assert.True(allDesktopTypes.Contains(excluded),
                $"{excluded} is listed as an exclusion but is not a desktop body editor — remove the stale exclusion.");
    }

    /// <summary>Extract a C# method body (block- or expression-bodied) by name from source text.</summary>
    static string? ExtractMethodBody(string source, string methodName)
    {
        var sig = Regex.Match(source,
            @"(?:private|public|protected|internal)[^\n=]*\b" + Regex.Escape(methodName) + @"\s*\([^)]*\)\s*");
        if (!sig.Success)
            return null;

        int i = sig.Index + sig.Length;
        if (i >= source.Length)
            return null;

        if (source[i] == '=') // expression-bodied "=> …;"
        {
            int semi = source.IndexOf(';', i);
            return semi < 0 ? source[i..] : source[i..semi];
        }
        if (source[i] == '{') // block-bodied
        {
            int depth = 0;
            for (int j = i; j < source.Length; j++)
            {
                if (source[j] == '{') depth++;
                else if (source[j] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source[i..(j + 1)];
                }
            }
        }
        return null;
    }

    static string ReadRepoFile(string relativePath)
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, relativePath);
        Assert.True(File.Exists(path), $"{relativePath} not found at {path}");
        return File.ReadAllText(path);
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (FEBuilderGBA.sln).");
    }
}
