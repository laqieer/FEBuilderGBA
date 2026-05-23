// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for AIScriptViewModel (#410 / #374 Phase 4).
//
// Mirrors the wired WF `AIScriptForm` cross-editor jump callsites that the
// Avalonia view actually triggers after PR #410. Per Copilot CLI plan-review
// v2 #1 AND PR #571 Copilot CLI review #2 (strict reading of
// `INavigationTargetSource`), manifest rows correspond ONLY to working
// `WindowManager.Navigate<>` callsites that this PR ACTUALLY wires in the
// view code-behind.
//
// The Unit / Class / DisASM jumps from WF AIScriptForm originate from the
// per-parameter `ParamLabel_Click` handler (which dispatches on the
// runtime ArgType of the selected opcode). That dispatch path requires the
// live opcode disassembly + per-arg ArgType resolution, which is
// WinForms-coupled today via EventScript.DisAssemble. Until that Core
// extraction lands, none of those Unit/Class/DisASM jumps actually fire in
// Avalonia — so this manifest deliberately omits them. The
// JumpParityScanner correctly reports them as `MissingAvManifest`, which
// is the truthful state.
//
// Same rationale applies to the heavy AI sub-editors (AIUnits, AITiles,
// AIASMCoordinate, AIASMRange, AIASMCALLTALK, AIScriptCategorySelect) —
// they are reachable in WF only via the same param-label dispatch.
//
// This matches the SongTrack PR #412 precedent (3 import-dispatch flows
// deliberately absent from the manifest until their underlying Core
// extraction lands) and is documented in `INavigationTargetSource.cs` as
// the contract for manifest entries.
//
// Wired callsite (matches AIScriptView.axaml.cs):
//   - JumpToPointerToolCopyTo -> PointerToolCopyToView (DetailAddress_Click)
//
// `TargetAddress: null` is the sentinel for "manifest declares the jump but
// the runtime address is determined at click time" — matches the precedent
// used by SongTrackViewModel.NavigationTargets.cs and EventUnitViewModel.
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
                // The ONLY wired Navigate<> callsite in AIScriptView.axaml.cs.
                new NavigationTarget(
                    CommandName: "JumpToPointerToolCopyTo",
                    TargetViewType: typeof(PointerToolCopyToView),
                    TargetAddress: null),
            };
        }
    }
}
