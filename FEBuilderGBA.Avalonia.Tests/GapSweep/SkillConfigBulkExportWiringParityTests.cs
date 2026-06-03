// SPDX-License-Identifier: GPL-3.0-or-later
// #920 SLICE 1 — wiring-parity tests proving the SkillConfigSkillSystemView
// routes its "Bulk Export" button to the READ-ONLY cross-platform
// SkillConfigSkillSystemBulkExportCore seam (TSV + per-extended-anime PNGs).
//
// Static source read — no Avalonia head required.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

public class SkillConfigBulkExportWiringParityTests
{
    [Fact]
    public void SkillSystemView_BulkExport_CallsCoreSeam()
    {
        string source = ReadView("SkillConfigSkillSystemView.axaml.cs");

        Assert.Contains("BulkExport_Click", source);
        // Routes to the READ-ONLY Core seam, dereffing BOTH pointer LOCATIONS
        // via the VM (the Core seam p32's them).
        Assert.Contains("SkillConfigSkillSystemBulkExportCore.ExportAll", source);
        Assert.Contains("_vm.TextPointerLocation", source);
        Assert.Contains("_vm.AnimePointerLocation", source);
        // Per-extended-anime: builds the script via the merged #912 helper.
        Assert.Contains("SkillSystemsAnimeExportCore.BuildScriptLines", source);
        // The #912 PNG-dispose hygiene lesson + #922 thread 2: each unique IImage
        // is disposed in a finally so a mid-loop Save() throw can't leak bitmaps.
        Assert.Contains("img.Dispose()", source);
        Assert.Contains("finally", source);
        // The save filter is the *.SkillConfig.tsv documented contract.
        Assert.Contains("*.SkillConfig.tsv", source);
    }

    [Fact]
    public void CoreSeam_IsReadOnly_AndDerefsBothLocations()
    {
        string repoRoot = FindRepoRoot();
        string core = File.ReadAllText(Path.Combine(repoRoot,
            "FEBuilderGBA.Core", "SkillConfigSkillSystemBulkExportCore.cs"));

        // MUST-FIX 1: deref BOTH pointer locations.
        Assert.Contains("rom.p32(textPointerLocation)", core);
        Assert.Contains("rom.p32(animePointerLocation)", core);
        // MUST-FIX 2: getBlockDataCount with the i<255 predicate.
        Assert.Contains("getBlockDataCount", core);
        // MUST-FIX 3: NOT_FOUND guard.
        Assert.Contains("U.NOT_FOUND", core);
        // MUST-FIX 4: HEX dir suffix.
        Assert.Contains("\"anime\" + U.ToHexString(i)", core);
        // READ-ONLY: no ROM write APIs.
        Assert.DoesNotContain("write_u8", core);
        Assert.DoesNotContain("write_u16", core);
        Assert.DoesNotContain("write_u32", core);
        Assert.DoesNotContain("write_p32", core);
        Assert.DoesNotContain("SetU", core);
        Assert.DoesNotContain("BeginUndoScope", core);
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
