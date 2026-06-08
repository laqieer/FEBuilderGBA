// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for SkillConfigFE8NVer3SkillViewModel (#392).
//
// WF SkillConfigFE8NVer3SkillForm has THREE jump callsites that the
// JumpParityScanner needs to cross-reference (Copilot plan-review #2):
//   1. X_JUMP_TO_COMBAT_ART_Click -> PatchForm.JumpToSelectStruct(...)
//      Avalonia counterpart: PatchManagerView (paired via the
//      KnownExtraCrossViewMappings entry below). #1009 implemented the
//      filter-level jump: the handler now Navigates to PatchManagerView and
//      calls JumpTo("FE8N SKILL COMBAT ART", 0) (the #428 filter seam) to
//      filter + select the patch. Only the struct-level row selection
//      (PatchManagerView has no inner struct list) remains gated on #374, so
//      the IssueRef stays #374 to flag that residual gap.
//   2. ImportButton_Click -> ErrorPaletteShowForm.JumpFormLow(...)
//      Avalonia counterpart: ErrorPaletteShowView. The Import flow itself
//      is no-op until ImageUtilSkillSystemsAnimeCreator extracts to Core
//      (#500), so the dialog jump is also KnownGap.
//   3. X_N_JumpEditor_Click -> ToolAnimationCreatorForm.JumpFormLow(...)
//      Avalonia counterpart: ToolAnimationCreatorView. The Init flow is
//      still WF-only (tracked by #500).
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class SkillConfigFE8NVer3SkillViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets() => new[]
        {
            // (1) WF X_JUMP_TO_COMBAT_ART_Click -> PatchForm. Filter-level jump
            //     implemented (#1009: JumpTo("FE8N SKILL COMBAT ART", 0)); only
            //     struct-level row selection remains gated on #374.
            new NavigationTarget(
                CommandName: "JumpToCombatArt",
                TargetViewType: typeof(PatchManagerView),
                TargetAddress: null,
                IssueRef: "#374"),
            // (2) WF ImportButton_Click (palette mismatch path) -> ErrorPaletteShowForm.
            new NavigationTarget(
                CommandName: "JumpToErrorPaletteShow",
                TargetViewType: typeof(ErrorPaletteShowView),
                TargetAddress: null,
                IssueRef: "#374"),
            // (3) WF X_N_JumpEditor_Click -> ToolAnimationCreatorForm.
            new NavigationTarget(
                CommandName: "JumpToAnimationCreator",
                TargetViewType: typeof(ToolAnimationCreatorView),
                TargetAddress: null,
                IssueRef: "#500"),
        };
    }
}
