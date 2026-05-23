// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for EventUnitFE7ViewModel (#431 / #374 Phase 4).
//
// Mirrors the three WF `EventUnitFE7Form` jump callsites:
//   - JUMP_BATTLETALK_Click -> EventBattleTalkFE7Form
//       (`f.JumpTo((uint)B0.Value)` — passes unit id)
//   - JUMP_BATTLEBGM_Click  -> SoundBossBGMForm
//       (`f.JumpTo((uint)B0.Value)` — passes unit id)
//   - JUMP_HAIKU_Click       -> EventHaikuFE7Form
//       (`f.JumpTo((uint)B0.Value, (uint)MAP_LISTBOX.SelectedIndex)` — unit id + map id)
//
// `TargetAddress: null` is the sentinel for "computed at click time" — the
// AV View code-behind dispatches to the Core search helpers
// (`MapEventUnitCore.FindBattleTalkFE7UnitIdAddress` etc.) and then opens
// the target editor with the resolved byte address. This matches the
// existing `UnitFE7ViewModel.NavigationTargets.cs` precedent for dynamic
// jumps.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class EventUnitFE7ViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToBattleTalk",
                    TargetViewType: typeof(EventBattleTalkFE7View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToBattleBGM",
                    TargetViewType: typeof(SoundBossBGMViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToHaiku",
                    TargetViewType: typeof(EventHaikuFE7View),
                    TargetAddress: null),
            };
        }
    }
}
