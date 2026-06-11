// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for TextViewerViewModel (#404 / #374 Phase 4; #1028 Slice A).
//
// Per strict AIScript precedent (#410 / PR #571 Copilot CLI review #2), manifest
// rows correspond ONLY to working navigation callsites that the PR ACTUALLY wires
// in the View code-behind. WF `TextForm` has 6 outgoing jumps; #1028 Slice A wires
// the FIRST of them (the References-tab "Add Reference" modal dialog). The other 5
// remain blocked on Core extractions not yet completed:
//
//   * `TextScriptCategorySelectForm` — opened from the rich-text bracket
//     dispatch context menu (`SelectEscapeText` in WF TextForm). Depends on
//     `EventScript.DisAssemble` per-arg `ArgType` dispatch which is still
//     WinForms-coupled today (same scope rationale as AIScript #410 deferred
//     Unit/Class jumps).
//
//   * `ImagePortraitForm` / `ImagePortraitFE6Form` — opened from the WF
//     TextListSp easy-mode panel's `TextListSpShowCharLabel_Click` handler.
//     The easy-mode panel itself isn't ported to Avalonia (the entire
//     TextListSp* sub-tab structure depends on the same rich-text dispatch
//     pipeline noted above).
//
//   * `TextBadCharPopupForm` — opened from `NeedAntiHuffman` in the encode
//     error path. Triggering it requires `PatchUtil.SearchAntiHuffmanPatch`
//     (not yet ported to Core). Avalonia's `WriteText` currently falls back
//     to `UnHuffmanEncode` silently without the patch check — a behavioral
//     gap tracked separately. The popup view itself exists in Avalonia
//     (`TextBadCharPopupView`) but the trigger logic doesn't.
//
// #1028 Slice A closes the `TextRefAddDialog` jump: `OnAddReferenceClick` opens
// `TextRefAddDialogView` modally (pre-filled with the selected text id + its
// existing reference comment) and persists the result via the `ITextIDCache`
// Core seam (Update + Save). This is a modal-dialog jump (opened via
// `ShowDialog`, not `WindowManager.Navigate`), so it follows the
// EventUnitViewModel `JumpToNewAlloc`/`JumpToItemDrop` precedent: a manifest
// entry with `TargetAddress: null` (the text id is resolved at click time).
//
// Until the remaining Core extractions land, declaring manifest entries for
// those 5 jumps would create FALSE PARITY in the gap-sweep scanner. Per the
// strict AIScript precedent, only the truthfully-wired jump is declared here.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class TextViewerViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // References-tab "Add Reference" (#1028 Slice A). Opened modally
                // via TextViewerView.OnAddReferenceClick -> ShowDialog. The text id
                // is the currently-selected text, resolved at click time, so
                // TargetAddress is the null "no static target" sentinel (same as
                // the EventUnit New-Alloc / Item-Drop modal-dialog entries).
                new NavigationTarget(
                    CommandName: "OnAddReference",
                    TargetViewType: typeof(TextRefAddDialogView),
                    TargetAddress: null),
            };
        }
    }
}
