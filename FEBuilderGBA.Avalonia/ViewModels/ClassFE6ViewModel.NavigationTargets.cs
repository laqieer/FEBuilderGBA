// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ClassFE6ViewModel. (#388 / #374)
//
// FE6 class jumps target MoveCostFE6View (not MoveCostEditorView), per
// the ListParityHelper registration and the FE6-specific MoveCostFE6ViewModel
// data shape. The CC Branch jump is intentionally absent because FE6 has no
// cc_branch table (that's an FE8-only feature surfaced by ClassEditorView).
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ClassFE6ViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — declarative manifest of cross-editor
        // jumps this VM exposes. Mirror the navigation callsites in
        // ClassFE6View.axaml.cs WITHOUT changing actual click behavior.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // BattleAnime (P48) -> ImageBattleAnimeView. The click
                // handler converts the GBA pointer to a ROM offset (matches
                // ClassEditorView.JumpToBattleAnime_Click).
                new NavigationTarget(
                    CommandName: "JumpToBattleAnime",
                    TargetViewType: typeof(ImageBattleAnimeView),
                    TargetAddress: null),

                // MoveCost (P52) -> MoveCostFE6View with CostType.MoveCostNormal.
                new NavigationTarget(
                    CommandName: "JumpToMoveCost_FE6",
                    TargetViewType: typeof(MoveCostFE6View),
                    TargetAddress: null),

                // Terrain Avoid (P56) -> MoveCostFE6View with CostType.TerrainAvoid.
                new NavigationTarget(
                    CommandName: "JumpToTerrainAvoid_FE6",
                    TargetViewType: typeof(MoveCostFE6View),
                    TargetAddress: null),

                // Terrain Def (P60) -> MoveCostFE6View with CostType.TerrainDefense.
                new NavigationTarget(
                    CommandName: "JumpToTerrainDef_FE6",
                    TargetViewType: typeof(MoveCostFE6View),
                    TargetAddress: null),

                // Terrain Res (P64) -> MoveCostFE6View with CostType.TerrainResistance.
                new NavigationTarget(
                    CommandName: "JumpToTerrainRes_FE6",
                    TargetViewType: typeof(MoveCostFE6View),
                    TargetAddress: null),

                // HardCoding warning hyperlink -> PatchManagerView filter
                // (matches ClassEditorView.HardCodingWarning_Click).
                new NavigationTarget(
                    CommandName: "JumpToHardCodingPatch",
                    TargetViewType: typeof(PatchManagerView),
                    TargetAddress: null),

                // Name text id (W0) link -> TextViewerView.
                new NavigationTarget(
                    CommandName: "JumpToNameText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),

                // Desc text id (W2) link -> TextViewerView.
                new NavigationTarget(
                    CommandName: "JumpToDescText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),

                // Portrait link (W8) -> PortraitViewerView (matches the
                // actual Avalonia type name used by ClassEditorViewModel).
                new NavigationTarget(
                    CommandName: "JumpToPortrait",
                    TargetViewType: typeof(PortraitViewerView),
                    TargetAddress: null),
            };
        }
    }
}
