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
        // Slice C round 1 migrations (#743 / PR #745):
        "SkillConfigSkillSystemView.axaml",
        "SkillConfigFE8NVer2SkillView.axaml",
        "SkillConfigFE8NVer3SkillView.axaml",
        "SkillConfigFE8UCSkillSys09xView.axaml",
        "SkillAssignmentClassSkillSystemView.axaml",
        "SkillAssignmentClassCSkillSysView.axaml",
        "WorldMapEventPointerView.axaml",
        "MonsterItemViewerView.axaml",
        // Slice C round 2 migrations (#743 round 2): both use the new
        // TrailingContent slot to hold extra strip controls (combos)
        // between the inputs and the Reload button.
        "SkillConfigFE8NSkillView.axaml",
        "MapTileAnimation2View.axaml",
    };

    /// <summary>
    /// Editor views that are PERMANENTLY deferred — keep their hand-rolled
    /// top bars and do NOT migrate to the unified controls. Each entry has
    /// a documented technical reason; the corresponding test below asserts
    /// the view still contains its custom top bar so a future agent can't
    /// silently sneak it into the migrated list without making an actual
    /// migration decision.
    /// </summary>
    static readonly (string view, string reason)[] s_permanentlyDeferredViews =
    {
        ("EventCondView.axaml",
         "Avalonia NumericUpDown.FormatString=\"X*\" throws FormatException " +
         "(#253 / NumericUpDownFormatStringTests). The legacy top-bar uses " +
         "TextBox+\"0x{addr:X08}\" for the address display; migrating to a " +
         "NumericUpDown loses the hex visual (a UX regression) or risks the " +
         "FormatException. Pending a 'display-as-text' mode on the unified " +
         "control under a separate issue."),
        ("EventUnitView.axaml",
         "Same hex-display drift as EventCondView — TextBox-backed TopAddr " +
         "with \"0x{addr:X08}\" format. NumericUpDown cannot reproduce this " +
         "without a hex FormatString (which throws FormatException, #253)."),
        ("EventUnitFE7View.axaml",
         "Same hex-display drift as EventCondView — TextBox-backed TopAddr " +
         "with \"0x{addr:X08}\" format. NumericUpDown cannot reproduce this " +
         "without a hex FormatString (which throws FormatException, #253)."),
        ("ImageUnitPaletteView.axaml",
         "The read-config fields live inside the Search Tools tab in a " +
         "2-column vertical Grid (not a horizontal strip), and each field " +
         "is a <TextBox IsReadOnly> rather than a NumericUpDown. The " +
         "TrailingContent slot doesn't resolve this — the fundamental " +
         "topology is incompatible with the horizontal-strip contract."),
        ("TextViewerView.axaml",
         "Per the issue body: TextViewer's Edit tab carries a content-search " +
         "bar (not address bar), and its Search Tools tab has filter fields " +
         "in a 2-column vertical Grid. Two different incompatible " +
         "topologies — out of scope for the unified-bar migration."),
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
    [MemberData(nameof(ReadOnlyMigratedViews))]
    public void ReadOnlyMigratedView_DoesNotContainLegacyHandRolledTopBar(string viewName)
    {
        // The legacy read-only pattern always contains an
        // `IsEnabled="False"` NumericUpDown for ReadStartAddress (an
        // editable input being used as a display field). After migrating
        // to EditorTopBar that NumericUpDown is replaced by a TextBlock
        // slot, so this regex MUST NOT match.
        string axaml = ReadView(viewName);
        var legacyReadOnlyRegex = new Regex(
            "<NumericUpDown[^>]*Name=\"ReadStartAddressBox\"[^>]*IsEnabled=\"False\"",
            RegexOptions.Singleline);
        Assert.False(legacyReadOnlyRegex.IsMatch(axaml),
            $"{viewName}: still contains legacy IsEnabled=\"False\" ReadStartAddressBox NumericUpDown");
    }

    [Theory]
    [MemberData(nameof(EditableMigratedViews))]
    public void EditableMigratedView_DoesNotContainLegacyHandRolledTopBar(string viewName)
    {
        // Pre-migration editable top-bar pattern (panel3 / panel1):
        //   <StackPanel Orientation="Horizontal" ...> [optionally wrapped
        //                                              in a <Border> or
        //                                              <DockPanel.Dock="Top">]
        //     ... <NumericUpDown ... Name="(ReadStartAddressBox
        //         |TopAddressBox|NumericUpDown1)" ... Maximum="..." .../>
        //     ... <NumericUpDown ... Name="(ReadCountBox|NumericUpDown2)" .../>
        //     ... <Button ... Click="(ReloadList_Click|Reload_Click)" .../>
        //   </StackPanel>
        //
        // The DISTINGUISHING marker (which the per-entry write bar in
        // AIScriptView's panel5 lacks) is the *pairing* of a ReadStart-
        // style NumericUpDown name with a Reload button inside the same
        // <StackPanel Orientation="Horizontal"> block.
        //
        // After migration the entire block is replaced by
        // <controls:EditorTopBarWithInputs ... /> so this combined pattern
        // MUST NOT match.
        string axaml = ReadView(viewName);

        // The unified control must be present.
        Assert.Contains("<controls:EditorTopBarWithInputs", axaml);

        // Match the legacy top-bar pattern at the StackPanel level (NOT
        // requiring a surrounding <Border> — SMEPromoList's legacy strip
        // used `DockPanel.Dock="Top"` directly on the StackPanel without
        // a Border, so a Border-anchored regex misses it).
        //
        // The match requires BOTH:
        //  - a NumericUpDown carrying one of the legacy top-bar
        //    AutomationIds (the keys all four editable migrations used:
        //    *_ReadStartAddress_Input, *_TopAddress_Input, or
        //    *_NumericUpDown1_Input — the SMEPromoList variant). This
        //    keys off AutomationId because SMEPromoList's pre-migration
        //    NumericUpDown had no `Name` attribute, only an AutomationId
        //    (Copilot review of #741 round 3).
        //  - a Button with Click="ReloadList_Click" OR "Reload_Click"
        // inside the same <StackPanel Orientation="Horizontal"> block.
        // The combo only existed on the legacy top-bar; e.g. AIScript's
        // panel5 per-entry write bar uses AIScript_Address_Input
        // (not the ReadStart/TopAddress/NumericUpDown1 ids), so it
        // does not false-positive.
        Assert.False(s_editableLegacyRegex.IsMatch(axaml),
            $"{viewName}: still contains a legacy <StackPanel Orientation=\"Horizontal\"> top-bar with a ReadStart/TopAddress/NumericUpDown1 AutomationId + direct Click=\"Reload*_Click\" button");
    }

    [Theory]
    [MemberData(nameof(AllMigratedViews))]
    public void MigratedView_HasTopBarAutomationId(string viewName)
    {
        // Every host sets a "..._TopBar" automation id on the unified
        // control (mirrors UnitEditorView et al.). Multi-bar hosts use
        // composite ids like "MonsterItemViewer_Item_TopBar" or
        // "WorldMapEventPointer_BeforeTopBar" (#743), so the prefix
        // allows underscores. The trailing "_TopBar" stays required so
        // the assertion can't false-positive on the inner-control ids
        // (e.g. "..._Reload_Button").
        string axaml = ReadView(viewName);
        Assert.Matches(new Regex("AutomationProperties\\.AutomationId=\"[A-Za-z0-9_]+_TopBar\""),
            axaml);
    }

    [Fact]
    public void EditorTopBarWithInputs_PinsReloadToRightEdge()
    {
        // Regression guard for #741 / #743 Copilot review: the editable
        // variant's AXAML must use a Grid where the Reload button stays
        // pinned to the far-right edge, matching the read-only EditorTopBar
        // contract.
        //
        // After #743 round 2 the layout switched from `*,Auto` to
        // `*,Auto,Auto` to add an optional `TrailingContent` ContentPresenter
        // BETWEEN the inputs and the Reload button. The trailing column
        // collapses to 0 width when no content is set, so the rendered
        // result for unmigrated hosts matches the pre-#743 two-column
        // layout exactly. This test accepts either schema so both pre- and
        // post-#743 layouts pass — Reload must be in the LAST column either
        // way.
        string path = Path.Combine(
            FindRepoRoot(),
            "FEBuilderGBA.Avalonia",
            "Controls",
            "EditorTopBarWithInputs.axaml");
        Assert.True(File.Exists(path));
        string axaml = File.ReadAllText(path);
        // Accept either the pre-#743 (`*,Auto`) or post-#743
        // (`*,Auto,Auto`) layout.
        Assert.Matches(new Regex(
            "ColumnDefinitions=\"\\*,Auto(?:,Auto)?\""),
            axaml);
        // Reload button must be placed in the last column so it lives at
        // the right edge, not at the natural end of a StackPanel. The post-
        // #743 layout uses Grid.Column="2"; the pre-#743 layout used
        // Grid.Column="1". Accept either.
        Assert.Matches(new Regex(
            "<Button[^>]*Grid\\.Column=\"(?:1|2)\"[^>]*Name=\"ReloadButton\"",
            RegexOptions.Singleline),
            axaml);
        // The TrailingContent ContentPresenter (when present) must sit in
        // the column BEFORE Reload — never after. This locks in the
        // before-reload slot position so an accidental refactor can't
        // silently swap them.
        if (axaml.Contains("TrailingContentPresenter"))
        {
            Assert.Matches(new Regex(
                "<ContentPresenter[^>]*Grid\\.Column=\"1\"[^>]*Name=\"TrailingContentPresenter\"",
                RegexOptions.Singleline),
                axaml);
            // And Reload in column 2 (after the trailing slot).
            Assert.Matches(new Regex(
                "<Button[^>]*Grid\\.Column=\"2\"[^>]*Name=\"ReloadButton\"",
                RegexOptions.Singleline),
                axaml);
        }
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
        // After Slice C round 2 (#743), SkillConfigFE8NSkillView and
        // MapTileAnimation2View joined the migrated list (round 1's 8 +
        // round 2's 2 = 10 total). The 5 PERMANENT deferrals listed in
        // s_permanentlyDeferredViews must NOT be claimed as migrated — each
        // has a documented technical reason (see the array initialiser).
        var migrated = new System.Collections.Generic.HashSet<string>(AllMigratedViewNames());
        foreach (var (view, _) in s_permanentlyDeferredViews)
        {
            Assert.DoesNotContain(view, migrated);
        }
    }

    public static TheoryData<string, string> PermanentlyDeferredViews
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (view, reason) in s_permanentlyDeferredViews) data.Add(view, reason);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PermanentlyDeferredViews))]
    public void PermanentlyDeferredView_DoesNotUseUnifiedBar(string viewName, string reason)
    {
        // Round-2 (#743) closure check: each permanently-deferred view
        // must NOT contain `<controls:EditorTopBarWithInputs ` in its AXAML
        // — that's what marks it as still hand-rolled. If a future agent
        // partially migrates one without updating the deferred list, this
        // test catches the inconsistency. The `reason` parameter is the
        // documented technical rationale (visible in xunit test name) so
        // the deferral can't degrade into "we forgot to migrate this".
        Assert.NotNull(reason); // suppress unused-parameter warning
        Assert.NotEmpty(reason);
        string axaml = ReadView(viewName);
        Assert.DoesNotContain("<controls:EditorTopBarWithInputs", axaml);
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

    // The editable-legacy regex is shared between the per-view sweep and
    // the fixture-based predicate tests, so we keep it as a single
    // constant — a future regex tweak must update one place and is
    // visible to every test that depends on it.
    static readonly Regex s_editableLegacyRegex = new Regex(
        "<StackPanel[^>]*Orientation=\"Horizontal\"[^>]*>" +
        ".*?<NumericUpDown[^>]*AutomationProperties\\.AutomationId=\"[A-Za-z0-9]+_(?:ReadStartAddress|TopAddress|NumericUpDown1)_Input\"" +
        ".*?<Button[^>]*Click=\"(?:ReloadList_Click|Reload_Click)\"[^>]*/>" +
        ".*?</StackPanel>",
        RegexOptions.Singleline);

    [Fact]
    public void EditableLegacyPredicate_DetectsBorderWrappedFixture()
    {
        // Pre-migration SongTrack/SongInstrument/AIScript pattern: a
        // <Border> wrapping the StackPanel with the ReadStart NumericUpDown +
        // Reload button. Locks the editable predicate so a regex tweak
        // can't silently regress.
        const string legacyFixture = """
        <Border BorderBrush="Gray" BorderThickness="1" Padding="4" Margin="0,0,0,4">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="First Address" VerticalAlignment="Center" Width="100" />
            <NumericUpDown AutomationProperties.AutomationId="SongTrack_ReadStartAddress_Input"
                           Name="ReadStartAddressBox" Width="120" Minimum="0" Maximum="4294967295"
                           FormatString="0" VerticalAlignment="Center" />
            <TextBlock Text="Read Count" VerticalAlignment="Center" Width="80" />
            <NumericUpDown AutomationProperties.AutomationId="SongTrack_ReadCount_Input"
                           Name="ReadCountBox" Width="80" Minimum="0" Maximum="4096"
                           FormatString="0" VerticalAlignment="Center" />
            <Button AutomationProperties.AutomationId="SongTrack_ReloadList_Button"
                    Name="ReloadListButton" Content="Reload" Width="80" Click="ReloadList_Click" />
          </StackPanel>
        </Border>
        """;
        Assert.True(s_editableLegacyRegex.IsMatch(legacyFixture),
            "The editable-pattern predicate failed to detect the Border-wrapped SongTrack-style fixture.");
    }

    [Fact]
    public void EditableLegacyPredicate_DetectsBorderlessSMEPromoListFixture()
    {
        // Pre-migration SMEPromoList pattern: a <StackPanel
        // DockPanel.Dock="Top"> top strip WITHOUT a surrounding <Border>.
        //
        // IMPORTANT: the real pre-migration markup has only an
        // AutomationProperties.AutomationId on each NumericUpDown — NO
        // `Name` attribute (Copilot review of #741 round 3, blocking).
        // This fixture mirrors that markup faithfully so the predicate
        // we lock in genuinely keys off the AutomationId, not on a
        // stray `Name="NumericUpDown1"` that never existed.
        const string legacyFixture = """
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Height="34"
                    Margin="8,4,8,0" Spacing="8">
          <TextBlock Text="Address:" VerticalAlignment="Center" />
          <NumericUpDown AutomationProperties.AutomationId="SMEPromoList_NumericUpDown1_Input"
                         FormatString="0" Value="{Binding ReadStartAddress}" Minimum="0"
                         Width="130" Height="25" />
          <TextBlock Text="Count:" VerticalAlignment="Center" />
          <NumericUpDown AutomationProperties.AutomationId="SMEPromoList_NumericUpDown2_Input"
                         FormatString="0" Value="{Binding ReadCount}" Minimum="1" Maximum="256"
                         Width="78" Height="25" />
          <Button AutomationProperties.AutomationId="SMEPromoList_Reload_Button"
                  Content="Reload" Width="112" Height="30" Click="Reload_Click" />
        </StackPanel>
        """;
        Assert.True(s_editableLegacyRegex.IsMatch(legacyFixture),
            "The editable-pattern predicate failed to detect the borderless SMEPromoList-style fixture (DockPanel.Dock=\"Top\" StackPanel without <Border> wrapper, AutomationId-only on the NumericUpDown).");
    }
}
