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
        // in SupportTalkView.axaml.cs. The unit-id jumps from issue #360
        // ("Add jump, pick, and preview for all id/address fields") are now
        // wired (Fixed in #360 / PR #638), so the IssueRef tags are dropped.
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

                // Fixed in #360 (PR #638): Support Partner 1 / Partner 2 unit-id
                // fields now have a jump-to-unit button (the WinForms
                // SupportTalkForm wires both to UnitForm via InputFormRef;
                // the Avalonia view wires SupportPartner1/2_Jump → UnitEditorView).
                new NavigationTarget(
                    CommandName: "JumpToPartner1",
                    TargetViewType: typeof(UnitEditorView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToPartner2",
                    TargetViewType: typeof(UnitEditorView),
                    TargetAddress: null),
            };
        }
    }
}
