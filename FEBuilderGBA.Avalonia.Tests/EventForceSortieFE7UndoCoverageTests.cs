// SPDX-License-Identifier: GPL-3.0-or-later
// Regression tests for #1439 — the Avalonia Force Sortie (FE7) editor
// (EventForceSortieFE7View) must wrap BOTH _vm.Write() (outer D0 pointer) and
// _vm.WriteSubEntry() (inner unit-list sub-entry) in a single UndoService scope
// so Edit > Undo can revert them together. Before the fix OnWrite called
// _vm.Write()/_vm.WriteSubEntry() with no Begin/Commit, so the bare
// EditorFormRef.WriteFields path recorded nothing into the undo buffer (the
// known sibling gap from #1427).
//
// Static-analysis guard (DiscoverViewCoveredVmMethods over the live
// EventForceSortieFE7View.axaml.cs) — FAILS pre-fix, PASSES post-fix, proving
// the view-level scope exists around both writes. Mirrors the canary pattern in
// EventForceSortieUndoCoverageTests (#1427) / UndoCoverageScannerTests.
using System;
using System.IO;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class EventForceSortieFE7UndoCoverageTests
    {
        /// <summary>
        /// The View must register BOTH EventForceSortieFE7ViewModel.Write and
        /// .WriteSubEntry as undo-covered: OnWrite wraps both calls in
        /// _undoService.Begin / try-Commit / catch-Rollback.
        /// FAILS before the #1439 fix (no Begin/Commit), PASSES after.
        /// </summary>
        [SkippableFact]
        public void EventForceSortieFE7View_WrapsBothWrites_InUndoScope()
        {
            string? repoRoot = FindRepoRoot();
            Skip.If(repoRoot == null,
                "Repo root (FEBuilderGBA.sln) not found — running from a published binary outside the source tree.");

            string viewPath = Path.Combine(repoRoot!,
                "FEBuilderGBA.Avalonia", "Views", "EventForceSortieFE7View.axaml.cs");
            Assert.True(File.Exists(viewPath), $"View source not found at {viewPath}");

            var covered = UndoCoverageScanner.DiscoverViewCoveredVmMethods(new[] { viewPath });
            Assert.Contains(("EventForceSortieFE7ViewModel", "Write"), covered);
            Assert.Contains(("EventForceSortieFE7ViewModel", "WriteSubEntry"), covered);
        }

        /// <summary>
        /// Walk up from the test binary's base directory looking for
        /// FEBuilderGBA.sln. Returns null when running outside the source tree.
        /// </summary>
        static string? FindRepoRoot()
        {
            string start = AppDomain.CurrentDomain.BaseDirectory;
            for (DirectoryInfo? dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            }
            return null;
        }
    }
}
