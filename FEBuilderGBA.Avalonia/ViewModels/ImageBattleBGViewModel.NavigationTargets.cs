// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ImageBattleBGViewModel. (#434)
//
// Split into a separate file so the `FEBuilderGBA.Avalonia.Views` dependency
// stays out of the main VM. Purely declarative metadata — the actual click
// handlers in ImageBattleBGView.axaml.cs do the navigation; this file just
// records what targets exist so the Phase 4 scanner can cross-reference
// them against WinForms `InputFormRef.JumpForm<T>` callsites.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageBattleBGViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #434). ImageBattleBGForm
        // exposes two JumpForm<T> callsites in WinForms:
        //
        //   1. GraphicsToolButton_Click → JumpFormLow<GraphicsToolForm>
        //      followed by `f.Jump(width, height, image, ..., tsa, ..., palette, ...)`
        //   2. DecreaseColorTSAToolButton_Click → JumpForm<DecreaseColorTSAToolForm>
        //      followed by `f.InitMethod(2)`
        //
        // Both are now wired in ImageBattleBGView.axaml.cs:
        //   1. GraphicsTool_Click → WindowManager.Open<GraphicsToolView>
        //      followed by view.Jump(...)
        //   2. DecreaseColor_Click → WindowManager.Open<DecreaseColorTSAToolView>
        //      followed by view.InitMethod(2)
        //
        // The 2026-05-24 jumps-sweep already pairs both target views in
        // the WF-to-AV map (lines 271-272). No IssueRef set — every row is
        // working after this PR.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // 1. BG → Graphics Tool (with pre-populated image+tsa+palette).
                new NavigationTarget(
                    CommandName: "JumpToGraphicsTool",
                    TargetViewType: typeof(GraphicsToolView),
                    TargetAddress: null),

                // 2. BG → Color Reduce Tool (with InitMethod(2) for BG mode).
                new NavigationTarget(
                    CommandName: "JumpToDecreaseColor",
                    TargetViewType: typeof(DecreaseColorTSAToolView),
                    TargetAddress: null),
            };
        }
    }
}
