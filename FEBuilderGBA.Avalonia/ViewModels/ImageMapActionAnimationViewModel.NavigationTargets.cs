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
// As of #500 the Avalonia `ToolAnimationCreatorView` has a real
// `InitFromRom(...)` flow (and a paired `InitFromFile(...)` for the script-
// file path), and `ImageMapActionAnimationView.OpenInCreator_Click` invokes
// it. The manifest entry below is now a `Match` (IssueRef = null) and the
// gap-sweep scanner picks that up automatically.
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
                // the current map-action animation. Wired in #500.
                new NavigationTarget(
                    CommandName: "JumpToAnimationCreator",
                    TargetViewType: typeof(ToolAnimationCreatorView),
                    TargetAddress: null,
                    IssueRef: null),
            };
        }
    }
}
