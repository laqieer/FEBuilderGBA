// SPDX-License-Identifier: GPL-3.0-or-later
// #913 SLICE 1 — wiring-parity tests proving the SkillConfig views route their
// "Animation Import" button to the cross-platform SkillSystemsAnimeImportCore
// seam via the shared SkillConfigAnimeImportHelper.
//
// The 4 views that WF's ImageUtilSkillSystemsAnimeCreator.Import covers
// (SkillSystem, FE8N Ver2, FE8N Ver3, FE8U-C SkillSys 0.9x) MUST call the
// helper. FE8N Ver1 (no animation pointer in its VM) MUST stay a stub.
//
// Static source read — no Avalonia head required.
using System;
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

public class SkillConfigAnimeImportWiringParityTests
{
    [Theory]
    [InlineData("SkillConfigSkillSystemView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs")]
    public void WiredView_AnimationImport_CallsHelper(string fileName)
    {
        string source = ReadView(fileName);
        Assert.Contains("SkillConfigAnimeImportHelper.ImportAsync", source);
        Assert.Contains("AnimationImport_Click", source);
        // Must pass the VM's real animation pointer, not a hardcoded literal.
        Assert.Contains("_vm.AnimationPointer", source);
        // Import handler must route the shared UndoService for atomic rollback.
        Assert.Contains("_undoService", source);
    }

    [Fact]
    public void FE8NVer1View_AnimationImport_StaysStub()
    {
        // FE8N Ver1 has no AnimationPointer (its VM hardcodes 0); the import
        // handler must remain a no-op stub and must NOT call the helper.
        string source = ReadView("SkillConfigFE8NSkillView.axaml.cs");
        Assert.DoesNotContain("SkillConfigAnimeImportHelper.ImportAsync", source);
    }

    [Fact]
    public void Helper_UsesCoreImportSeam_AndFE8JOnlyContract()
    {
        string repoRoot = FindRepoRoot();
        string helper = File.ReadAllText(Path.Combine(repoRoot,
            "FEBuilderGBA.Avalonia", "Services", "SkillConfigAnimeImportHelper.cs"));

        // Routes the ROM-mutating Core seam.
        Assert.Contains("SkillSystemsAnimeImportCore.ImportSkillAnimation", helper);
        // Wraps the write in an UndoService scope (atomic rollback on failure).
        Assert.Contains("undoService.Begin", helper);
        Assert.Contains("undoService.Commit", helper);
        Assert.Contains("undoService.Rollback", helper);
        // Pointer==0 is rejected with a clear error (no-op guard).
        Assert.Contains("animationPointer == 0", helper);
    }

    [Fact]
    public void CoreSeam_FE8UDeferral_IsExplicit()
    {
        // The Core import must short-circuit FE8U with a clean, ZERO-mutation
        // not-supported error (Slice 2 re-emits the program code).
        string repoRoot = FindRepoRoot();
        string core = File.ReadAllText(Path.Combine(repoRoot,
            "FEBuilderGBA.Core", "SkillSystemsAnimeImportCore.cs"));
        Assert.Contains("is_multibyte", core);
        Assert.Contains("FE8U", core);
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
