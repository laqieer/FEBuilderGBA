// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the DumpStructSelectDialog navigation manifest (#439).
//
// The dispatcher dialog exposes 4 distinct jump targets that mirror the
// WinForms DumpStructSelectDialogForm.ShowDumpSelectDialog routing:
//   - Self (re-open dispatcher)             -> DumpStructSelectDialogView
//   - Output text dialog (export results)   -> DumpStructSelectToTextDialogView
//   - Func_Binary handler (Hex Editor)      -> HexEditorView
//   - RAM-pointer fallback                  -> PointerToolCopyToView
using System.Linq;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class DumpStructSelectDialogNavigationTargetsTests
    {
        [Fact]
        public void Manifest_DeclaresAllFourTargets()
        {
            var vm = new DumpStructSelectDialogViewModel();
            INavigationTargetSource src = vm;
            var targets = src.GetNavigationTargets();
            Assert.NotNull(targets);
            // Self-jump (the dispatcher re-opening itself, e.g. SaveDumpAutomatic).
            Assert.Contains(targets, t => t.TargetViewType == typeof(DumpStructSelectDialogView));
            // Export results: opens the text-display dialog.
            Assert.Contains(targets, t => t.TargetViewType == typeof(DumpStructSelectToTextDialogView));
            // Binary button: opens the Hex Editor.
            Assert.Contains(targets, t => t.TargetViewType == typeof(HexEditorView));
            // RAM-pointer fallback: opens the pointer-tool dialog.
            Assert.Contains(targets, t => t.TargetViewType == typeof(PointerToolCopyToView));
        }

        [Fact]
        public void Manifest_HasFourEntries()
        {
            // Exactly 4 distinct targets — no fabricated extras.
            var vm = new DumpStructSelectDialogViewModel();
            INavigationTargetSource src = vm;
            var targets = src.GetNavigationTargets();
            Assert.Equal(4, targets.Count);
        }

        [Fact]
        public void Manifest_NoEntryHasIssueRef()
        {
            // All four targets are functional Avalonia views with no known-broken
            // jumps. If a follow-up bug is discovered, set IssueRef on that entry.
            var vm = new DumpStructSelectDialogViewModel();
            INavigationTargetSource src = vm;
            var targets = src.GetNavigationTargets();
            Assert.All(targets, t => Assert.Null(t.IssueRef));
        }

        [Fact]
        public void Manifest_DistinctCommandNamesPerTarget()
        {
            // Each manifest entry must have a unique CommandName so the scanner
            // can disambiguate them in the report.
            var vm = new DumpStructSelectDialogViewModel();
            INavigationTargetSource src = vm;
            var targets = src.GetNavigationTargets();
            var commandNames = targets.Select(t => t.CommandName).ToList();
            Assert.Equal(commandNames.Count, commandNames.Distinct().Count());
        }
    }
}
