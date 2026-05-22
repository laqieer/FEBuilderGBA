// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for MapTerrainBGLookupTableViewModel. (#441)
//
// Split into a separate file so the `FEBuilderGBA.Avalonia.Views` dependency
// stays out of the main VM (which is exercised by Core tests via project
// reference). Purely declarative metadata — the actual click handlers in
// MapTerrainBGLookupTableView.axaml.cs do the navigation; this file just
// records what targets exist so the Phase 4 scanner can cross-reference
// them against WinForms `InputFormRef.JumpForm<T>` callsites.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class MapTerrainBGLookupTableViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #441). MapTerrainBGLookupTableForm
        // exposes three JumpForm<T> callsites in WinForms:
        //
        //   1. X_JUMP_FLOOR_Click → JumpForm<MapTerrainFloorLookupTableForm>
        //   2. ERROR_Not_Allocated_Click → JumpForm<PatchForm>
        //   3. JumpToRef (static) → JumpForm<MapTerrainBGLookupTableForm>
        //
        // All three are now wired in MapTerrainBGLookupTableView.axaml.cs:
        //   1. JumpToBattleFloor_Click → WindowManager.Open<MapTerrainFloorLookupTableView>
        //      followed by NavigateToFilterAndRow(filter, row) to preserve
        //      both axes (WindowManager.Navigate<T>(uint) only accepts a
        //      single address — Copilot CLI plan-review point 2).
        //   2. PatchInstall_Click   → WindowManager.Open<PatchManagerView>
        //   3. static JumpToRef     → opens self with parsed (filter, list) selection
        //
        // Layer 1b of JumpParityScanner.BuildWfFormToAvViewsMap pairs
        // `PatchForm` ↔ `PatchManagerView` (declared in #482 by ListParityHelper
        // .GetExtraCrossViewMappings), so the WF callsite (2) MATCHes the
        // manifest row below. No IssueRef set — every row is working.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // 1. BG → Floor sister editor.
                new NavigationTarget(
                    CommandName: "JumpToFloorLookup",
                    TargetViewType: typeof(MapTerrainFloorLookupTableView),
                    TargetAddress: null),

                // 2. BG → PatchManager (drive user to install ExtendsBattleBG).
                new NavigationTarget(
                    CommandName: "JumpToPatchExtendsBattleBG",
                    TargetViewType: typeof(PatchManagerView),
                    TargetAddress: null),

                // 3. Self-jump — static JumpToRef(text) parses a reference
                //    string and opens this view with the right filter+row.
                //    Mirrors WF MapTerrainBGLookupTableForm.JumpToRef.
                new NavigationTarget(
                    CommandName: "JumpToSelfFromRef",
                    TargetViewType: typeof(MapTerrainBGLookupTableView),
                    TargetAddress: null),
            };
        }
    }
}
