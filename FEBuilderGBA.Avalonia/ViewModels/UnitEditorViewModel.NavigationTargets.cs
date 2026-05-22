// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for UnitEditorViewModel. (#374)
//
// Split into a separate file to isolate the `FEBuilderGBA.Avalonia.Views`
// dependency from the main VM. Purely declarative metadata.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class UnitEditorViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374). Mirror the navigation
        // callsites in UnitEditorView.axaml.cs without changing behavior.
        // All entries here are STABLE (no IssueRef) — they're recorded so the
        // Phase 4 scanner has a baseline of working jumps to compare the
        // WinForms callsite inventory against.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Text-id jumps (Name / Desc).
                new NavigationTarget(
                    CommandName: "JumpToNameText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToDescText",
                    TargetViewType: typeof(TextViewerView),
                    TargetAddress: null),

                // Class jump — patch-conditional: FE6 routes to ClassFE6View,
                // FE7/FE8 routes to ClassEditorView.
                new NavigationTarget(
                    CommandName: "JumpToClassFE6",
                    TargetViewType: typeof(ClassFE6View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToClass",
                    TargetViewType: typeof(ClassEditorView),
                    TargetAddress: null),

                // Portrait jump.
                new NavigationTarget(
                    CommandName: "JumpToPortrait",
                    TargetViewType: typeof(PortraitViewerView),
                    TargetAddress: null),
                // #358: Support Unit jump (mirrors WinForms J_44_SUPPORTUNIT).
                // FE6 routes via UnitFE6ViewModel; this entry covers FE7/FE8.
                new NavigationTarget(
                    CommandName: "JumpToSupportUnit",
                    TargetViewType: typeof(SupportUnitEditorView),
                    TargetAddress: null),
            };
        }
    }
}
