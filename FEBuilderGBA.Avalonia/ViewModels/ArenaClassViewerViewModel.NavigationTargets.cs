// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ArenaClassViewerViewModel. (#374)
//
// Split into a separate file to isolate the `FEBuilderGBA.Avalonia.Views`
// dependency from the main VM. Purely declarative metadata.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ArenaClassViewerViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374). Stable jump: the class-id
        // text-block in ArenaClassViewerView is wired to navigate to the
        // class editor (FE6 → ClassFE6View, FE7/FE8 → ClassEditorView).
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToClassFE6",
                    TargetViewType: typeof(ClassFE6View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToClass",
                    TargetViewType: typeof(ClassEditorView),
                    TargetAddress: null),
            };
        }
    }
}
