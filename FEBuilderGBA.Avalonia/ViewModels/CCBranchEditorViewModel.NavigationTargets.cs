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
        // to ClassForm via InputFormRef). Issue #365 reported a related bug:
        // the Upstream Chain panel computation was wrong AND the promotion
        // fields lacked jump buttons. Both are now fixed (Fixed in #365 /
        // PR #460): Promo1/2_Jump → ClassEditorView and BuildUpstreamChain
        // computes the before-promotion list, so the IssueRef tags are dropped.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Fixed in #365 (PR #460): Upstream chain display now correct
                // and navigation to the promotion class is wired. Target view
                // is ClassEditorView (FE8 — CCBranch is FE8-only).
                new NavigationTarget(
                    CommandName: "JumpToPromotionClass1",
                    TargetViewType: typeof(ClassEditorView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToPromotionClass2",
                    TargetViewType: typeof(ClassEditorView),
                    TargetAddress: null),
            };
        }
    }
}
