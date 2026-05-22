// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for MapTerrainFloorLookupTableViewModel. (#442)
//
// Split into a separate file to keep the `FEBuilderGBA.Avalonia.Views`
// dependency out of the main VM (which is exercised by Core tests via
// project reference). Purely declarative metadata — the actual click
// handlers in MapTerrainFloorLookupTableView.axaml.cs do the navigation;
// this file just records what targets exist so the Phase 4 scanner can
// cross-reference them against WinForms `InputFormRef.JumpForm<T>` callsites.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class MapTerrainFloorLookupTableViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #442). MapTerrainFloorLookupTableForm
        // exposes three JumpForm<T> callsites in WinForms:
        //
        //   1. X_JUMP_BG_Click  → JumpForm<MapTerrainBGLookupTableForm>
        //   2. ERROR_Not_Allocated_Click → JumpForm<PatchForm>
        //   3. JumpToRef (static) → JumpForm<MapTerrainFloorLookupTableForm>
        //
        // All three are now wired in MapTerrainFloorLookupTableView.axaml.cs:
        //   1. JumpToBG_Click          → WindowManager.Navigate<MapTerrainBGLookupTableView>
        //   2. PatchInstall_Click      → WindowManager.Open<PatchManagerView>
        //   3. static JumpToRefAsync   → opens self with parsed (filter, list) selection
        //
        // Layer 1b of JumpParityScanner.BuildWfFormToAvViewsMap pairs
        // `PatchForm` ↔ `PatchManagerView`, so the WF callsite (2) MATCHes
        // the manifest row below. No IssueRef set — every row is working.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // 1. Floor → BG sister editor.
                new NavigationTarget(
                    CommandName: "JumpToBGLookup",
                    TargetViewType: typeof(MapTerrainBGLookupTableView),
                    TargetAddress: null),

                // 2. Floor → PatchManager (drive user to install ExtendsBattleBG).
                new NavigationTarget(
                    CommandName: "JumpToPatchExtendsBattleBG",
                    TargetViewType: typeof(PatchManagerView),
                    TargetAddress: null),

                // 3. Self-jump — JumpToRefAsync(text) parses a reference
                //    string and opens this view with the right filter+row.
                //    Mirrors WF JumpToRef.
                new NavigationTarget(
                    CommandName: "JumpToSelfFromRef",
                    TargetViewType: typeof(MapTerrainFloorLookupTableView),
                    TargetAddress: null),
            };
        }
    }
}
