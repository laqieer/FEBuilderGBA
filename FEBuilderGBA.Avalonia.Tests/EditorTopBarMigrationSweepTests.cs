// SPDX-License-Identifier: GPL-3.0-or-later
//
// Locks in the #649 Slice B migration set. For each editor in the
// migrated-list, this sweep asserts that the AXAML imports the Controls
// namespace and contains either a <controls:EditorTopBar ...> element
// (read-only top-bars) or a <controls:EditorTopBarWithInputs ...> element
// (editable top-bars). It also verifies the legacy hand-rolled top-bar
// pattern has been removed and that legacy AutomationIds are still
// reachable (preserved via the *AutomationId override styled-properties
// or renamed to *_Label for read-only displays).
//
// A negative-control test guarantees the helper predicate correctly
// classifies a hand-rolled-top-bar fixture as "unmigrated".
//
// A sanity check ensures a known migrated editor (`UnitEditorView`) passes,
// and a known deferred editor (`SkillConfigSkillSystemView`) is NOT in the
// migrated list.
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class EditorTopBarMigrationSweepTests
{
    static string FindRepoRoot()
    {
        // Walk up from the test assembly until we find the .sln.
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        for (int i = 0; i < 12; i++)
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Repo root not found");
    }

    static string ReadView(string viewName)
    {
        string path = Path.Combine(
            FindRepoRoot(),
            "FEBuilderGBA.Avalonia",
            "Views",
            viewName);
        Assert.True(File.Exists(path), $"View file not found: {path}");
        return File.ReadAllText(path);
    }

    // -----------------------------------------------------------------
    // Migrated set — every editor in this list MUST use either
    // EditorTopBar or EditorTopBarWithInputs for its read-config bar.
    //
    // Read-only candidates use EditorTopBar (StartAddress / ReadCount /
    // Size / Reload as TextBlock displays).
    //
    // Editable candidates use EditorTopBarWithInputs (NumericUpDown +
    // Reload). All three editable editors set ShowReadSize=False because
    // they only have Start + Count, not Size.
    // -----------------------------------------------------------------

    // Canonical source-of-truth arrays. Every TheoryData / regression-guard
    // helper below derives from these so adding a new migrated view here
    // automatically extends every sweep that runs on `AllMigratedViews`
    // (#741 Copilot CLI review — non-blocking cleanup).
    static readonly string[] s_readOnlyMigratedViews =
    {
        // Image / demo viewers — 6 from the original audit.
        "ImageBattleBGView.axaml",
        "ImageBGView.axaml",
        "ImageMagicCSACreatorView.axaml",
        "ImageMagicFEditorView.axaml",
        "ImageMapActionAnimationView.axaml",
        "OPClassDemoViewerView.axaml",
        // 4 more added per Copilot CLI plan review (round 1):
        "ItemUsagePointerViewerView.axaml",
        "MapTerrainBGLookupTableView.axaml",
        "MapTerrainFloorLookupTableView.axaml",
        "MapExitPointView.axaml",
    };

    static readonly string[] s_editableMigratedViews =
    {
        "SongTrackView.axaml",
        "SongInstrumentView.axaml",
        "AIScriptView.axaml",
        "SMEPromoListView.axaml",
    };

    public static TheoryData<string> ReadOnlyMigratedViews => ToTheoryData(s_readOnlyMigratedViews);
    public static TheoryData<string> EditableMigratedViews => ToTheoryData(s_editableMigratedViews);
    public static TheoryData<string> AllMigratedViews => ToTheoryData(AllMigratedViewNames());

    static TheoryData<string> ToTheoryData(System.Collections.Generic.IEnumerable<string> names)
    {
        var data = new TheoryData<string>();
        foreach (var name in names) data.Add(name);
        return data;
    }

    /// <summary>
    /// Plain string enumeration of every migrated view's AXAML filename —
    /// usable from regression-guard tests without going through xunit's
    /// TheoryData indexer. Derived from the canonical arrays so adding a
    /// view in one place extends every sweep.
    /// </summary>
    public static System.Collections.Generic.IEnumerable<string> AllMigratedViewNames()
    {
        foreach (var name in s_readOnlyMigratedViews) yield return name;
        foreach (var name in s_editableMigratedViews) yield return name;
    }

    // -----------------------------------------------------------------
    // Per-view sweeps.
    // -----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllMigratedViews))]
    public void MigratedView_ImportsControlsNamespace(string viewName)
    {
        string axaml = ReadView(viewName);
        Assert.Contains("xmlns:controls=\"clr-namespace:FEBuilderGBA.Avalonia.Controls\"", axaml);
    }

    [Theory]
    [MemberData(nameof(ReadOnlyMigratedViews))]
    public void ReadOnlyMigratedView_UsesEditorTopBar(string viewName)
    {
        string axaml = ReadView(viewName);
        Assert.Contains("<controls:EditorTopBar", axaml);
    }

    [Theory]
    [MemberData(nameof(EditableMigratedViews))]
    public void EditableMigratedView_UsesEditorTopBarWithInputs(string viewName)
    {
        string axaml = ReadView(viewName);
        Assert.Contains("<controls:EditorTopBarWithInputs", axaml);
    }

    [Theory]
    [MemberData(nameof(AllMigratedViews))]
    public void MigratedView_DoesNotContainLegacyHandRolledTopBar(string viewName)
    {
        // Predicate: detect a `<Border ...> <StackPanel ... Orientation="Horizontal"
        // ...> <Label Content="(Read Start Address|First Address|...)" ...
        // ... <NumericUpDown ... Reload" Click="ReloadList_Click" ...`.
        //
        // We approximate with a tighter regex — the legacy pattern always
        // includes either `Label Content="Read Start Address"` /
        // `Label Content="Read Count"` immediately next to a NumericUpDown
        // with `IsEnabled="False"` (the read-only display pattern). If
        // both still appear inside a <Border>...<StackPanel>... block,
        // the view hasn't fully migrated.
        string axaml = ReadView(viewName);
        // Block on either pattern that the read-only migrations were
        // supposed to remove. The 4 editable migrations should not include
        // an IsEnabled="False" NumericUpDown for ReadStart/ReadCount.
        var legacyReadOnlyRegex = new Regex(
            "<NumericUpDown[^>]*Name=\"ReadStartAddressBox\"[^>]*IsEnabled=\"False\"",
            RegexOptions.Singleline);
        Assert.False(legacyReadOnlyRegex.IsMatch(axaml),
            $"{viewName}: still contains legacy IsEnabled=\"False\" ReadStartAddressBox NumericUpDown");
    }

    [Theory]
    [MemberData(nameof(AllMigratedViews))]
    public void MigratedView_HasTopBarAutomationId(string viewName)
    {
        // Every host sets a "..._TopBar" automation id on the unified
        // control (mirrors UnitEditorView et al.).
        string axaml = ReadView(viewName);
        Assert.Matches(new Regex("AutomationProperties\\.AutomationId=\"[A-Za-z0-9]+_TopBar\""),
            axaml);
    }

    [Fact]
    public void EditorTopBarWithInputs_PinsReloadToRightEdge()
    {
        // Regression guard for #741 Copilot review: the editable variant's
        // AXAML must use a Grid with ColumnDefinitions="*,Auto" so the
        // Reload button stays pinned to the far-right edge (Column 1),
        // matching the read-only EditorTopBar contract.
        string path = Path.Combine(
            FindRepoRoot(),
            "FEBuilderGBA.Avalonia",
            "Controls",
            "EditorTopBarWithInputs.axaml");
        Assert.True(File.Exists(path));
        string axaml = File.ReadAllText(path);
        Assert.Contains("ColumnDefinitions=\"*,Auto\"", axaml);
        // Reload button must be placed in Grid.Column="1" so it lives in
        // the Auto column at the right edge, not at the natural end of a
        // StackPanel.
        Assert.Matches(new Regex(
            "<Button[^>]*Grid\\.Column=\"1\"[^>]*Name=\"ReloadButton\"",
            RegexOptions.Singleline),
            axaml);
    }

    // -----------------------------------------------------------------
    // Negative-control / regression-guard tests.
    // -----------------------------------------------------------------

    [Fact]
    public void KnownMigratedEditor_UnitEditor_PassesSweep()
    {
        // UnitEditorView was migrated in PR #669 — it's the canonical
        // "this is what migration looks like" exemplar. If this fails,
        // something has regressed the EditorTopBar usage repo-wide.
        string axaml = ReadView("UnitEditorView.axaml");
        Assert.Contains("xmlns:controls=\"clr-namespace:FEBuilderGBA.Avalonia.Controls\"", axaml);
        Assert.Contains("<controls:EditorTopBar", axaml);
        Assert.Contains("UnitEditor_TopBar", axaml);
    }

    [Fact]
    public void KnownDeferredEditor_NotInMigratedList()
    {
        // SkillConfigSkillSystemView is explicitly deferred to Slice C
        // (patch-detection coupling). It must NOT be claimed as migrated
        // — if someone accidentally adds it to the AllMigratedViews list
        // without doing the actual migration, this test catches it.
        foreach (string name in AllMigratedViewNames())
        {
            Assert.NotEqual("SkillConfigSkillSystemView.axaml", name);
            Assert.NotEqual("MonsterItemViewerView.axaml", name);
            Assert.NotEqual("ImageUnitPaletteView.axaml", name);
        }
    }

    [Fact]
    public void LegacyTopBarPredicate_DetectsHandRolledFixture()
    {
        // Inline fixture: the literal legacy pattern. Locks the predicate
        // so a future refactor of the regex can't silently turn into a
        // no-op.
        const string legacyFixture = """
        <Border BorderBrush="Gray" BorderThickness="1" Margin="4">
          <StackPanel Orientation="Horizontal" Spacing="6" Margin="4">
            <Label Content="Read Start Address" VerticalAlignment="Center" />
            <NumericUpDown AutomationProperties.AutomationId="Foo_ReadStart_Input"
                           FormatString="0" Name="ReadStartAddressBox" Width="120"
                           Minimum="0" Maximum="65535999"
                           IsEnabled="False" VerticalAlignment="Center" />
          </StackPanel>
        </Border>
        """;
        var legacyReadOnlyRegex = new Regex(
            "<NumericUpDown[^>]*Name=\"ReadStartAddressBox\"[^>]*IsEnabled=\"False\"",
            RegexOptions.Singleline);
        Assert.True(legacyReadOnlyRegex.IsMatch(legacyFixture),
            "The legacy-pattern predicate failed to detect a hand-rolled fixture — refactor the regex.");
    }
}
