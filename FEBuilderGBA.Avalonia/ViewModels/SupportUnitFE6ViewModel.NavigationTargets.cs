// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for SupportUnitFE6ViewModel (#358 / #436).
//
// Phase 4 (#374) parity seam: declare every cross-editor jump exposed by
// the FE6 Support Unit editor view so the static-analysis sweep can verify
// parity against the WinForms `InputFormRef.JumpForm<T>(...)` callsites.
//
// FE6 has its own Support Talk and Unit editors (separate from FE7/FE8).
// Pure metadata: no live ROM state is required.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SupportUnitFE6ViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // FE6 per-partner SupportTalk jump (10 partner slots, all
                // route to the same FE6 talk view).
                new NavigationTarget(
                    CommandName: "JumpToSupportTalk_FE6",
                    TargetViewType: typeof(SupportTalkFE6View),
                    TargetAddress: null),
                // FE6 source-unit back-jump (read-only display + Open Unit button).
                new NavigationTarget(
                    CommandName: "JumpToSourceUnit_FE6",
                    TargetViewType: typeof(UnitFE6View),
                    TargetAddress: null),
            };
        }
    }
}
