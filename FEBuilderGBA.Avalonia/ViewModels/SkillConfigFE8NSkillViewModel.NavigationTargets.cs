// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for SkillConfigFE8NSkillViewModel (#390).
//
// WF SkillConfigFE8NSkillForm's X_N_JumpEditor_Click opens a
// ToolAnimationCreatorForm populated with the currently selected skill's
// animation. The Avalonia ToolAnimationCreatorView is still a UI shell with
// empty handlers (tracked by #500), so we declare the jump here as a
// KnownGap until that lands. Mirrors the pattern used by
// SkillConfigFE8NVer2SkillViewModel (#396) and SkillConfigSkillSystemViewModel (#427).
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SkillConfigFE8NSkillViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets() => new[]
        {
            new NavigationTarget(
                CommandName: "JumpToAnimationCreator",
                TargetViewType: typeof(ToolAnimationCreatorView),
                TargetAddress: null,
                // Tracked by #500 — ToolAnimationCreatorView.Init is still
                // not implemented in Avalonia, so the jump button is wired
                // but no-ops with a tooltip until that lands.
                IssueRef: "#500"),
        };
    }
}
