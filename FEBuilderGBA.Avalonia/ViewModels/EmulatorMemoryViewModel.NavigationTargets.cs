// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for EmulatorMemoryViewModel (#385 / #374 Phase 4).
//
// Mirrors the 12 cross-editor jump callsites the gap-sweep
// 2026-05-27-jumps-sweep.md enumerated on the WF EmulatorMemoryForm:
//
//   1. EventScriptForm (3 callsites: J_CurrentEventAddress,
//      EventHistoryListBox jump, RunningEventListBox jump)
//   2. ProcsScriptForm (PROCS_JUMP_CURSOL_CODE)
//   3. HexEditorForm
//   4. MapChangeForm
//   5. RAMRewriteToolMAPForm (2 callsites)
//   6. RAMRewriteToolForm
//   7. SongTableForm
//   8. TextForm (=TextViewerView)
//   9. ToolBGMMuteDialogForm
//
// Of those 12, 9 are wired in EmulatorMemoryView.axaml.cs as functional
// Open<TView>() handlers (they open the target editor without a
// pre-selected address - the SongTable/BGM/Hex/Text/EventScript/Procs/
// MapChange/RAMRewriteTool/RAMRewriteToolMAP all open at their default
// entry point). The remaining 3 require a live RAM address (the
// current event begin / running line / procs cursor) that this VM
// cannot compute without P/Invoke, so they stay KnownGap with #385.
//
// When a cross-platform RAM reader lands (likely tracked separately by
// #374 as a fundamental Avalonia architectural gap), the 3 KnownGap
// rows here will flip to functional and IssueRef will be removed.

using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class EmulatorMemoryViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // ---------------- Functional Open<T> jumps ----------------
                // Each corresponds to a Click="OpenXxx_Click" handler in
                // EmulatorMemoryView.axaml.cs that opens the target editor
                // parameterlessly via WindowManager.Open<TView>().
                new NavigationTarget(
                    CommandName: "OpenEventScript",
                    TargetViewType: typeof(EventScriptView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenProcsScript",
                    TargetViewType: typeof(ProcsScriptView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenHexEditor",
                    TargetViewType: typeof(HexEditorView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenTextViewer",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenSongTable",
                    TargetViewType: typeof(SongTableView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenToolBGMMuteDialog",
                    TargetViewType: typeof(ToolBGMMuteDialogView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenMapChange",
                    TargetViewType: typeof(MapChangeView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenRAMRewriteTool",
                    TargetViewType: typeof(RAMRewriteToolView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "OpenRAMRewriteToolMAP",
                    TargetViewType: typeof(RAMRewriteToolMAPView),
                    TargetAddress: null),

                // ---------------- KnownGap jumps (live-RAM-dependent) ----------------
                // These three WF callsites compute their target address from
                // live emulator RAM (the current event begin pointer, the
                // running event line pointer, the procs script cursor). The
                // Avalonia cross-platform build has no P/Invoke RAM reader,
                // so the address is unobtainable - the manifest entry is
                // tagged with IssueRef="#385" so the JumpParityScanner
                // reports it as KnownGap rather than MissingAvManifest, and
                // the Phase 4 test renders it as Skip until a cross-platform
                // RAM reader lands.
                new NavigationTarget(
                    CommandName: "JumpToCurrentEvent",
                    TargetViewType: typeof(EventScriptView),
                    TargetAddress: 0u,
                    IssueRef: "#385"),
                new NavigationTarget(
                    CommandName: "JumpToRunningEventLine",
                    TargetViewType: typeof(EventScriptView),
                    TargetAddress: 0u,
                    IssueRef: "#385"),
                new NavigationTarget(
                    CommandName: "JumpToProcsCursor",
                    TargetViewType: typeof(ProcsScriptView),
                    TargetAddress: 0u,
                    IssueRef: "#385"),
            };
        }
    }
}
