// SPDX-License-Identifier: GPL-3.0-or-later
// #910 — wiring-parity tests proving the SkillConfig views route their
// "Animation Export" button to the cross-platform SkillSystemsAnimeExportCore
// seam via the shared SkillConfigAnimeExportHelper.
//
// The 4 views that WF's ImageUtilSkillSystemsAnimeCreator.Export covers
// (SkillSystem, FE8N Ver2, FE8N Ver3, FE8U-C SkillSys 0.9x) MUST call the
// helper. FE8N Ver1 (no animation pointer in its VM) MUST stay a stub.
//
// Static source read — no Avalonia head required.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

public class SkillConfigAnimeExportWiringParityTests
{
    [Theory]
    [InlineData("SkillConfigSkillSystemView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs")]
    public void WiredView_AnimationExport_CallsHelper(string fileName)
    {
        string source = ReadView(fileName);
        Assert.Contains("SkillConfigAnimeExportHelper.ExportAsync", source);
        Assert.Contains("AnimationExport_Click", source);
        // Must pass the VM's real animation pointer, not a hardcoded literal.
        Assert.Contains("_vm.AnimationPointer", source);
    }

    [Fact]
    public void FE8NVer1View_AnimationExport_StaysStub()
    {
        // FE8N Ver1 has no AnimationPointer (its VM hardcodes 0); the export
        // handler must remain a no-op stub and must NOT call the helper.
        string source = ReadView("SkillConfigFE8NSkillView.axaml.cs");
        Assert.DoesNotContain("SkillConfigAnimeExportHelper.ExportAsync", source);
    }

    [Fact]
    public void Helper_UsesCoreExportSeam_AndGameFrameGifDelay()
    {
        string repoRoot = FindRepoRoot();
        string helper = File.ReadAllText(Path.Combine(repoRoot,
            "FEBuilderGBA.Avalonia", "Services", "SkillConfigAnimeExportHelper.cs"));

        // Uses the READ-ONLY Core seam.
        Assert.Contains("SkillSystemsAnimeExportCore.ExportSkillAnimation", helper);
        Assert.Contains("SkillSystemsAnimeExportCore.BuildScriptLines", helper);
        // GIF delay unit matches every other Avalonia animation export.
        Assert.Contains("U.GameFrameSecToGifFrameSec", helper);
        // Pointer==0 is a no-op (early guard).
        Assert.Contains("animationPointer == 0", helper);
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
