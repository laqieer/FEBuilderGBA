// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for SupportTalkViewModel. (#374)
//
// Split into a separate file to isolate the `FEBuilderGBA.Avalonia.Views`
// dependency from the main VM. Purely declarative metadata.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SupportTalkViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374). Mirror navigation callsites
        // in SupportTalkView.axaml.cs and flag the missing unit-id jumps from
        // issue #360 ("Add jump, pick, and preview for all id/address fields").
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Working: text-id jumps for the three support tiers (C/B/A).
                new NavigationTarget(
                    CommandName: "JumpToTextC",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToTextB",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToTextA",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),

                // Known-broken (#360): Support Partner 1 / Partner 2 unit-id
                // fields have no jump-to-unit button (the WinForms
                // SupportTalkForm wires both to UnitForm via InputFormRef).
                new NavigationTarget(
                    CommandName: "JumpToPartner1",
                    TargetViewType: typeof(UnitEditorView),
                    TargetAddress: null,
                    IssueRef: "#360"),
                new NavigationTarget(
                    CommandName: "JumpToPartner2",
                    TargetViewType: typeof(UnitEditorView),
                    TargetAddress: null,
                    IssueRef: "#360"),
            };
        }
    }
}
