// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for DumpStructSelectDialogViewModel (#439).
//
// Split into a separate file because INavigationTargetSource is part of the
// FEBuilderGBA.Avalonia.Services assembly while the View target types live
// in FEBuilderGBA.Avalonia.Views. Keeping the manifest in a partial lets the
// main VM file stay free of Views-namespace coupling.
//
// The WinForms `DumpStructSelectDialogForm` exposes 5 JumpForm callsites
// (see docs/avalonia-gaps/2026-05-22-jumps-sweep.md lines 222-226):
//   1. line 1165 — InputFormRef.JumpFormLow<PointerToolCopyToForm>
//   2. line 1176 — InputFormRef.JumpFormLow<DumpStructSelectDialogForm> (self)
//   3. line 1184 — InputFormRef.JumpForm<HexEditorForm>
//   4. line 1254 — InputFormRef.JumpFormLow<DumpStructSelectToTextDialogForm>
//   5. line 1263 — InputFormRef.JumpFormLow<DumpStructSelectDialogForm> (self)
//
// Five WF rows map to four DISTINCT manifest entries because both self-jumps
// resolve to the same DumpStructSelectDialogView target. The JumpParityScanner
// matches both WF self-rows against the single manifest entry, so all 5
// sweep rows classify as `Match`.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class DumpStructSelectDialogViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // WF lines 1176 + 1263 — the dispatcher re-opens itself when
                // ShowDumpSelectDialog routes from another caller. AV mirrors
                // this with WindowManager.Open<DumpStructSelectDialogView>().
                new NavigationTarget(
                    CommandName: "JumpToSelf",
                    TargetViewType: typeof(DumpStructSelectDialogView),
                    TargetAddress: null),

                // WF line 1254 — open the text-display dialog with the
                // generated export output (CSV/TSV/EA/STRUCT/NMM).
                new NavigationTarget(
                    CommandName: "ShowExportText",
                    TargetViewType: typeof(DumpStructSelectToTextDialogView),
                    TargetAddress: null),

                // WF line 1184 — Func_Binary opens the Hex Editor at the
                // dumped address.
                new NavigationTarget(
                    CommandName: "JumpToHexEditor",
                    TargetViewType: typeof(HexEditorView),
                    TargetAddress: null),

                // WF line 1165 — for RAM-region addresses (U.is_RAMPointer)
                // ShowDumpSelectDialog opens the pointer-tool dialog instead.
                new NavigationTarget(
                    CommandName: "JumpToPointerTool",
                    TargetViewType: typeof(PointerToolCopyToView),
                    TargetAddress: null),
            };
        }
    }
}
