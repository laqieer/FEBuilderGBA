// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for SupportUnitEditorViewModel (#358 / #437).
//
// Phase 4 (#374) parity seam: declare every cross-editor jump exposed by
// this ViewModel's paired View so the static-analysis sweep can verify
// parity against WinForms' `InputFormRef.JumpForm<T>(...)` callsites.
//
// Pure metadata: no live ROM state is required to enumerate these entries.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SupportUnitEditorViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // Per-partner SupportTalk jumps.  FE6 routes to SupportTalkFE6View,
                // FE7 to SupportTalkFE7View, FE8 to SupportTalkView.  All three
                // are valid runtime targets; the actual choice happens in the
                // View click handler (per ROM version).  We list all three so
                // the static-analysis sweep treats all three WinForms callsites
                // (SupportTalkForm / SupportTalkFE6Form / SupportTalkFE7Form)
                // as covered.
                new NavigationTarget(
                    CommandName: "JumpToSupportTalk_FE6",
                    TargetViewType: typeof(SupportTalkFE6View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToSupportTalk_FE7",
                    TargetViewType: typeof(SupportTalkFE7View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToSupportTalk_FE8",
                    TargetViewType: typeof(SupportTalkView),
                    TargetAddress: null),
                // Source-unit back-jump (read-only display + Open Unit button).
                new NavigationTarget(
                    CommandName: "JumpToSourceUnit_FE7",
                    TargetViewType: typeof(UnitFE7View),
                    TargetAddress: null),
                new NavigationTarget(
                    CommandName: "JumpToSourceUnit_FE8",
                    TargetViewType: typeof(UnitEditorView),
                    TargetAddress: null),
            };
        }
    }
}
