// SPDX-License-Identifier: GPL-3.0-or-later
// Navigation manifest for ImageMagicFEditorViewModel (#418 / #374 Phase 4).
//
// Mirrors the single WF cross-editor jump callsite in
// `ImageMagicFEditorForm`:
//   - `X_N_JumpEditor_Click` -> `ToolAnimationCreatorForm`
//     (`InputFormRef.JumpFormLow<ToolAnimationCreatorForm>()` then
//      `f.Init(MagicAnime_FEEDitor, ID, filehint, filename)`)
//
// The WF flow exports the current magic anime to a temp `.txt` file
// and hands the path to ToolAnimationCreator. The Avalonia
// ToolAnimationCreatorView exists but its `Init()` / Magic Anime
// support is tracked separately by issue #500. The KnownGap row
// below points at #500 (open) so the jump-parity scanner reports
// `KnownGap` rather than `MissingAvManifest`.
//
// The other deferred actions on this view (Open Source / Select
// Source / Magic Anime Import / Magic Anime Export) are NOT
// cross-editor jumps — they are file/shell I/O. They are tracked
// via the disabled-button + tooltip pattern in the AXAML, not via
// NavigationTarget manifest rows (the JumpParityScanner contract
// requires concrete `TargetViewType` references, which file-shell
// actions do not have).
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageMagicFEditorViewModel : INavigationTargetSource
    {
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                new NavigationTarget(
                    CommandName: "JumpToToolAnimationCreator",
                    TargetViewType: typeof(ToolAnimationCreatorView),
                    TargetAddress: null,
                    IssueRef: "#500"),
            };
        }
    }
}
