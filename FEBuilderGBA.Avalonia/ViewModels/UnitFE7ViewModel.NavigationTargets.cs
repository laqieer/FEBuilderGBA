// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for UnitFE7ViewModel (#358 / #374 Phase 4).
//
// The FE7 unit editor jumps to the shared FE7/FE8 support-unit editor
// (J_44_SUPPORTUNIT equivalent).  Pure declarative metadata.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class UnitFE7ViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToSupportUnit",
                    TargetViewType: typeof(SupportUnitEditorView),
                    TargetAddress: null),
            };
        }
    }
}
