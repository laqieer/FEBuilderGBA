// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for TextViewerViewModel (#404 / #374 Phase 4;
// #1028 Slice A + Slice D; #1108).
//
// Per strict AIScript precedent (#410 / PR #571 Copilot CLI review #2), manifest
// rows correspond ONLY to working navigation callsites that the PR ACTUALLY wires
// in the View code-behind. WF `TextForm` has 6 outgoing jumps:
//   * `TextRefAddDialog`  — wired by #1028 Slice A (References-tab Add Reference).
//   * `TextBadCharPopup`  — wired by #1028 Slice D (encode-error bad-char popup).
//   * `TextScriptCategorySelectForm` — NOW WIRED by #1108 (Insert Escape Code).
//   * `ImagePortraitForm` / `ImagePortraitFE6Form` — NOW WIRED by #1108 (Jump to
//     Portrait), one row per ROM-version target.
//
// #1108 — the two remaining rich-text outgoing jumps are now wired WITHOUT a
// full `EventScript.DisAssemble` port. The previous deferral assumed the bracket
// dispatch needed per-arg ArgType disassembly; instead:
//
//   * Insert Escape Code: WF `TextForm.SelectEscapeText` opened
//     `TextScriptCategorySelectForm` from the rich-text context menu. The Avalonia
//     UI adapts this to a dedicated **Edit-tab "Insert Escape Code" button** that
//     opens `TextScriptCategorySelectView` modally; the dialog now loads the REAL
//     shipped text-escape + text-category tables via the scoped Core helper
//     `TextRichControlDecode.LoadEscapeEntries` / `LoadEscapeCategories` (which
//     read the same `config/data/text_escape_*` / `text_category_*` files WF
//     uses). OK returns the chosen `@XXXX` Code, inserted at the EditTextBox caret.
//
//   * Jump to Portrait: WF `TextForm.TextListSpShowCharLabel_Click` opened
//     `ImagePortraitForm` / `ImagePortraitFE6Form` from the easy-mode TextListSp
//     panel. The Avalonia UI adapts this to a dedicated **Edit-tab "Jump to
//     Portrait" button**. The portrait face id is decoded from the current edit
//     text via `TextRichControlDecode.FindFirstPortraitFaceId` — a minimal decode
//     built on the existing `ConversationScriptParser` (a faithful WF parser port)
//     plus Core `TextEscape`, so NO `EventScript.DisAssemble` port was needed. The
//     button jumps to the portrait editor at `portrait_pointer + faceId *
//     portrait_datasize` (the UnitEditorView.JumpToPortrait_Click address pattern).
//     The `0xFFFF` visitor sentinel (and "no portrait code") show a status note
//     instead of navigating.
//
// #1028 Slice A closed the `TextRefAddDialog` jump: `OnAddReferenceClick` opens
// `TextRefAddDialogView` modally and persists the result via the `ITextIDCache`
// Core seam. Modal-dialog jump (`ShowDialog`, not `WindowManager.Navigate`), so
// it follows the EventUnitViewModel modal-jump precedent: TargetAddress: null.
//
// #1028 Slice D closed the `TextBadCharPopup` jump from the encode-error path
// (modal `ShowDialog`, TargetAddress: null).
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

                // Edit-tab "Insert Escape Code" (#1108). Opened modally via
                // TextViewerView.OnInsertEscapeCodeClick -> ShowDialog. No static
                // target (a category/escape picker, not an addressed editor), so
                // TargetAddress is the null sentinel.
                new NavigationTarget(
                    CommandName: "OnInsertEscapeCode",
                    TargetViewType: typeof(TextScriptCategorySelectView),
                    TargetAddress: null),

                // Edit-tab "Jump to Portrait" (#1108) — non-FE6 ROMs. Opened via
                // WindowManager.Navigate<ImagePortraitView>(addr) where addr is
                // computed at click time from the decoded face id, so TargetAddress
                // is the dynamic-address sentinel 0u (the target view exists; the
                // exact address is a runtime concern).
                new NavigationTarget(
                    CommandName: "OnJumpToPortrait",
                    TargetViewType: typeof(ImagePortraitView),
                    TargetAddress: 0u),

                // Edit-tab "Jump to Portrait" (#1108) — FE6 variant. Same dynamic
                // address; routes to ImagePortraitFE6View when RomInfo.version == 6.
                new NavigationTarget(
                    CommandName: "OnJumpToPortrait",
                    TargetViewType: typeof(ImagePortraitFE6View),
                    TargetAddress: 0u),
            };
        }
    }
}
