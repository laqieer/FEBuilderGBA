// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for UnitFE6ViewModel (#358 / #374 Phase 4).
//
// The FE6 unit editor jumps to the FE6 support-unit editor (J_44_SUPPORTUNIT
// equivalent).  Pure declarative metadata — no live ROM state required.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class UnitFE6ViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToSupportUnit",
                    TargetViewType: typeof(SupportUnitFE6View),
                    TargetAddress: null),
            };
        }
    }
}
