// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ImageMapActionAnimationViewModel (#433).
//
// Split into a separate file because INavigationTargetSource lives in
// FEBuilderGBA.Avalonia.Services while the View target types live in
// FEBuilderGBA.Avalonia.Views. The main VM file stays free of Views-namespace
// coupling.
//
// The WinForms `ImageMapActionAnimationForm` exposes ONE JumpForm callsite:
//   X_N_JumpEditor_Click -> InputFormRef.JumpFormLow<ToolAnimationCreatorForm>
//
// On the Avalonia side, `ToolAnimationCreatorView` is currently a UI shell
// with empty `Create_Click` / `BrowseImage_Click` handlers — calling
// `WindowManager.Open<ToolAnimationCreatorView>()` would just open a
// non-functional window. Therefore the manifest declares this jump as a
// `KnownGap` (non-null `IssueRef`) tracked by the follow-up issue
// #<followup-creator-init>. The view code-behind intentionally does NOT
// render a jump button until the target editor implements its Init flow —
// this is parity-via-declaration, not parity-via-broken-UI.
//
// Once the follow-up issue lands, the IssueRef can be cleared and a button
// added to the AXAML — at that point the gap-sweep scanner will
// automatically flip the row from KnownGap to Match.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageMapActionAnimationViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // WF X_N_JumpEditor_Click — open the Animation Creator on
                // the current map-action animation. Tracked as KnownGap
                // until the Avalonia `ToolAnimationCreatorView` implements
                // a real Init flow (see issue #500).
                new NavigationTarget(
                    CommandName: "JumpToAnimationCreator",
                    TargetViewType: typeof(ToolAnimationCreatorView),
                    TargetAddress: null,
                    IssueRef: "#500"),
            };
        }
    }
}
