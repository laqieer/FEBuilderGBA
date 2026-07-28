// SPDX-License-Identifier: GPL-3.0-or-later
// #2019 — Behavior of the shared launcher matcher: the filter must find an editor by the
// title of the window/page it opens, not only by the visible button label. Existing
// label-only matching, case-insensitivity and definite non-matches must be preserved.
using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class EditorSearchIndexTests
{
    [Fact]
    public void Visual_finds_the_map_editor_by_its_window_title()
    {
        // The reported case: the button reads "Map Editor" but opens "Visual Map Editor".
        Assert.False("Map Editor".Contains("Visual", StringComparison.OrdinalIgnoreCase));
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", "Visual"));
        Assert.True(EditorSearchIndex.MatchesCatalogEntry("MapEditor", "Map Editor", "Visual"));
    }

    [Fact]
    public void Existing_label_matching_still_works_and_is_case_insensitive()
    {
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", "map"));
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", "EDITOR"));
        Assert.True(EditorSearchIndex.MatchesCatalogEntry("MapEditor", "Map Editor", "mAp eDiTor"));
        // Title matching is case-insensitive too.
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", "vIsUaL"));
    }

    [Fact]
    public void Empty_filter_matches_everything()
    {
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", ""));
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", "   "));
        Assert.True(EditorSearchIndex.MatchesCatalogEntry("MapEditor", "Map Editor", ""));
    }

    [Fact]
    public void Definite_non_match_stays_hidden()
    {
        Assert.False(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", "zzzz-no-such-editor"));
        Assert.False(EditorSearchIndex.MatchesCatalogEntry("MapEditor", "Map Editor", "zzzz-no-such-editor"));
        // A term from an unrelated editor's title must not leak across identities.
        Assert.False(EditorSearchIndex.MatchesDesktopButton("MapEditorButton", "Map Editor", "Huffman"));
    }

    [Fact]
    public void Duplicate_unit_palette_labels_resolve_to_distinct_titles()
    {
        // Two desktop buttons share the label "Unit Palette"; identity is the Button Name.
        Assert.True(EditorSearchIndex.MatchesDesktopButton("ImgUnitPaletteButton", "Unit Palette", "Palette Editor"));
        Assert.False(EditorSearchIndex.MatchesDesktopButton("UnitPaletteButton", "Unit Palette", "Palette Editor"));

        Assert.True(EditorSearchIndex.MatchesDesktopButton("UnitPaletteButton", "Unit Palette", "Assignment"));
        Assert.False(EditorSearchIndex.MatchesDesktopButton("ImgUnitPaletteButton", "Unit Palette", "Assignment"));

        // Same distinction on the single-view side, keyed by EditorEntry.Key.
        Assert.True(EditorSearchIndex.MatchesCatalogEntry("ImageUnitPalette", "Unit Palette", "Palette Editor"));
        Assert.False(EditorSearchIndex.MatchesCatalogEntry("UnitPalette", "Unit Palette", "Palette Editor"));
    }

    [Fact]
    public void Desktop_only_event_template_editors_are_searchable_by_title()
    {
        // EventTemplate1..6 are Window-derived and intentionally absent from the catalog, so
        // they only exist on desktop — but they must still be findable there.
        Assert.True(EditorSearchIndex.MatchesDesktopButton("Template1Button", "Template 1", "Event Template 1"));
        Assert.Null(EditorSearchIndex.AliasesForCatalogEntry("EventTemplate1"));
    }

    [Fact]
    public void Multi_view_entries_carry_every_variant_title()
    {
        // Map Settings dispatches on ROM version; all candidate titles are searchable. The
        // version/patch visibility gate stays authoritative about whether the button is shown,
        // so a visible shared button may additionally match a sibling variant's title.
        var aliases = EditorSearchIndex.AliasesForDesktopButton("MapSettingsButton");
        Assert.NotNull(aliases);
        Assert.True(aliases!.Count >= 3, "MapSettingsButton should carry every dispatched Map Settings title.");
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapSettingsButton", "Map Settings", "FE7U"));
        Assert.True(EditorSearchIndex.MatchesDesktopButton("MapSettingsButton", "Map Settings", "FE6"));

        // Same for a patch-dispatched button.
        Assert.True(EditorSearchIndex.MatchesDesktopButton("StatBonusesButton", "Stat Bonuses", "Venno"));
        Assert.True(EditorSearchIndex.MatchesDesktopButton("StatBonusesButton", "Stat Bonuses", "Skill Systems"));
    }

    [Fact]
    public void Unknown_desktop_button_falls_open_to_label_matching()
    {
        Assert.Null(EditorSearchIndex.AliasesForDesktopButton("SomeFutureButtonNotYetIndexed"));
        Assert.True(EditorSearchIndex.MatchesDesktopButton("SomeFutureButtonNotYetIndexed", "Brand New Editor", "brand"));
        Assert.False(EditorSearchIndex.MatchesDesktopButton("SomeFutureButtonNotYetIndexed", "Brand New Editor", "Visual"));
        // Null identity / null label are tolerated.
        Assert.True(EditorSearchIndex.MatchesDesktopButton(null, "Brand New Editor", "brand"));
        Assert.False(EditorSearchIndex.MatchesCatalogEntry(null, null, "brand"));
    }

    [Fact]
    public void Matcher_matches_localized_aliases_as_well_as_raw_literals()
    {
        string[] aliases = { "Visual Map Editor" };
        // Raw literal.
        Assert.True(EditorSearchIndex.Matches("Map Editor", aliases, "Visual"));

        // Force a deterministic translated title so this branch cannot silently skip when
        // the test process happens to use English.
        Assert.True(EditorSearchIndex.Matches(
            "Map Editor",
            aliases,
            "可视化",
            value => value == "Visual Map Editor" ? "可视化地图编辑器" : value));

        // Localization is looked up per keystroke, so no cached remap can go stale.
        Assert.True(EditorSearchIndex.Matches(null, aliases, "map editor"));
    }

    [Fact]
    public void Index_covers_every_catalog_entry_and_every_indexed_alias_is_reachable()
    {
        foreach (var entry in EditorCatalog.AllEntries)
        {
            var aliases = EditorSearchIndex.AliasesForCatalogEntry(entry.Key);
            Assert.True(aliases is { Count: > 0 }, $"No search aliases for catalog entry {entry.Key}.");
            foreach (string alias in aliases!)
                Assert.True(EditorSearchIndex.MatchesCatalogEntry(entry.Key, entry.Label, alias),
                    $"Alias '{alias}' of {entry.Key} is not reachable through the matcher.");
        }
    }

    [Fact]
    public void Shared_matcher_is_pure_over_null_alias_sets()
    {
        Assert.True(EditorSearchIndex.Matches("Map Editor", null, "map"));
        Assert.False(EditorSearchIndex.Matches("Map Editor", null, "visual"));
        Assert.False(EditorSearchIndex.Matches(null, null, "visual"));
        Assert.True(EditorSearchIndex.Matches(null, new List<string> { "Visual Map Editor" }, "visual"));
    }
}
