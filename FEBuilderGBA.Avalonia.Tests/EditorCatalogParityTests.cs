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
    public void Every_desktop_body_editor_is_in_the_catalog()
    {
        string axaml = ReadRepoFile(Path.Combine("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml"));
        string cs = ReadRepoFile(Path.Combine("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

        // Collect the Click handlers of every button inside a body <Expander>…</Expander>.
        var handlers = new List<string>();
        foreach (Match exp in Regex.Matches(axaml, @"<Expander\b.*?</Expander>", RegexOptions.Singleline))
        {
            foreach (Match btn in Regex.Matches(exp.Value, @"<Button\b[^>]*?\bClick=""([A-Za-z0-9_]+)""", RegexOptions.Singleline))
                handlers.Add(btn.Groups[1].Value);
        }
        handlers = handlers.Distinct(StringComparer.Ordinal).ToList();
        Assert.True(handlers.Count > 150, $"Expected the full desktop editor body; found only {handlers.Count} button handlers.");

        var desktopTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (NonEditorHandlers.Contains(handler))
                continue;

            string? body = ExtractMethodBody(cs, handler);
            Assert.True(body != null, $"Body button handler {handler} not found in MainWindow.axaml.cs.");

            // Match ALL open verbs (Open / Navigate / OpenModal / PickFromEditor / OpenAsTopLevel).
            var types = Regex.Matches(body!, @"\b(?:Open|Navigate|OpenModal|PickFromEditor|OpenAsTopLevel)<\s*([A-Za-z0-9_]+)")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Fail loud: a body button we cannot resolve to an editor type is a hole in the
            // anti-drift net. Either it opens an editor (add it) or it is a non-editor action
            // (add it to NonEditorHandlers).
            Assert.True(types.Count >= 1,
                $"Body button handler {handler} does not resolve to any editor type — the parity scanner cannot verify it. " +
                "If it opens an editor, ensure the handler uses a recognized open verb; if it is a non-editor action, add it to NonEditorHandlers.");

            foreach (var t in types)
                desktopTypes.Add(t);
        }

        var catalogTypes = EditorCatalog.AllEntries
            .SelectMany(e => e.Views)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = desktopTypes
            .Where(t => !catalogTypes.Contains(t) && !ExcludedDesktopEditors.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "Desktop body editors missing from EditorCatalog (add them to EditorCatalog.cs, or to ExcludedDesktopEditors if single-view-incompatible): "
            + string.Join(", ", missing));

        // The exclusions must be real desktop-body editors (so a stale exclusion is caught).
        foreach (var excluded in ExcludedDesktopEditors)
            Assert.True(desktopTypes.Contains(excluded),
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
