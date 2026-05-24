// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for TextViewerViewModel (#404 / #374 Phase 4).
//
// Per strict AIScript precedent (#410 / PR #571 Copilot CLI review #2), manifest
// rows correspond ONLY to working `WindowManager.Navigate<>` callsites that the
// PR ACTUALLY wires in the View code-behind. WF `TextForm` has 6 outgoing
// jumps, but every one of them depends on a Core extraction not yet completed
// in this PR:
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
//   * `TextRefAddDialogForm` — opened from `AddRefButton_Click` in the
//     WF RefPage. Depends on `UseTextIDCache.Update` Core extraction not
//     yet completed (the field is `object` in `CoreState`). The Avalonia
//     References tab in this PR shows a disabled "Add Reference" button
//     with a tooltip explaining the Core gap.
//
// Until those Core extractions land, declaring manifest entries for any of
// these jumps would create FALSE PARITY in the gap-sweep scanner: the
// scanner reports "wired" when no actual click handler exists in the view.
// Per the strict AIScript precedent, the truthful state is an EMPTY manifest.
// The 6 WF outgoing jumps remain "MissingAvManifest" rows in the jumps-sweep
// — this is the accurate signal until the relevant Core extractions land.
//
// `INavigationTargetSource` is implemented (not absent) so the gap-sweep
// scanner's reflection-based discovery records "zero wired entries" rather
// than "interface not implemented at all". Both states are semantically
// identical for the scanner but the explicit declaration is documentation.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class TextViewerViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            // Empty manifest: zero outgoing WindowManager.Navigate<T> callsites
            // wired in TextViewerView.axaml.cs as of this PR. See the file-level
            // comment for the per-jump scope rationale.
            return Array.Empty<NavigationTarget>();
        }
    }
}
