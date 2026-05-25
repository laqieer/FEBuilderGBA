// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for EventCondViewModel (#386 / #374 Phase 4).
//
// Mirrors the five WF EventCondForm jump callsites:
//   - Jump_TO_Event_Click       -> EventScriptForm
//       (EventCondForm.cs line 2215: InputFormRef.JumpForm<EventScriptForm>)
//   - Jump_TO_EventUnit_Click (FE8+)  -> EventUnitForm
//       (EventCondForm.cs line 2224)
//   - Jump_TO_EventUnit_Click (FE7)   -> EventUnitFE7Form
//       (EventCondForm.cs line 2229)
//   - Jump_TO_EventUnit_Click (FE6)   -> EventUnitFE6Form
//       (EventCondForm.cs line 2234)
//   - PreciseEevntCondArea -> MapPointerNewPLISTPopupForm
//       (EventCondForm.cs line 3119: InputFormRef.JumpFormLow<MapPointerNewPLISTPopupForm>)
//
// All five entries are listed. The first four are clean Match candidates — the
// WF jump opens the editor and lands the user on the correct row, and the
// Avalonia equivalent (WindowManager.Navigate<TView>(addr)) does the same.
//
// The fifth (MapPointerNewPLIST) is marked with IssueRef="#386-newalloc" as a
// KnownGap: the Avalonia popup opens but the WF commit-back state-machine
// (replacing the event PLIST in map settings after the new PLIST is selected)
// is intentionally deferred to a follow-up. The button still opens the dialog
// so users can see the new-allocation UI; the state machine commit is what
// remains incomplete in the Avalonia port.
//
// `TargetAddress: null` is the sentinel for "computed at click time" — the AV
// View code-behind dispatches to the appropriate target based on the current
// record/slot and ROM version (FE6/FE7/FE8). This matches the precedent set
// in EventUnitFE7ViewModel.NavigationTargets.cs (#431/PR #522).
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class EventCondViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToEventScript",
                    TargetViewType: typeof(EventScriptView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToEventUnit",
                    TargetViewType: typeof(EventUnitView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToEventUnitFE7",
                    TargetViewType: typeof(EventUnitFE7View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToEventUnitFE6",
                    TargetViewType: typeof(EventUnitFE6View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToMapPointerNewPLIST",
                    TargetViewType: typeof(MapPointerNewPLISTPopupView),
                    TargetAddress: null,
                    IssueRef: "#386-newalloc"),
            };
        }
    }
}
