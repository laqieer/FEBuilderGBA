// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for the two root causes behind the 2026-05-22 scheduled E2E
// failures (#498/#502/#509/#514/#515):
//
//   Root cause #1 — `FormatString="X8"` (or X4/X2) on Avalonia's
//   decimal-typed NumericUpDown throws FormatException when a value is
//   assigned. The exception interrupts the enclosing code, leaving
//   downstream NumericUpDown.Value at the default null. The
//   `--data-verify` UI check then reports `UI_EMPTY` for any NUD whose
//   Value is null after the view's Opened handler runs.
//
//   Root cause #2 — `IdFieldControl.ValueBox` (the inner NumericUpDown)
//   stays at Value=null when the host's StyledProperty Value default is 0
//   and the host writes 0 to it (no PropertyChanged event fires).
//
// These tests open each affected editor via the headless harness with a
// ROM loaded, run Show() (which fires the Opened handler synchronously
// before returning in Avalonia.Headless), and assert that every
// effectively-visible NumericUpDown has a non-null Value — mirroring the
// production data-verify UI check in MainWindow.CheckNumericUpDownsDisplayValues,
// which also filters only by IsEffectivelyVisible (not IsEnabled — disabled
// NUDs with stale state are intentionally caught).
//
// Marked [Collection("SharedState")] because the tests mutate
// CoreState.ROM via RomTestHelper.WithRom.
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class DataVerifyEmptyNumericUpDownTests
{
    readonly ITestOutputHelper _output;

    public DataVerifyEmptyNumericUpDownTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Returns the names of every effectively-visible NumericUpDown whose
    /// Value is null. This mirrors the predicate
    /// `MainWindow.CheckNumericUpDownsDisplayValues` uses to emit
    /// `UIVERIFY: {view}|emptyNUDs=...` lines — `IsEffectivelyVisible` only,
    /// no `IsEnabled` filter, so disabled NUDs left at `null` still count
    /// as empty (which is the bug we're guarding against here).
    /// </summary>
    static List<string> FindEmptyVisibleNuds(Control control)
    {
        var empty = new List<string>();
        foreach (var descendant in control.GetVisualDescendants())
        {
            if (descendant is NumericUpDown nud)
            {
                if (!nud.IsEffectivelyVisible) continue;
                if (nud.Value == null)
                    empty.Add(nud.Name ?? "(unnamed)");
            }
        }
        return empty;
    }

    /// <summary>
    /// Helper: instantiates the view, calls Show() so Opened fires, calls
    /// SelectFirstItem via the IEditorView interface (compile-time safe —
    /// avoids the brittleness of reflection if the method name changes),
    /// and checks every NumericUpDown for null Value.
    /// </summary>
    static List<string> OpenAndCollectEmptyNuds<TView>() where TView : Control, IEditorView, new()
    {
        var view = new TView();
        try
        {
            if (view is Window window)
                window.Show();
            else if (view is IEmbeddableEditor embeddable)
                embeddable.Show();
            else
                throw new InvalidOperationException($"{typeof(TView).Name} is neither Window nor IEmbeddableEditor.");
            // SelectFirstItem mirrors what the data-verify harness does after
            // the Opened handler runs (MainWindow.RunDataVerify line ~1352).
            // Every editor that data-verify exercises implements IEditorView,
            // so the cast is compile-time enforced.
            view.SelectFirstItem();
            return FindEmptyVisibleNuds(view);
        }
        finally
        {
            if (view is Window window)
                window.Close();
            else if (view is IEmbeddableEditor embeddable)
                embeddable.Close();
        }
    }

    /// <summary>
    /// Returns true if the ROM is available, logs a SKIP message otherwise.
    /// xUnit doesn't natively support "Skipped" results from inside test bodies,
    /// but emitting via ITestOutputHelper makes the skip visible in CI logs
    /// (per Copilot PR #545 review #3 — silent `return` is not visible).
    /// </summary>
    bool RomAvailable(string version)
    {
        if (TestRomLocator.FindRom(version) != null) return true;
        _output.WriteLine($"SKIP: {version}.gba not found in roms/ or ROMS_DIR — data-verify NUD regression test cannot run for {version}.");
        return false;
    }

    // -----------------------------------------------------------------
    // Root cause #1 — hex FormatString on NumericUpDown.
    // These four editors carry a `FormatString="X8"` ReadStartAddressBox /
    // ItemAddressBox that throws during the LoadList path, leaving the
    // subsequent ReadCountBox.Value (FormatString="0") at null.
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void MapTerrainBGLookupTableView_NoEmptyNumericUpDowns_FE6()
    {
        if (!RomAvailable("FE6")) return;
        RomTestHelper.WithRom("FE6", () =>
        {
            var empty = OpenAndCollectEmptyNuds<MapTerrainBGLookupTableView>();
            Assert.True(empty.Count == 0,
                $"MapTerrainBGLookupTableView (FE6) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    [AvaloniaFact]
    public void MapTerrainFloorLookupTableView_NoEmptyNumericUpDowns_FE6()
    {
        if (!RomAvailable("FE6")) return;
        RomTestHelper.WithRom("FE6", () =>
        {
            var empty = OpenAndCollectEmptyNuds<MapTerrainFloorLookupTableView>();
            Assert.True(empty.Count == 0,
                $"MapTerrainFloorLookupTableView (FE6) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    [AvaloniaFact]
    public void ImageBGView_NoEmptyNumericUpDowns_FE6()
    {
        if (!RomAvailable("FE6")) return;
        RomTestHelper.WithRom("FE6", () =>
        {
            var empty = OpenAndCollectEmptyNuds<ImageBGView>();
            Assert.True(empty.Count == 0,
                $"ImageBGView (FE6) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    [AvaloniaFact]
    public void ImageBattleBGView_NoEmptyNumericUpDowns_FE6()
    {
        if (!RomAvailable("FE6")) return;
        RomTestHelper.WithRom("FE6", () =>
        {
            var empty = OpenAndCollectEmptyNuds<ImageBattleBGView>();
            Assert.True(empty.Count == 0,
                $"ImageBattleBGView (FE6) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    // -----------------------------------------------------------------
    // Root cause #2 — IdFieldControl inner ValueBox.Value stays null when
    // the host's Value default (0u) equals the first-item value loaded
    // from ROM. On FE8U, CCBranch entry 0 has PromotionClass1=0|2=0 and
    // MonsterItem entry 0 has ItemId=0, both reproducing the trap.
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void CCBranchEditorView_NoEmptyNumericUpDowns_FE8U()
    {
        if (!RomAvailable("FE8U")) return;
        RomTestHelper.WithRom("FE8U", () =>
        {
            var empty = OpenAndCollectEmptyNuds<CCBranchEditorView>();
            Assert.True(empty.Count == 0,
                $"CCBranchEditorView (FE8U) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    [AvaloniaFact]
    public void MonsterItemViewerView_NoEmptyNumericUpDowns_FE8U()
    {
        if (!RomAvailable("FE8U")) return;
        RomTestHelper.WithRom("FE8U", () =>
        {
            var empty = OpenAndCollectEmptyNuds<MonsterItemViewerView>();
            Assert.True(empty.Count == 0,
                $"MonsterItemViewerView (FE8U) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    // -----------------------------------------------------------------
    // Root cause #3 — KnownGap NumericUpDown left at Avalonia's
    // decimal? default null because nothing in UpdateUI() seeds a value.
    // ImageUnitPaletteView's BattleAnimeBox is IsEnabled="False" /
    // ToolTip.Tip="KnownGap" but still IsEffectivelyVisible, so the
    // production data-verify UI check flags it as UI_EMPTY.
    // Surfaced in 2026-05-24/25 scheduled E2E failures
    // (#612/#613/#616/#623/#625), introduced when ImageUnitPaletteView
    // was added by PR #585 (closes #397) after PR #545 had cleared the
    // hex-FormatString and IdFieldControl traps.
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void ImageUnitPaletteView_NoEmptyNumericUpDowns_FE8U()
    {
        if (!RomAvailable("FE8U")) return;
        RomTestHelper.WithRom("FE8U", () =>
        {
            var empty = OpenAndCollectEmptyNuds<ImageUnitPaletteView>();
            Assert.True(empty.Count == 0,
                $"ImageUnitPaletteView (FE8U) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    // -----------------------------------------------------------------
    // Root cause #4 — NumericUpDown left at Avalonia's decimal? default
    // null because the static read-config bar declares Value-less
    // controls and nothing in LoadList()/UpdateUI() seeds them, AND the
    // four KnownGap N00/N08/N10/N18 PanPotBox controls in the per-type
    // tabs are likewise never seeded (the pan-pot field is not part of
    // the Avalonia VM yet — same shape as ImageUnitPaletteView's
    // BattleAnimeBox in commit e22139266). Both shapes sit on an
    // IsEffectivelyVisible surface (the static read bar always; the
    // PanPotBoxes whenever the loaded instrument's HeaderByte selects
    // the matching DirectSound tab), so the production data-verify UI
    // check flags them as UI_EMPTY. Surfaced in 2026-05-25/26 scheduled
    // E2E failures (#639/#640/#641/#642/#643), introduced when
    // SongInstrumentView landed via PR #626 (closes #387).
    //
    // The FE8U variant guards the ReadCountBox shape — instrument 0 has
    // HeaderByte=0x01 (SquareWave1), which selects the N01 tab, so only
    // the static read bar is visible.
    //
    // The FE7U variant guards the PanPotBox shape — instrument 0 has
    // HeaderByte=0x00 (DirectSound), which selects the N00 tab, making
    // N00_PanPotBox visible at load time.
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void SongInstrumentView_NoEmptyNumericUpDowns_FE8U()
    {
        if (!RomAvailable("FE8U")) return;
        RomTestHelper.WithRom("FE8U", () =>
        {
            var empty = OpenAndCollectEmptyNuds<SongInstrumentView>();
            Assert.True(empty.Count == 0,
                $"SongInstrumentView (FE8U) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    [AvaloniaFact]
    public void SongInstrumentView_NoEmptyNumericUpDowns_FE7U()
    {
        if (!RomAvailable("FE7U")) return;
        RomTestHelper.WithRom("FE7U", () =>
        {
            var empty = OpenAndCollectEmptyNuds<SongInstrumentView>();
            Assert.True(empty.Count == 0,
                $"SongInstrumentView (FE7U) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    // -----------------------------------------------------------------
    // #747 / #748: extend the regression matrix to FE7J and FE8J so all
    // four daily-failing ROMs are pinned by the headless suite, not just
    // the FE7U/FE8U pair that originally shipped.
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void SongInstrumentView_NoEmptyNumericUpDowns_FE7J()
    {
        if (!RomAvailable("FE7J")) return;
        RomTestHelper.WithRom("FE7J", () =>
        {
            var empty = OpenAndCollectEmptyNuds<SongInstrumentView>();
            Assert.True(empty.Count == 0,
                $"SongInstrumentView (FE7J) has empty NUDs: {string.Join(", ", empty)}");
        });
    }

    [AvaloniaFact]
    public void SongInstrumentView_NoEmptyNumericUpDowns_FE8J()
    {
        if (!RomAvailable("FE8J")) return;
        RomTestHelper.WithRom("FE8J", () =>
        {
            var empty = OpenAndCollectEmptyNuds<SongInstrumentView>();
            Assert.True(empty.Count == 0,
                $"SongInstrumentView (FE8J) has empty NUDs: {string.Join(", ", empty)}");
        });
    }
}
