// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for TextViewerViewModel (#404 / #374 Phase 4;
// #1028 Slice A + Slice D).
//
// Per strict AIScript precedent (#410 / PR #571 Copilot CLI review #2), manifest
// rows correspond ONLY to working navigation callsites that the PR ACTUALLY wires
// in the View code-behind. WF `TextForm` has 6 outgoing jumps; Slice A wired the
// References-tab "Add Reference" modal dialog and Slice D now wires the
// `TextBadCharPopup` jump. The remaining 3 stay blocked on Core extractions:
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
// #1028 Slice A closed the `TextRefAddDialog` jump: `OnAddReferenceClick` opens
// `TextRefAddDialogView` modally (pre-filled with the selected text id + its
// existing reference comment) and persists the result via the `ITextIDCache`
// Core seam (Update + Save). This is a modal-dialog jump (opened via
// `ShowDialog`, not `WindowManager.Navigate`), so it follows the
// EventUnitViewModel `JumpToNewAlloc`/`JumpToItemDrop` precedent: a manifest
// entry with `TargetAddress: null` (the text id is resolved at click time).
//
// #1028 Slice D closes the `TextBadCharPopup` jump: WF `TextForm.NeedAntiHuffman`
// opens `TextBadCharPopupForm` from the encode-error path. The Core
// `PatchDetection.SearchAntiHuffmanPatch` port now exists, so
// `TextViewerView.OnWriteTextClick` actually shows `TextBadCharPopupView` modally
// when Huffman encode fails AND the AntiHuffman patch is missing (ja/zh/ko, per
// WF). This is a real, wired modal-dialog jump, so its manifest entry is declared
// below (TargetAddress: null — opened via ShowDialog, not WindowManager.Navigate).
//
// Until the remaining Core extractions land, declaring manifest entries for
// those 3 jumps would create FALSE PARITY in the gap-sweep scanner. Per the
// strict AIScript precedent, only the truthfully-wired jumps are declared here.
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

                // Bad-character popup (#1028 Slice D). Opened modally via
                // TextViewerView.OnWriteTextClick -> ShowBadCharPopupAsync ->
                // ShowDialog when Huffman encode fails and the AntiHuffman patch
                // is missing (ja/zh/ko). The error text is resolved at write time,
                // so TargetAddress is the null "no static target" sentinel.
                new NavigationTarget(
                    CommandName: "OnWriteText",
                    TargetViewType: typeof(TextBadCharPopupView),
                    TargetAddress: null),
            };
        }
    }
}
