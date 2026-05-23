// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for WorldMapEventPointerViewModel. (#432)
//
// Split into a separate file to keep the `FEBuilderGBA.Avalonia.Views`
// dependency out of the main VM (which is exercised by Core tests via
// project reference). Purely declarative metadata — the actual click
// handlers in WorldMapEventPointerView.axaml.cs do the navigation;
// this file just records what targets exist so the Phase 4 scanner can
// cross-reference them against WinForms `InputFormRef.JumpForm<T>` callsites.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class WorldMapEventPointerViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #432).
        // WorldMapEventPointerForm exposes FIVE JumpForm<T> callsites in
        // WinForms (1 entry per real click handler):
        //
        //   1. JUMP_OPNING_EVENT_Click    -> JumpForm<EventScriptForm>
        //   2. JUMP_ENDING1_EVENT_Click   -> JumpForm<EventScriptForm>
        //   3. JUMP_ENDING2_EVENT_Click   -> JumpForm<EventScriptForm>
        //   4. X_JUMP_ROAD_Click          -> JumpForm<WorldMapPathForm>
        //   5. X_JUMP_WORLDMAP_POINT_Click -> JumpForm<WorldMapPointForm>
        //
        // All five are wired in WorldMapEventPointerView.axaml.cs:
        //   1. JumpToOpening_Click       -> WindowManager.Navigate<EventScriptView>
        //   2. JumpToEnding1_Click       -> WindowManager.Navigate<EventScriptView>
        //   3. JumpToEnding2_Click       -> WindowManager.Navigate<EventScriptView>
        //   4. JumpToWorldMapPath_Click  -> WindowManager.Open<WorldMapPathView>
        //   5. JumpToWorldMapPoint_Click -> WindowManager.Open<WorldMapPointView>
        //
        // Scanner artifact note: the 2026-05-24 jumps-sweep emits SIX rows
        // for this form, not five. The sixth row pairs `WorldMapPathForm`
        // with `WorldMapPathEditorView` (a Layer 2 PairMatcher discovery
        // beyond the authoritative Layer 1 ListParityHelper seed). The WF
        // form has only ONE `JumpForm<WorldMapPathForm>()` callsite — adding
        // a second AV manifest entry pointing at WorldMapPathEditorView
        // would synthesize parity beyond what WinForms exposes (a stub
        // view, not the WF-port). Per Copilot CLI plan review point 2
        // (issue #432), the manifest declares only the five real entries;
        // the sixth scanner row remains as MissingAvManifest and is
        // documented here as a known scanner artifact rather than a gap.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // 1. Opening cinematic event -> EventScript editor.
                new NavigationTarget(
                    CommandName: "JumpToOpeningEvent",
                    TargetViewType: typeof(EventScriptView),
                    TargetAddress: null),

                // 2. Eirika ending event -> EventScript editor.
                new NavigationTarget(
                    CommandName: "JumpToEnding1Event",
                    TargetViewType: typeof(EventScriptView),
                    TargetAddress: null),

                // 3. Ephraim ending event -> EventScript editor.
                new NavigationTarget(
                    CommandName: "JumpToEnding2Event",
                    TargetViewType: typeof(EventScriptView),
                    TargetAddress: null),

                // 4. WorldMap path editor (X_JUMP_ROAD).
                new NavigationTarget(
                    CommandName: "JumpToWorldMapPath",
                    TargetViewType: typeof(WorldMapPathView),
                    TargetAddress: null),

                // 5. WorldMap point editor (X_JUMP_WORLDMAP_POINT).
                new NavigationTarget(
                    CommandName: "JumpToWorldMapPoint",
                    TargetViewType: typeof(WorldMapPointView),
                    TargetAddress: null),
            };
        }
    }
}
