// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for EventUnitViewModel (#420 / #374 Phase 4).
//
// Mirrors the six WF `EventUnitForm` jump callsites:
//   - JUMP_BATTLETALK_Click -> EventBattleTalkForm
//       (`f.JumpTo((uint)B0.Value, (uint)MAP_LISTBOX.SelectedIndex)` —
//        passes unit id + map id)
//   - JUMP_BATTLEBGM_Click  -> SoundBossBGMForm
//       (`f.JumpTo((uint)B0.Value)` — passes unit id)
//   - JUMP_HAIKU_Click       -> EventHaikuForm
//       (`f.JumpTo((uint)B0.Value, (uint)MAP_LISTBOX.SelectedIndex)` —
//        unit id + map id)
//   - NewButton_Click        -> EventUnitNewAllocForm (modal dialog)
//   - X_ITEMDROP_Click       -> EventUnitItemDropForm (modal dialog)
//   - X_RANDOMMONSTER_DoubleClick -> MonsterProbabilityForm
//       (`InputFormRef.JumpForm<MonsterProbabilityForm>(B1.Value)` —
//        pre-selects the class_id row)
//
// `TargetAddress: null` is the sentinel for "no static target address" —
// the AV View code-behind dispatches at click time. For
// JumpToBattleTalk/JumpToBattleBGM/JumpToHaiku, it calls the Core search
// helpers (`MapEventUnitCore.FindXxxFE8Address` etc.) and opens the
// target editor with the resolved byte address. For JumpToNewAlloc and
// JumpToItemDrop, it opens the proxy dialog view with no address (modal
// dialog flow). For JumpToMonsterProbability, the View resolves B1's
// class_id ROW INDEX to the matching Monster Probability entry address
// (MonsterProbabilityViewerViewModel.ResolveAddressByClassIndex) and
// Navigates so the viewer opens pre-selected on the class_id-indexed row
// (WF JumpForm selectedID == class_id parity, #1018). TargetAddress stays
// null because the address is resolved dynamically at click time.
// Matches the precedent from `EventUnitFE7ViewModel.NavigationTargets.cs`.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class EventUnitViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToBattleTalk",
                    TargetViewType: typeof(EventBattleTalkView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToBattleBGM",
                    TargetViewType: typeof(SoundBossBGMViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToHaiku",
                    TargetViewType: typeof(EventHaikuView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToNewAlloc",
                    TargetViewType: typeof(EventUnitNewAllocView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToItemDrop",
                    TargetViewType: typeof(EventUnitItemDropView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToMonsterProbability",
                    TargetViewType: typeof(MonsterProbabilityViewerView),
                    TargetAddress: null),
            };
        }
    }
}
