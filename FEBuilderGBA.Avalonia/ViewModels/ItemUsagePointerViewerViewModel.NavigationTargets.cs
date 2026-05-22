// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ItemUsagePointerViewerViewModel. (#440)
//
// Split into a separate file to keep the `FEBuilderGBA.Avalonia.Views`
// dependency out of the main VM (which is exercised by Core tests via
// project reference). Purely declarative metadata — the actual click
// handlers in ItemUsagePointerViewerView.axaml.cs do the navigation;
// this file just records what targets exist so the Phase 4 scanner can
// cross-reference them against WinForms `InputFormRef.JumpForm<T>` callsites.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ItemUsagePointerViewerViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #440). ItemUsagePointerForm
        // exposes three JumpForm<T> callsites in WinForms:
        //
        //   1. PromotionItemLink_Click   → JumpForm<ItemPromotionForm>
        //   2. StatBoosterItemLink_Click → JumpForm<ItemStatBonusesForm>
        //   3. ERROR_IER_PATCH_Click     → JumpForm<PatchForm>
        //
        // All three are now wired in ItemUsagePointerViewerView.axaml.cs:
        //   1. PromotionItemLink_Click   → WindowManager.Navigate<ItemPromotionViewerView>
        //   2. StatBoosterItemLink_Click → WindowManager.Navigate<ItemStatBonusesViewerView>
        //   3. IerPatch_Click            → WindowManager.Open<PatchManagerView>
        //
        // Layer 1b of JumpParityScanner.BuildWfFormToAvViewsMap pairs
        // `PatchForm` ↔ `PatchManagerView` (declared in
        // ListParityHelper.GetExtraCrossViewMappings), so the WF callsite (3)
        // MATCHes the manifest row below. No IssueRef set — every row is working.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // 1. Item Usage → Promotion editor (when current filter is
                //    a promotion-related array). Navigates by current item ID.
                new NavigationTarget(
                    CommandName: "JumpToPromotion",
                    TargetViewType: typeof(ItemPromotionViewerView),
                    TargetAddress: null),

                // 2. Item Usage → Stat Bonuses (when current filter is
                //    a stat-booster array). Navigates by current item ID.
                new NavigationTarget(
                    CommandName: "JumpToStatBonuses",
                    TargetViewType: typeof(ItemStatBonusesViewerView),
                    TargetAddress: null),

                // 3. Item Usage → PatchManager (drive user to install IER
                //    when the patch is detected as already installed —
                //    mirrors the WF ERROR_IER_PATCH_Click handler).
                new NavigationTarget(
                    CommandName: "JumpToIerPatch",
                    TargetViewType: typeof(PatchManagerView),
                    TargetAddress: null),
            };
        }
    }
}
