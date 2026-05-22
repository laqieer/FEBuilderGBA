// SPDX-License-Identifier: GPL-3.0-or-later
// Sweep-level integration test for the DumpStructSelectDialog gap-sweep
// fix (#439). Asserts that the 5 WinForms JumpForm callsites whose
// SourceForm is DumpStructSelectDialogForm — listed at
// docs/avalonia-gaps/2026-05-22-jumps-sweep.md lines 222-226 — are all
// classified as Match (no longer MissingAvManifest) after the manifest is
// in place.
using System;
using System.IO;
using System.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class DumpStructSelectDialogParityTests
    {
        [Fact]
        public void FiveWfJumps_AllMatchAfterFix()
        {
            string? repoRoot = FindRepoRoot();
            if (repoRoot == null) return; // Outside source tree, skip.

            var rows = JumpParityScanner.Scan(repoRoot)
                .Where(r => r.SourceForm == "DumpStructSelectDialogForm")
                .ToList();

            // Five WF callsites: lines 1165, 1176, 1184, 1254, 1263 of
            // DumpStructSelectDialogForm.cs. Two self-jumps (1176 + 1263),
            // one each to DumpStructSelectToTextDialogForm (1254),
            // HexEditorForm (1184), and PointerToolCopyToForm (1165).
            Assert.Equal(5, rows.Count);
            Assert.All(rows, r => Assert.Equal(JumpRowStatus.Match, r.Status));

            // Distribution check — exactly the documented 2/1/1/1 split.
            Assert.Equal(2, rows.Count(r => r.TargetWfType == "DumpStructSelectDialogForm"));
            Assert.Equal(1, rows.Count(r => r.TargetWfType == "DumpStructSelectToTextDialogForm"));
            Assert.Equal(1, rows.Count(r => r.TargetWfType == "HexEditorForm"));
            Assert.Equal(1, rows.Count(r => r.TargetWfType == "PointerToolCopyToForm"));
        }

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
