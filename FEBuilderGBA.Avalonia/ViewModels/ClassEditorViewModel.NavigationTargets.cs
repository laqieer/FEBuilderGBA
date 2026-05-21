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
        // Known-broken jumps (per issue #359 — "Add jump support to
        // Pointers/Movement/Terrain fields like Move Cost"): the Pointers
        // panel exposes BattleAnime / MoveCostRain / MoveCostSnow /
        // TerrainAvoid / TerrainDef / TerrainRes pointer textboxes, none of
        // which currently have Jump buttons. Their target editors are the
        // same MoveCost / Terrain editors that JumpToMoveCost reaches; we
        // tag each entry with IssueRef="#359" so Phase 4 surfaces them as
        // expected-skipped backlog rows until the fix PR adds the buttons.
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

                // Known-broken (#359): no Jump button for BattleAnimePtr;
                // WinForms ClassForm has one wired to ImageBattleAnimeForm
                // (which maps to ImageBattleAnimeView on the AV side).
                new NavigationTarget(
                    CommandName: "JumpToBattleAnime",
                    TargetViewType: typeof(ImageBattleAnimeView),
                    TargetAddress: null,
                    IssueRef: "#359"),

                // Known-broken (#359): MoveCostRain / MoveCostSnow pointer
                // textboxes have no Jump buttons. Target is MoveCostEditorView.
                new NavigationTarget(
                    CommandName: "JumpToMoveCostRain",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null,
                    IssueRef: "#359"),
                new NavigationTarget(
                    CommandName: "JumpToMoveCostSnow",
                    TargetViewType: typeof(MoveCostEditorView),
                    TargetAddress: null,
                    IssueRef: "#359"),

                // Known-broken (#359): Terrain (Avoid/Def/Res) pointer fields
                // have no Jump buttons. WinForms ClassForm wires these to the
                // floor-lookup table editors; we use MapTerrainFloorLookupTableView
                // as the closest existing AV target.
                new NavigationTarget(
                    CommandName: "JumpToTerrainAvoid",
                    TargetViewType: typeof(MapTerrainFloorLookupTableView),
                    TargetAddress: null,
                    IssueRef: "#359"),
                new NavigationTarget(
                    CommandName: "JumpToTerrainDef",
                    TargetViewType: typeof(MapTerrainFloorLookupTableView),
                    TargetAddress: null,
                    IssueRef: "#359"),
                new NavigationTarget(
                    CommandName: "JumpToTerrainRes",
                    TargetViewType: typeof(MapTerrainFloorLookupTableView),
                    TargetAddress: null,
                    IssueRef: "#359"),
            };
        }
    }
}
