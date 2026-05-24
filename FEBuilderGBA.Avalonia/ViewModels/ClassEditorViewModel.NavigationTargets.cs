// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ClassEditorViewModel. (#374)
//
// This partial class lives in a separate file because it depends on
// `FEBuilderGBA.Avalonia.Views` types. The main `ClassEditorViewModel.cs`
// file is linked into `FEBuilderGBA.Core.Tests` (cross-platform Core test
// project that can't reach the Views namespace), so keeping the manifest
// here lets us declare the View-typed targets compile-time-safe while the
// Core tests stay buildable.
//
// The interface implementation is purely declarative metadata — it never
// changes the navigation behavior of the actual click handlers in
// ClassEditorView.axaml.cs. The Phase 4 scanner reads this manifest via
// reflection to cross-reference against WinForms `InputFormRef.JumpForm<T>`
// callsites in `ClassForm.cs` and surface parity gaps.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ClassEditorViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374). Declarative manifest of
        // cross-editor jumps this VM exposes. Mirror the navigation callsites
        // in ClassEditorView.axaml.cs WITHOUT changing any actual behavior.
        //
        // Issue #359 closed: all six BattleAnime / MoveCostRain /
        // MoveCostSnow / TerrainAvoid / TerrainDef / TerrainRes jumps now
        // have Click handlers + UI buttons. The terrain jumps target
        // MoveCostEditorView (the same shared editor that handles all six
        // MOVECOST1..6 WinForms cost types via the CostType combo), not
        // the floor-lookup table — the WinForms `MOVECOST4/5/6` linktypes
        // also dispatch into MoveCostForm, so MoveCostEditorView is the
        // correct parity target.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Working: JumpToMoveCost_Click → Navigate<MoveCostEditorView>.
                // Address is the current CLASS address (the table-offset bug
                // was fixed in #344/#346 — see ClassEditorView.axaml.cs:534).
                new NavigationTarget(
                    CommandName: "JumpToMoveCost",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null),

                // Working: text-id jumps via NavigateToTextId. Two entries
                // (Name and Description) reflect the two on-form buttons.
                new NavigationTarget(
                    CommandName: "JumpToNameText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToDescText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),

                // Working: portrait link click.
                new NavigationTarget(
                    CommandName: "JumpToPortrait",
                    TargetViewType: typeof(PortraitViewerView),
                    TargetAddress: null),

                // Fixed in #359: JumpToBattleAnime_Click →
                // Navigate<ImageBattleAnimeView>(U.toOffset(rawPtr)). The
                // conversion from raw GBA pointer to ROM offset is required
                // because the receiving EntryList stores ROM offsets.
                new NavigationTarget(
                    CommandName: "JumpToBattleAnime",
                    TargetViewType: typeof(ImageBattleAnimeView),
                    TargetAddress: null),

                // Fixed in #359: Ptr60 → MoveCostEditorView with CostType
                // MoveCostRain (FE7/8) or TerrainAvoid (FE6). Both paths
                // land in MoveCostEditorView; the version-aware dispatch
                // is in the Click handler, not the manifest.
                new NavigationTarget(
                    CommandName: "JumpToMoveCostRain",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null),

                // Fixed in #359: Ptr64 → MoveCostEditorView with CostType
                // MoveCostSnow (FE7/8) or TerrainDefense (FE6).
                new NavigationTarget(
                    CommandName: "JumpToMoveCostSnow",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null),

                // Fixed in #359: Terrain Avoid/Def/Res Jumps land in
                // MoveCostEditorView (not MapTerrainFloorLookupTableView —
                // the WinForms MOVECOST4/5/6 linktypes also dispatch into
                // MoveCostForm, so MoveCostEditorView is the parity target).
                new NavigationTarget(
                    CommandName: "JumpToTerrainAvoid",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToTerrainDef",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToTerrainRes",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null),

                // Added in #406: HardCoding warning hyperlink opens the
                // patch manager filtered to the hardcoded-class entry
                // (mirrors WF ClassForm.HardCodingWarningLabel_Click which
                // calls InputFormRef.JumpForm<PatchForm>() and JumpTo
                // "HARDCODING_CLASS=<id>"). Address slot is unused — the
                // jump uses a string filter key, not a numeric address.
                new NavigationTarget(
                    CommandName: "JumpToHardCodingPatch",
                    TargetViewType: typeof(PatchManagerView),
                    TargetAddress: null),

                // Added in #406: FE8-only CC Branch jump (parity with WF
                // ClassForm.J_5_Click which calls
                // InputFormRef.JumpForm<CCBranchForm>(SelectedIndex)).
                // The address slot is left null at manifest time — the live
                // click handler dispatches the current 0-based class id
                // (`ClassList.SelectedOriginalIndex`) to
                // `Navigate<CCBranchEditorView>(uint)` at click time. The
                // manifest records that this jump exists and where it lands,
                // not the runtime parameter (matches the rest of this VM's
                // manifest where all `TargetAddress` slots are null because
                // the addresses are computed per click). Skip note: the
                // click handler short-circuits for non-FE8 ROMs.
                new NavigationTarget(
                    CommandName: "JumpToCCBranch_FE8",
                    TargetViewType: typeof(CCBranchEditorView),
                    TargetAddress: null),
            };
        }
    }
}
