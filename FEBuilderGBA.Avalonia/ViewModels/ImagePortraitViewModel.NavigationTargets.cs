// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for ImagePortraitViewModel (#424).
//
// Split into a separate file so the `FEBuilderGBA.Avalonia.Views`
// dependency stays out of the main VM. Purely declarative metadata —
// the actual click handlers in ImagePortraitView.axaml.cs do the
// navigation; this file just records what targets exist so the Phase 4
// scanner can cross-reference them against WinForms `InputFormRef.JumpForm<T>`
// callsites in ImagePortraitForm.cs.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImagePortraitViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #424). ImagePortraitForm
        // exposes 3 cross-editor jumps in WinForms (see jumps-sweep
        // 2026-05-26 ImagePortraitForm rows):
        //
        //   1. J_8_Click (palette label hyperlink) → JumpForm<ImagePalletForm>
        //      followed by `f.JumpTo(baseBitmap, D8.Value, mode=1)`.
        //      → WindowManager.Open<ImagePalletView>() in AV. The rich
        //      `JumpTo(bitmap, palette, mode)` enrichment is deferred to a
        //      follow-up issue (target view does not yet expose that contract).
        //
        //   2. ImportButton_Click → JumpFormLow<ImagePortraitImporterForm>
        //      followed by `f.SetOrignalImage(bitmap, eyeX, eyeY, mouthX, mouthY)`.
        //      → WindowManager.Open<ImagePortraitImporterView>() in AV. The
        //      bitmap + coord propagation is similarly deferred.
        //
        //   3. X_JUMP_STATUS_HEIGHT_Click (FE8 only) → JumpForm<UnitIncreaseHeightForm>
        //      with portraitID (= AddressList.SelectedIndex), resolved via
        //      `InputFormRef.JumpTo(search_id)` in WF.
        //      → WindowManager.Open<UnitIncreaseHeightView>() in AV. The
        //      ID-based row selection is deferred until the target view
        //      exposes an ID-based selector.
        //
        // TargetAddress is `null` for all 3 to match the open-only contract
        // — the WindowManager.Open<T>() call does NOT pre-populate the
        // target view's selection.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // 1. Portrait → Palette editor (open-only; rich JumpTo deferred).
                new NavigationTarget(
                    CommandName: "JumpToPalette",
                    TargetViewType: typeof(ImagePalletView),
                    TargetAddress: null),

                // 2. Portrait → Portrait Importer (open-only; rich SetOriginalImage deferred).
                new NavigationTarget(
                    CommandName: "JumpToImporter",
                    TargetViewType: typeof(ImagePortraitImporterView),
                    TargetAddress: null),

                // 3. Portrait → Unit Height adjuster (FE8 only — open-only;
                //    portraitID-based row selection deferred).
                new NavigationTarget(
                    CommandName: "JumpToStatusHeight",
                    TargetViewType: typeof(UnitIncreaseHeightView),
                    TargetAddress: null),
            };
        }
    }
}
