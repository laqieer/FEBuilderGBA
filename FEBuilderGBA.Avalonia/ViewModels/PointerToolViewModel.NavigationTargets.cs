// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 4 navigation manifest for PointerToolViewModel. (#438)
//
// Split into a separate file so the FEBuilderGBA.Avalonia.Views dependency
// stays out of the main VM (which is exercised by Core tests via project
// reference). Purely declarative metadata — the actual click handlers in
// PointerToolView.axaml.cs do the navigation; this file just records what
// targets exist so the Phase 4 scanner can cross-reference them against
// WinForms `InputFormRef.JumpForm<T>` callsites.
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class PointerToolViewModel : INavigationTargetSource
    {
        // ----------------------------------------------------------------
        // INavigationTargetSource — Phase 4 (#374, #438). PointerToolForm
        // exposes three JumpForm<T> callsites in WinForms:
        //
        //   1. BatchButton_Click           → JumpFormLow<PointerToolBatchInputForm>
        //   2. OtherROMAddress_MouseDoubleClick → JumpFormLow<PointerToolCopyToForm>
        //   3. ComandLineSearch (static CLI helper) → JumpFormLow<PointerToolForm>
        //
        // All three are now wired in PointerToolView.axaml.cs:
        //   1. Batch_Click           → WindowManager.Instance.Open<PointerToolBatchInputView>
        //   2. AddressDoubleClick    → WindowManager.Instance.Navigate<PointerToolCopyToView>
        //   3. (CLI surface only — the FEBuilderGBA.CLI --pointer-search subcommand
        //      delegates to PointerCalcCore.SearchAddresses; the manifest entry
        //      below records the WF↔AV symmetry so the scanner classifies it
        //      as Match rather than MissingAvManifest.)
        //
        // No IssueRef is set on any row — every entry has a working
        // counterpart in the live AV UI / CLI surface.
        // ----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
        {
            return new[]
            {
                // 1. PointerTool → PointerToolBatchInput (batch address conversion).
                //    No address argument — the dialog reads multi-line text input
                //    and processes each line independently.
                new NavigationTarget(
                    CommandName: "JumpToBatchInput",
                    TargetViewType: typeof(PointerToolBatchInputView),
                    TargetAddress: null),

                // 2. PointerTool → PointerToolCopyTo (address-copy dialog).
                //    Address argument carries the source value the user
                //    double-clicked in one of the 7 read-only address fields.
                new NavigationTarget(
                    CommandName: "JumpToCopyTo",
                    TargetViewType: typeof(PointerToolCopyToView),
                    TargetAddress: 0u),

                // 3. PointerTool → PointerTool self (CLI --pointer-search loop).
                //    WF ComandLineSearch reopens the form to drive batch CLI
                //    operations. AV equivalent: FEBuilderGBA.CLI --pointer-search
                //    delegates to PointerCalcCore directly — no window is opened.
                //    Manifest entry retained so JumpParityScanner records the
                //    WF↔AV symmetry as Match.
                new NavigationTarget(
                    CommandName: "JumpToSelf",
                    TargetViewType: typeof(PointerToolView),
                    TargetAddress: null),
            };
        }
    }
}
