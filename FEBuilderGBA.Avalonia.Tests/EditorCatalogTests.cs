// SPDX-License-Identifier: GPL-3.0-or-later
// #1891 — EditorCatalog well-formedness. The single-view (WebAssembly / Android) launcher
// renders EditorCatalog; these tests guard the invariants the launcher relies on:
//   * a substantial, category-complete catalog (not the old 9-editor stub);
//   * unique keys (used for AutomationIds + the E2E open-by-key hook);
//   * EVERY View type implements IEmbeddableEditor — the discriminator that keeps the 6
//     Window-derived EventTemplate editors (which throw on a single-view host) OUT of the
//     catalog. A plain "is a Control" check is NOT enough (Window is a Control), so this is
//     the test that actually catches a Window editor slipping in;
//   * the version-applicability gate mirrors the desktop MainWindow.UpdateEditorVisibility.
using System;
using System.Linq;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class EditorCatalogTests
{
    [Fact]
    public void Catalog_is_substantial_and_every_category_non_empty()
    {
        // The desktop body exposes ~230 editors across 28 categories; the catalog mirrors it
        // minus the 6 Window-derived EventTemplate editors (excluded on purpose).
        Assert.True(EditorCatalog.AllEntries.Count >= 200,
            $"Catalog collapsed to {EditorCatalog.AllEntries.Count} entries — expected the full desktop editor set (~223).");
        Assert.Equal(28, EditorCatalog.Categories.Count);
        Assert.All(EditorCatalog.Categories, g => Assert.NotEmpty(g));
    }

    [Fact]
    public void Catalog_keys_are_unique()
    {
        var dupes = EditorCatalog.AllEntries
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(dupes.Count == 0, "Duplicate catalog keys: " + string.Join(", ", dupes));
    }

    [Fact]
    public void Every_entry_has_open_action_and_at_least_one_view()
    {
        foreach (var e in EditorCatalog.AllEntries)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Label), $"entry with key '{e.Key}' has no label");
            Assert.NotNull(e.Open);
            Assert.NotEmpty(e.Views);
        }
    }

    [Fact]
    public void Every_view_is_an_embeddable_editor_with_a_parameterless_ctor()
    {
        foreach (var e in EditorCatalog.AllEntries)
        {
            foreach (var t in e.Views)
            {
                Assert.True(typeof(IEmbeddableEditor).IsAssignableFrom(t),
                    $"{t.Name} (entry '{e.Label}') must implement IEmbeddableEditor to host in the single-view launcher. " +
                    "Window-derived editors (e.g. the EventTemplate editors) throw NotSupportedException on the wasm/Android single-view host and must be excluded from the catalog.");
                Assert.True(typeof(global::Avalonia.Controls.Control).IsAssignableFrom(t),
                    $"{t.Name} is not an Avalonia Control");
                Assert.NotNull(t.GetConstructor(Type.EmptyTypes));
            }
        }
    }

    [Fact]
    public void Window_derived_EventTemplate_editors_are_not_in_the_catalog()
    {
        // These are the only Window-derived body editors (verified in code); they cannot host
        // on the single-view launcher, so they must never appear in the catalog.
        var names = EditorCatalog.AllEntries.SelectMany(e => e.Views).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var excluded in new[]
        {
            "EventTemplate1View", "EventTemplate2View", "EventTemplate3View",
            "EventTemplate4View", "EventTemplate5View", "EventTemplate6View",
        })
        {
            Assert.DoesNotContain(excluded, names);
        }
    }

    static EditorEntry Entry(string label) =>
        EditorCatalog.AllEntries.First(e => e.Label == label);

    [Fact]
    public void Untagged_editor_applies_to_every_version()
    {
        var units = Entry("Units");
        Assert.True(EditorCatalog.AppliesTo(units, 6, true));
        Assert.True(EditorCatalog.AppliesTo(units, 7, false));
        Assert.True(EditorCatalog.AppliesTo(units, 8, false));
    }

    [Fact]
    public void Fe8_only_categories_are_gated_to_fe8()
    {
        var monster = EditorCatalog.AllEntries.First(e => e.Category == "Monsters");
        Assert.False(EditorCatalog.AppliesTo(monster, 6, false));
        Assert.False(EditorCatalog.AppliesTo(monster, 7, false));
        Assert.True(EditorCatalog.AppliesTo(monster, 8, false));
    }

    [Theory]
    [InlineData("Class (FE6)", 6, false, true)]
    [InlineData("Class (FE6)", 8, false, false)]
    [InlineData("Units (FE7)", 7, true, true)]
    [InlineData("Units (FE7)", 8, false, false)]
    [InlineData("Map Settings (FE7U)", 7, false, true)]   // FE7U == version 7, non-multibyte (US)
    [InlineData("Map Settings (FE7U)", 7, true, false)]   // FE7JP (multibyte) hides the FE7U editor
    [InlineData("OP Demo (FE8U)", 8, false, true)]
    [InlineData("OP Demo (FE8U)", 8, true, false)]
    public void Version_tagged_editors_follow_the_desktop_rule(string label, int ver, bool multibyte, bool expected)
    {
        Assert.Equal(expected, EditorCatalog.AppliesTo(Entry(label), ver, multibyte));
    }

    [Fact]
    public void Special_cased_editors_match_desktop_gating()
    {
        // #1411: the generic Portrait Editor (28-byte) is hidden on FE6 only.
        var portrait = Entry("Portrait Editor");
        Assert.False(EditorCatalog.AppliesTo(portrait, 6, false));
        Assert.True(EditorCatalog.AppliesTo(portrait, 7, false));
        Assert.True(EditorCatalog.AppliesTo(portrait, 8, false));

        // Senseki Comment is FE7-only (no version suffix in its label).
        var senseki = Entry("Senseki Comment");
        Assert.True(EditorCatalog.AppliesTo(senseki, 7, false));
        Assert.False(EditorCatalog.AppliesTo(senseki, 6, false));
        Assert.False(EditorCatalog.AppliesTo(senseki, 8, false));
    }
}
