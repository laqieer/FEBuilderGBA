// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for AIScriptViewModel (#410 / #374 Phase 4).
//
// Mirrors the wired WF `AIScriptForm` cross-editor jump callsites that the
// Avalonia view actually triggers after PR #410. Per Copilot CLI plan-review
// v2 #1 (the strict reading of `INavigationTargetSource`), manifest rows
// correspond ONLY to working `WindowManager.Navigate<>` callsites; the
// heavy AI sub-editors that require modal pick + post-dialog value
// propagation (AIUnits, AITiles, AIASMCoordinate, AIASMRange,
// AIASMCALLTALK, AIScriptCategorySelect) stay DELIBERATELY ABSENT until
// their host runtime wiring lands — `JumpParityScanner` reports them as
// `MissingAvManifest`, which is the truthful state.
//
// This matches the SongTrack PR #412 precedent (3 import-dispatch flows
// absent from the manifest while their underlying Core extraction is in
// flight) and is documented in `INavigationTargetSource.cs` as the contract
// for manifest entries: each row mirrors a real, wired AV navigation
// callsite.
//
// Wired callsites (with version dispatch in the view code-behind):
//   - JumpToUnit              -> UnitEditorView    (FE8U / FE8JP)
//   - JumpToUnitFE7           -> UnitFE7View       (FE7)
//   - JumpToUnitFE6           -> UnitFE6View       (FE6)
//   - JumpToClass             -> ClassEditorView   (FE7 / FE8)
//   - JumpToClassFE6          -> ClassFE6View      (FE6)
//   - JumpToDisASM            -> DisASMView        (POINTER_ASM args)
//   - JumpToPointerToolCopyTo -> PointerToolCopyToView (Address double-click)
//
// `TargetAddress: null` is the sentinel for "manifest declares the jump but
// the runtime address is determined at click time" — matches the precedent
// used by SongTrackViewModel.NavigationTargets.cs and EventUnitViewModel.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class AIScriptViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // ---------------- Unit jumps (3-way version dispatch) ----------------
                new NavigationTarget(
                    CommandName: "JumpToUnit",
                    TargetViewType: typeof(UnitEditorView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToUnitFE7",
                    TargetViewType: typeof(UnitFE7View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToUnitFE6",
                    TargetViewType: typeof(UnitFE6View),
                    TargetAddress: null),

                // ---------------- Class jumps (2-way version dispatch) ----------------
                new NavigationTarget(
                    CommandName: "JumpToClass",
                    TargetViewType: typeof(ClassEditorView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToClassFE6",
                    TargetViewType: typeof(ClassFE6View),
                    TargetAddress: null),

                // ---------------- DisASM (POINTER_ASM args) ----------------
                new NavigationTarget(
                    CommandName: "JumpToDisASM",
                    TargetViewType: typeof(DisASMView),
                    TargetAddress: null),

                // ---------------- Address-double-click pointer copy ----------------
                new NavigationTarget(
                    CommandName: "JumpToPointerToolCopyTo",
                    TargetViewType: typeof(PointerToolCopyToView),
                    TargetAddress: null),
            };
        }
    }
}
