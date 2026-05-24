// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ItemFE6ViewModel. (#374 / #402)
//
// Split into a separate file to isolate the `FEBuilderGBA.Avalonia.Views`
// dependency from the main VM. Purely declarative metadata.
//
// #402 expands the manifest from 1 row (DescText only) to 7 rows:
//   text jumps (3): JumpToNameText, JumpToDescText, JumpToUseDescText
//   feature jumps (4): JumpToHardcoding, JumpToWeaponEffect,
//                      JumpToStatBonuses, JumpToEffectiveness
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ItemFE6ViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #402).
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Text jumps - W0/W2/W4 hyperlink labels open
                // TextViewerView at the entry for the selected text id.
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

                // #402 feature jumps mirroring WF buttons:
                //   HardCodingWarningLabel -> PatchForm filtered on
                //     HARDCODING_ITEM=NN (NN = list index)
                //   JumpToITEMEFFECT       -> ItemWeaponEffectForm at the
                //     row whose B0 == list index (linear scan, NOT
                //     base + index*16 - the table is not contiguous)
                //   P12 ptr Jump           -> ItemStatBonusesForm
                //   P16 ptr Jump           -> ItemEffectivenessForm
                new NavigationTarget(
                    CommandName: "JumpToHardcoding",
                    TargetViewType: typeof(PatchManagerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToWeaponEffect",
                    TargetViewType: typeof(ItemWeaponEffectViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToStatBonuses",
                    TargetViewType: typeof(ItemStatBonusesViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToEffectiveness",
                    TargetViewType: typeof(ItemEffectivenessViewerView),
                    TargetAddress: null),
            };
        }
    }
}
