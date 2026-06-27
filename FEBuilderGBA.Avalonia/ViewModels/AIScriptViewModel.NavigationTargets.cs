// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for AIScriptViewModel (#410 / #374 Phase 4 / #1600).
//
// Mirrors the wired WF `AIScriptForm` cross-editor jump callsites that the
// Avalonia view actually triggers.
//
// #1600 wired the per-parameter `POINTER_AI*` jump dispatch (WF
// `AIScriptForm.ParamLabel_Clicked`): clicking an AI-pointer parameter row in
// the AIScript detail panel opens the matching AI sub-editor seeded at the
// opcode-arg pointer (allocating a 4-byte ASM block on null/broken for the 3
// ASM types), via `WindowManager.Navigate<TView>(addr)`. The five sub-editors
// are now reachable for real ROM data, so they appear in the manifest. The
// remaining WF Unit/Class/DisASM param jumps still originate from the same
// per-arg dispatch and are tracked separately (their ArgTypes are not the AI
// pointer types this slice covers).
//
// Wired callsites (match AIScriptView.axaml.cs):
//   - JumpToPointerToolCopyTo -> PointerToolCopyToView (DetailAddress_Click)
//   - JumpToAIUnits           -> AIUnitsView           (ParamLabel_Click, POINTER_AIUNIT)
//   - JumpToAITiles           -> AITilesView           (ParamLabel_Click, POINTER_AITILE)
//   - JumpToAIASMCoordinate   -> AIASMCoordinateView   (ParamLabel_Click, POINTER_AICOORDINATE)
//   - JumpToAIASMRange        -> AIASMRangeView        (ParamLabel_Click, POINTER_AIRANGE)
//   - JumpToAIASMCALLTALK     -> AIASMCALLTALKView     (ParamLabel_Click, POINTER_AICALLTALK)
//
// `TargetAddress: null` is the sentinel for "manifest declares the jump but the
// runtime address is determined at click time" (the param's resolved/allocated
// pointer) — matches the precedent used by SongTrackViewModel.NavigationTargets
// and EventUnitViewModel.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class AIScriptViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // ---------------- Address-double-click pointer copy ----------------
                new NavigationTarget(
                    CommandName: "JumpToPointerToolCopyTo",
                    TargetViewType: typeof(PointerToolCopyToView),
                    TargetAddress: null),

                // ---------------- POINTER_AI* parameter jumps (#1600) ----------------
                new NavigationTarget(
                    CommandName: "JumpToAIUnits",
                    TargetViewType: typeof(AIUnitsView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToAITiles",
                    TargetViewType: typeof(AITilesView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToAIASMCoordinate",
                    TargetViewType: typeof(AIASMCoordinateView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToAIASMRange",
                    TargetViewType: typeof(AIASMRangeView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToAIASMCALLTALK",
                    TargetViewType: typeof(AIASMCALLTALKView),
                    TargetAddress: null),
            };
        }
    }
}
