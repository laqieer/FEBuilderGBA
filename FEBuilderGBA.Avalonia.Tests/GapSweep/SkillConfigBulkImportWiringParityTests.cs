// SPDX-License-Identifier: GPL-3.0-or-later
// #923 SLICE 2 — wiring-parity tests proving the SkillConfigSkillSystemView
// routes its "Bulk Import" button to the BULK-ATOMIC cross-platform
// SkillConfigSkillSystemBulkImportCore seam (one atomic transaction; byte-
// identical rollback on any fault).
//
// Static source read — no Avalonia head required.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

public class SkillConfigBulkImportWiringParityTests
{
    [Fact]
    public void SkillSystemView_BulkImport_CallsCoreSeam()
    {
        string source = ReadView("SkillConfigSkillSystemView.axaml.cs");

        Assert.Contains("BulkImport_Click", source);
        // Routes to the BULK-ATOMIC Core seam, dereffing BOTH pointer LOCATIONS
        // via the VM (the Core seam p32's them).
        Assert.Contains("SkillConfigSkillSystemBulkImportCore.ImportAll", source);
        Assert.Contains("_vm.TextPointerLocation", source);
        Assert.Contains("_vm.AnimePointerLocation", source);
        // Reuses the single-import quantize loader for the per-frame PNGs.
        Assert.Contains("ImageImportService.LoadAndQuantizeFromFile", source);
        // The import filter is the *.SkillConfig.tsv documented contract.
        Assert.Contains("*.SkillConfig.tsv", source);
        // It does NOT open a UI UndoService scope (the Core seam owns the single
        // ambient BeginUndoScope; opening another would clobber it — non-reentrant).
        Assert.DoesNotContain("_undoService.Begin(\"Bulk Import", source);
    }

    [Fact]
    public void CoreSeam_IsBulkAtomic_WithThreeHighFixes()
    {
        string repoRoot = FindRepoRoot();
        string core = File.ReadAllText(Path.Combine(repoRoot,
            "FEBuilderGBA.Core", "SkillConfigSkillSystemBulkImportCore.cs"));

        // Derefs BOTH pointer locations.
        Assert.Contains("rom.p32(textPointerLocation)", core);
        Assert.Contains("rom.p32(animePointerLocation)", core);
        // #922 lesson: validate the pointer LOCATIONS (+3) BEFORE dereferencing.
        Assert.Contains("isSafetyOffset(textPointerLocation + 3", core);
        Assert.Contains("isSafetyOffset(animePointerLocation + 3", core);
        // getBlockDataCount with the i<255 predicate.
        Assert.Contains("getBlockDataCount", core);
        // NOT_FOUND guard.
        Assert.Contains("U.NOT_FOUND", core);

        // H3: ONE snapshot + ONE ambient BeginUndoScope wrapping the whole loop,
        // with the scope-alive assert and exactly-one Push on success.
        Assert.Contains("rom.Data.Clone()", core);
        Assert.Contains("ROM.BeginUndoScope(bulkUndoData)", core);
        Assert.Contains("IsAmbientUndoScopeActive", core);
        Assert.Contains("CoreState.Undo.Push(bulkUndoData)", core);

        // H2: per-skill import via manageSnapshot:false and return-value fault
        // detection (a non-empty returned string is treated as a fault).
        Assert.Contains("manageSnapshot: false", core);
        Assert.Contains("string.IsNullOrEmpty(err)", core);

        // H1: length-aware restore — down-resize before the in-place copy.
        Assert.Contains("write_resize_data((uint)snap.Length)", core);
        Assert.Contains("Array.Copy(snap, rom.Data, snap.Length)", core);

        // M1: textID written ONLY when non-zero.
        Assert.Contains("row.TextID != 0", core);
        // M2: applyRecycle toggle.
        Assert.Contains("applyRecycle", core);
        Assert.Contains("SkillConfigSkillTextIDRecycle.Convert", core);
    }

    static string ReadView(string fileName)
    {
        string repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot,
            "FEBuilderGBA.Avalonia", "Views", fileName));
    }

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
