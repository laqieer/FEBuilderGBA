// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ImageBGViewModel (#429).
//
// Split into a separate file so the `FEBuilderGBA.Avalonia.Views`
// dependency stays out of the main VM. Purely declarative metadata —
// the actual click handlers in ImageBGView.axaml.cs do the navigation;
// this file just records what targets exist so the Phase 4 scanner can
// cross-reference them against WinForms `InputFormRef.JumpForm<T>`
// callsites in ImageBGForm.cs.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageBGViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #429). ImageBGForm
        // exposes 3 JumpForm callsites in WinForms (see jumps-sweep
        // 2026-05-25 lines 256-258):
        //
        //   1. GraphicsToolButton_Click → JumpFormLow<GraphicsToolForm>
        //      followed by `f.Jump(width, height, image, imageType, tsa, tsaType, palette, ...)`
        //   2. DecreaseColorTSAToolButton_Click → JumpForm<DecreaseColorTSAToolForm>
        //      followed by `f.InitMethod(1)` (mode 1 = normal BG)
        //   3. ImportButton_Click → JumpFormLow<ImageBGSelectPopupForm>
        //      (only when BG256Color patch is installed — picks
        //      16-color / BG255 / BG224 mode)
        //
        // All three are now wired in ImageBGView.axaml.cs.
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

                // 2. BG → Color Reduce Tool (with InitMethod(1) for BG mode).
                new NavigationTarget(
                    CommandName: "JumpToDecreaseColor",
                    TargetViewType: typeof(DecreaseColorTSAToolView),
                    TargetAddress: null),

                // 3. BG → BG-mode-select popup (16/BG255/BG224 picker
                //    used by Import flow under BG256Color patch).
                new NavigationTarget(
                    CommandName: "JumpToBGSelectPopup",
                    TargetViewType: typeof(ImageBGSelectPopupView),
                    TargetAddress: null),
            };
        }
    }
}
