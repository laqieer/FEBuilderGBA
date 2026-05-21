// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for CCBranchEditorViewModel. (#374)
//
// Split into a separate file to isolate the `FEBuilderGBA.Avalonia.Views`
// dependency from the main VM. Purely declarative metadata.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class CCBranchEditorViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374). The CC Branch editor's
        // Promotion Class fields are class-id references whose natural jump
        // target is the Class editor (the WinForms CCBranchForm wires both
        // to ClassForm via InputFormRef). Issue #365 reports a related bug:
        // the Upstream Chain panel computation is wrong. Although #365 is
        // a DATA-DISPLAY bug (not a navigation bug), it's still surfaced as
        // a known-gap here so the meta tracking can route through Phase 4's
        // skipped tests.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Known-broken (#365): Upstream chain display is wrong;
                // navigation to source class is also missing. Target view
                // is ClassEditorView (FE8 — CCBranch is FE8-only).
                new NavigationTarget(
                    CommandName: "JumpToPromotionClass1",
                    TargetViewType: typeof(ClassEditorView),
                    TargetAddress: null,
                    IssueRef: "#365"),
                new NavigationTarget(
                    CommandName: "JumpToPromotionClass2",
                    TargetViewType: typeof(ClassEditorView),
                    TargetAddress: null,
                    IssueRef: "#365"),
            };
        }
    }
}
