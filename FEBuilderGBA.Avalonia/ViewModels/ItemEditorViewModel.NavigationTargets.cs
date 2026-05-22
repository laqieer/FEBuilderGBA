// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ItemEditorViewModel. (#374)
//
// Split into a separate file to isolate the `FEBuilderGBA.Avalonia.Views`
// dependency from the main VM. The interface implementation is purely
// declarative metadata — it never changes the navigation behavior of the
// actual click handlers in ItemEditorView.axaml.cs.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ItemEditorViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374). Mirror the navigation
        // callsites in ItemEditorView.axaml.cs WITHOUT changing actual
        // navigation behavior. Both Effectiveness jump paths are now fixed:
        // SkillSystems Rework by #362 / PR #456, and vanilla by #363.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Working: text-id jumps (Name / Desc / Use-Desc).
                new NavigationTarget(
                    CommandName: "JumpToNameText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToDescText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToUseDescText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),

                // Stat-bonus jump targets vary by patch state (skill-system /
                // Venno / vanilla) — these are documented as separate manifest
                // entries so the scanner sees all three.
                new NavigationTarget(
                    CommandName: "JumpToStatBonusesSkillSystem",
                    TargetViewType: typeof(ItemStatBonusesSkillSystemsView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToStatBonusesVenno",
                    TargetViewType: typeof(ItemStatBonusesVennoView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToStatBonusesVanilla",
                    TargetViewType: typeof(ItemStatBonusesViewerView),
                    TargetAddress: null),

                // Effectiveness jumps split by patch state. Both paths
                // enumerate items by their P16 pointer (skill-systems-rework
                // fixed by #362 / PR #456; vanilla fixed by #363) so the
                // address passed by ItemEditorView.JumpToEffectiveness_Click
                // matches a real list row in either receiver.
                new NavigationTarget(
                    CommandName: "JumpToEffectivenessSkillSystem",
                    TargetViewType: typeof(ItemEffectivenessSkillSystemsReworkView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToEffectivenessVanilla",
                    TargetViewType: typeof(ItemEffectivenessViewerView),
                    TargetAddress: null),
            };
        }
    }
}
