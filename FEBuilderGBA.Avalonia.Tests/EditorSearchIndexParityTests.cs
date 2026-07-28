// SPDX-License-Identifier: GPL-3.0-or-later
// #2019 — Anti-drift guard for EditorSearchIndex, the checked-in mirror of the editor
// display titles the launcher filters search. It re-parses the same sources the mirror was
// generated from (MainWindow.axaml's EditorPanel body, MainWindow.axaml.cs open handlers,
// each mapped View's C# ViewTitle / EditorDescriptor.Title literal and its AXAML root
// Title) and fails when a renamed title, a new editor button, or a new catalog entry is not
// reflected in the index. Title parsing is deliberately restricted to the mapped View files
// so an unrelated view's non-constant title cannot silently widen the contract.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class EditorSearchIndexParityTests
{
    // Open verbs recognized when resolving a Click handler to the View type(s) it opens.
    const string OpenVerbPattern = @"\b(?:Open|Navigate|OpenModal|PickFromEditor|OpenAsTopLevel)<\s*([A-Za-z0-9_]+)";

    // Desktop EditorPanel buttons that are actions rather than editor-open handlers. This set
    // must stay EXACTLY equal to the unmapped set (asserted below) — Event Template 1-6 are
    // mapped desktop-only editors, NOT exemptions.
    static readonly HashSet<string> NonEditorButtons = new(StringComparer.Ordinal)
    {
        "RunLintButton",
    };

    // Mapped Views whose title members are not string literals. Pinned (currently empty) so a
    // future non-constant title has to be justified here instead of silently dropping aliases.
    static readonly HashSet<string> NonConstantTitleViews = new(StringComparer.Ordinal);

    [Fact]
    public void Every_desktop_editor_button_has_a_unique_name_and_indexed_title_aliases()
    {
        var buttons = ReadEditorPanelButtons();
        Assert.True(buttons.Count > 150, $"Expected the full desktop editor body; found {buttons.Count} buttons.");

        // Scoped to the EditorPanel body: names outside it (e.g. the decomp toolbar) are not
        // search identities and are intentionally not required to exist or be unique.
        var unnamed = buttons.Where(b => string.IsNullOrWhiteSpace(b.Name)).Select(b => b.Click).ToList();
        Assert.True(unnamed.Count == 0,
            "EditorPanel buttons without a stable Name (search identity): " + string.Join(", ", unnamed));

        var duplicated = buttons.GroupBy(b => b.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicated.Count == 0,
            "Duplicate EditorPanel Button Names (search identity must be unique): " + string.Join(", ", duplicated));

        string cs = ReadRepoFile(Path.Combine("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));
        var unmapped = new List<string>();
        var indexedNames = new HashSet<string>(EditorSearchIndex.DesktopAliases.Keys, StringComparer.Ordinal);

        foreach (var (name, content, click) in buttons)
        {
            var types = ResolveOpenedViewTypes(cs, click);
            if (types.Count == 0)
            {
                unmapped.Add(name);
                Assert.False(indexedNames.Contains(name),
                    $"{name} opens no editor yet carries search aliases — remove it from EditorSearchIndex.");
                continue;
            }

            Assert.True(EditorSearchIndex.DesktopAliases.TryGetValue(name, out var aliases),
                $"Desktop editor button '{content}' ({name} -> {string.Join(",", types)}) has no EditorSearchIndex entry.");
            AssertAliasesMatchTitles(name, types, aliases!);
        }

        // The only unmapped buttons must be exactly the documented non-editor actions.
        Assert.Equal(
            NonEditorButtons.OrderBy(n => n, StringComparer.Ordinal).ToList(),
            unmapped.OrderBy(n => n, StringComparer.Ordinal).ToList());

        // No fabricated / stale index rows.
        var known = buttons.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
        var orphan = indexedNames.Except(known).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(orphan.Count == 0,
            "EditorSearchIndex desktop entries with no matching EditorPanel button: " + string.Join(", ", orphan));
    }

    [Fact]
    public void Desktop_only_event_template_editors_are_indexed()
    {
        for (int i = 1; i <= 6; i++)
        {
            string name = $"Template{i}Button";
            Assert.True(EditorSearchIndex.DesktopAliases.TryGetValue(name, out var aliases),
                $"{name} (desktop-only EventTemplate{i}View) must be searchable by its window title.");
            Assert.Contains($"Event Template {i}", aliases!, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_catalog_entry_has_indexed_title_aliases_for_its_views()
    {
        var keys = EditorCatalog.AllEntries.Select(e => e.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());

        foreach (var entry in EditorCatalog.AllEntries)
        {
            Assert.True(EditorSearchIndex.CatalogAliases.TryGetValue(entry.Key, out var aliases),
                $"Catalog entry '{entry.Label}' (key {entry.Key}) has no EditorSearchIndex entry.");
            AssertAliasesMatchTitles(entry.Key, entry.Views.Select(t => t.Name).ToList(), aliases!);
        }

        var orphan = EditorSearchIndex.CatalogAliases.Keys
            .Except(keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.True(orphan.Count == 0,
            "EditorSearchIndex catalog entries with no matching EditorEntry.Key: " + string.Join(", ", orphan));
    }

    [Fact]
    public void All_aliases_are_non_empty_and_deduplicated_case_insensitively()
    {
        foreach (var (key, aliases) in EditorSearchIndex.CatalogAliases.Concat(EditorSearchIndex.DesktopAliases))
        {
            Assert.True(aliases.Count > 0, $"{key} has an empty alias set.");
            foreach (string alias in aliases)
                Assert.False(string.IsNullOrWhiteSpace(alias), $"{key} has a blank alias.");
            Assert.Equal(aliases.Count, aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void Open_handler_parser_follows_local_helper_dispatch()
    {
        const string source = """
            private void Button_Click(object? sender, EventArgs e)
            {
                OpenThroughHelper();
            }

            private void OpenThroughHelper()
            {
                Open<FakeEditorView>();
            }
            """;

        Assert.Equal(new[] { "FakeEditorView" }, ResolveOpenedViewTypes(source, "Button_Click"));
    }

    /// <summary>
    /// The indexed aliases for one identity must equal the union of every declared title
    /// literal (C# ViewTitle, EditorDescriptor.Title, AXAML root Title) of its mapped Views.
    /// </summary>
    static void AssertAliasesMatchTitles(
        string identity,
        IReadOnlyList<string> viewTypes,
        IReadOnlyList<string> aliases)
    {
        var expected = new List<string>();
        foreach (string type in viewTypes.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal))
            expected.AddRange(ReadDeclaredTitles(type));

        var expectedSet = expected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(expectedSet.Count > 0,
            $"{identity}: no declared title literal found for {string.Join(",", viewTypes)} — the mirror cannot be verified.");

        var actualSet = aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expectedSet.Except(actualSet, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var extra = actualSet.Except(expectedSet, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{identity}: EditorSearchIndex is missing title alias(es): {string.Join(" | ", missing)}");
        Assert.True(extra.Count == 0, $"{identity}: EditorSearchIndex has stale title alias(es): {string.Join(" | ", extra)}");
    }

    /// <summary>
    /// Declared display titles of one View type: the C# <c>ViewTitle</c> and
    /// <c>Descriptor</c> title literals (unwrapping <c>R._("literal")</c>) plus the AXAML root
    /// <c>Title</c>. Parsing is restricted to the mapped View files by construction.
    /// </summary>
    static IEnumerable<string> ReadDeclaredTitles(string viewType)
    {
        var titles = new List<string>();

        string csPath = RepoPath(Path.Combine("FEBuilderGBA.Avalonia", "Views", viewType + ".axaml.cs"));
        if (!File.Exists(csPath))
            csPath = RepoPath(Path.Combine("FEBuilderGBA.Avalonia", "Views", viewType + ".cs"));
        if (File.Exists(csPath))
        {
            string src = File.ReadAllText(csPath);
            bool declaresViewTitle = Regex.IsMatch(src, @"\bViewTitle\b");
            bool literalViewTitle = false;
            foreach (Match m in Regex.Matches(src, @"ViewTitle\s*=>\s*(?:R\._\(\s*)?""((?:[^""\\]|\\.)*)"""))
            {
                literalViewTitle = true;
                titles.Add(Unescape(m.Groups[1].Value));
            }

            bool declaresDescriptor = Regex.IsMatch(src, @"\bDescriptor\s*=>");
            bool literalDescriptor = false;
            foreach (Match m in Regex.Matches(src, @"Descriptor\s*=>\s*new\s*\(\s*(?:R\._\(\s*)?""((?:[^""\\]|\\.)*)"""))
            {
                literalDescriptor = true;
                titles.Add(Unescape(m.Groups[1].Value));
            }

            bool nonConstant = (declaresViewTitle && !literalViewTitle) || (declaresDescriptor && !literalDescriptor);
            Assert.Equal(NonConstantTitleViews.Contains(viewType), nonConstant);
        }

        string axamlPath = RepoPath(Path.Combine("FEBuilderGBA.Avalonia", "Views", viewType + ".axaml"));
        if (File.Exists(axamlPath))
        {
            string axaml = File.ReadAllText(axamlPath);
            var root = Regex.Match(axaml, @"<(?:Window|UserControl)\b[^>]*>", RegexOptions.Singleline);
            if (root.Success)
            {
                var title = Regex.Match(root.Value, @"\bTitle=""([^""]*)""");
                if (title.Success && title.Groups[1].Value.Trim().Length > 0)
                    titles.Add(title.Groups[1].Value.Trim());
            }
        }

        return titles;
    }

    static string Unescape(string literal) => literal.Replace("\\\"", "\"").Replace("\\\\", "\\");

    static List<(string Name, string Content, string Click)> ReadEditorPanelButtons()
    {
        XElement editorPanel = LoadDesktopEditorPanel();
        XNamespace ns = editorPanel.Name.Namespace;
        return editorPanel.Descendants(ns + "Button")
            .Where(b => (string?)b.Attribute("Click") != null)
            .Select(b => (
                Name: (string?)b.Attribute("Name") ?? "",
                Content: (string?)b.Attribute("Content") ?? "",
                Click: (string?)b.Attribute("Click") ?? ""))
            .ToList();
    }

    static List<string> ResolveOpenedViewTypes(string mainWindowSource, string clickHandler)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string methodName)
        {
            if (!visited.Add(methodName))
                return;

            string? body = ExtractMethodBody(mainWindowSource, methodName);
            Assert.True(body != null,
                $"EditorPanel button handler/helper {methodName} not found in MainWindow.axaml.cs.");

            foreach (Match match in Regex.Matches(body!, OpenVerbPattern))
                types.Add(match.Groups[1].Value);

            // Follow same-class helper calls so moving a version/patch dispatch behind a helper
            // cannot silently narrow the title mirror. Dotted calls (service.Method()) are
            // excluded; only method names that resolve in this source are traversed.
            foreach (Match match in Regex.Matches(
                body!,
                @"(?<![A-Za-z0-9_.])([A-Za-z_][A-Za-z0-9_]*)\s*\("))
            {
                string helper = match.Groups[1].Value;
                if (!visited.Contains(helper)
                    && ExtractMethodBody(mainWindowSource, helper) != null)
                {
                    Visit(helper);
                }
            }
        }

        Visit(clickHandler);
        return types
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    static XElement LoadDesktopEditorPanel()
    {
        string axaml = ReadRepoFile(Path.Combine("FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml"));
        XDocument document = XDocument.Parse(axaml);
        XNamespace ns = document.Root!.Name.Namespace;
        return document.Descendants(ns + "StackPanel")
            .Single(e => (string?)e.Attribute("Name") == "EditorPanel");
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
        string path = RepoPath(relativePath);
        Assert.True(File.Exists(path), $"{relativePath} not found at {path}");
        return File.ReadAllText(path);
    }

    static string RepoPath(string relativePath) => Path.Combine(FindRepoRoot(), relativePath);

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
