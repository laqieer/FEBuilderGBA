// SPDX-License-Identifier: GPL-3.0-or-later
// #2019 — Real-control regression coverage for the launcher title search. Runs the
// production desktop MainWindow.ApplyFilter over the real editor body and the production
// single-view MainView.ApplyLauncherFilter over real Buttons/Expanders, so a wiring
// regression (lost section matching, lost count, broken version-hidden precedence, or
// single-view gaining desktop section semantics) fails here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Layout;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class MainWindowFilterTests
{
    static void ApplyFilter(MainWindow window, string filter)
        => typeof(MainWindow)
            .GetMethod("ApplyFilter", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, new object[] { filter });

    static void UpdateEditorVisibility(MainWindow window)
        => typeof(MainWindow)
            .GetMethod("UpdateEditorVisibility", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, null);

    static int MatchCount(MainWindow window)
    {
        var label = window.FindControl<TextBlock>("FilterMatchLabel");
        Assert.NotNull(label);
        string text = label!.Text ?? "";
        return int.Parse(text.Split(' ')[0]);
    }

    [AvaloniaFact]
    public void Title_only_term_reveals_the_map_editor_and_hides_unrelated_buttons()
    {
        var window = new MainWindow();
        var mapEditor = window.FindControl<Button>("MapEditorButton");
        Assert.NotNull(mapEditor);
        // Pre-condition of the bug: the label alone does not contain the query.
        Assert.DoesNotContain("Visual", mapEditor!.Content?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);

        ApplyFilter(window, "Visual");

        Assert.True(mapEditor.IsVisible, "'Visual' must reveal the Map Editor via its 'Visual Map Editor' title.");
        var expander = window.FindControl<Expander>("MapEditorsExpander");
        Assert.NotNull(expander);
        Assert.True(expander!.IsVisible);
        Assert.True(expander.IsExpanded, "A matching section must auto-expand.");

        // Unrelated editors are hidden, and the count reflects only real matches.
        var unrelated = window.FindControl<Button>("HexEditorButton");
        Assert.NotNull(unrelated);
        Assert.False(unrelated!.IsVisible);
        Assert.True(MatchCount(window) > 0);
        Assert.True(MatchCount(window) < 20, "A title-only term must not match the whole catalog.");

        // Clearing restores the pre-filter view.
        ApplyFilter(window, "");
        Assert.True(mapEditor.IsVisible);
        Assert.True(unrelated.IsVisible);
        Assert.False(window.FindControl<TextBlock>("FilterMatchLabel")!.IsVisible);
    }

    [AvaloniaFact]
    public void Section_header_matching_is_unchanged()
    {
        var window = new MainWindow();
        ApplyFilter(window, "Map Editors");

        var expander = window.FindControl<Expander>("MapEditorsExpander");
        Assert.NotNull(expander);
        Assert.True(expander!.IsVisible);
        // A section match shows every (non-version-hidden) button in that section.
        var wrap = Assert.IsType<WrapPanel>(expander.Content);
        Assert.All(wrap.Children.OfType<Button>()
                .Where(b => !MainWindow.IsButtonVersionHidden(b.Tag)),
            b => Assert.True(b.IsVisible));
    }

    [AvaloniaFact]
    public void Version_hidden_button_stays_hidden_even_when_its_title_matches()
    {
        var window = new MainWindow();
        var mapEditor = window.FindControl<Button>("MapEditorButton");
        Assert.NotNull(mapEditor);
        mapEditor!.Tag = false; // the version-hidden marker

        ApplyFilter(window, "Visual");

        Assert.False(mapEditor.IsVisible, "Version gating must win over a title match.");
        Assert.Equal(0, MatchCount(window));
    }

    [AvaloniaFact]
    public void Real_FE7_section_gate_stays_hidden_when_an_editor_title_matches()
    {
        ROM? previous = CoreState.ROM;
        var rom = new ROM();
        rom.LoadLow("synthetic-fe7u.gba", new byte[0x1000000], "AE7E01");
        CoreState.ROM = rom;
        try
        {
            var window = new MainWindow();
            UpdateEditorVisibility(window);

            var section = window.FindControl<Expander>("SkillsExpander");
            var button = window.FindControl<Button>("SkillUnitButton");
            Assert.NotNull(section);
            Assert.NotNull(button);
            Assert.False(section!.IsVisible);
            Assert.False(button!.IsVisible);
            Assert.True(MainWindow.IsButtonVersionHidden(button.Tag));

            ApplyFilter(window, "Assignment (Unit)");

            Assert.False(button.IsVisible);
            Assert.False(section.IsVisible);
            Assert.Equal(0, MatchCount(window));
        }
        finally
        {
            CoreState.ROM = previous;
        }
    }

    [AvaloniaFact]
    public void Label_only_term_still_matches_after_the_title_wiring()
    {
        var window = new MainWindow();
        ApplyFilter(window, "Hex Editor");
        Assert.True(window.FindControl<Button>("HexEditorButton")!.IsVisible);
        Assert.True(MatchCount(window) > 0);
    }

    [AvaloniaFact]
    public void Single_view_filter_matches_titles_without_gaining_section_semantics()
    {
        var mapEditor = new Button { Content = "Map Editor" };
        var hexEditor = new Button { Content = "Hex Editor" };
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        wrap.Children.Add(mapEditor);
        wrap.Children.Add(hexEditor);
        var expander = new Expander { Header = "Map Editors (2)", Content = wrap };

        var cats = new List<(Expander Exp, List<(Button Btn, string Label, string Key)> Buttons)>
        {
            (expander, new List<(Button, string, string)>
            {
                (mapEditor, "Map Editor", "MapEditor"),
                (hexEditor, "Hex Editor", "HexEditor"),
            }),
        };

        // Title-only term reveals just the Map Editor.
        MainView.ApplyLauncherFilter(cats, "Visual");
        Assert.True(mapEditor.IsVisible);
        Assert.False(hexEditor.IsVisible);
        Assert.True(expander.IsVisible);
        Assert.True(expander.IsExpanded);

        // Label term keeps working.
        MainView.ApplyLauncherFilter(cats, "hex");
        Assert.False(mapEditor.IsVisible);
        Assert.True(hexEditor.IsVisible);

        // Single-view has NO desktop section-header matching: the category name alone matches
        // nothing, so the category collapses.
        MainView.ApplyLauncherFilter(cats, "Map Editors (2)");
        Assert.False(mapEditor.IsVisible);
        Assert.False(hexEditor.IsVisible);
        Assert.False(expander.IsVisible);

        // Clearing restores everything.
        MainView.ApplyLauncherFilter(cats, "");
        Assert.True(mapEditor.IsVisible);
        Assert.True(hexEditor.IsVisible);
        Assert.True(expander.IsVisible);
    }
}
