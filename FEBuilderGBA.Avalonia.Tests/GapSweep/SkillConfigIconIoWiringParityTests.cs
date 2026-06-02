// SPDX-License-Identifier: GPL-3.0-or-later
// #898 — SkillConfig skill-icon Image Import/Export wiring parity tests.
//
// Asserts (via Roslyn-static source-text checks — no Avalonia head, no ROM)
// that the four applicable SkillConfig views route their ImageImport_Click /
// ImageExport_Click handlers through the shared SkillConfigIconIoHelper, and
// that FE8N v1 does NOT (it stays a read-only Log.Debug no-op because its WF
// form has no icon I/O and its icon address derivation lacks the 0x100 page
// offset used by v2/v3).
//
// Uses the regex-declaration handler-body extraction style from
// SkillConfigJumpWiringParityTests / ClassOPDemoParityTests so the test stays
// robust to handler parameter/modifier changes (e.g. the wired handlers are
// now `async void`).
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

public class SkillConfigIconIoWiringParityTests
{
    // The four wired views: their Import/Export handlers must call the helper.
    [Theory]
    [InlineData("SkillConfigSkillSystemView.axaml.cs")]
    [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs")]
    public void ImageImport_CallsSharedHelper_NotLogDebugNoop(string viewFile)
    {
        string source = ReadViewSource(viewFile);
        string body = ExtractHandlerBody(source, "ImageImport_Click");

        Assert.Contains("SkillConfigIconIoHelper.ImportIconAsync", body);
        Assert.DoesNotContain("Log.Debug", body);
    }

    [Theory]
    [InlineData("SkillConfigSkillSystemView.axaml.cs")]
    [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs")]
    public void ImageExport_CallsSharedHelper_NotLogDebugNoop(string viewFile)
    {
        string source = ReadViewSource(viewFile);
        string body = ExtractHandlerBody(source, "ImageExport_Click");

        Assert.Contains("SkillConfigIconIoHelper.ExportIconAsync", body);
        Assert.DoesNotContain("Log.Debug", body);
    }

    // The whole-file regex form mirrors the approved-plan acceptance check
    // (`void\s+ImageImport_Click[\s\S]*?SkillConfigIconIoHelper`).
    [Theory]
    [InlineData("SkillConfigSkillSystemView.axaml.cs")]
    [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs")]
    public void ImageImportHandler_ReachesHelper_WholeFileRegex(string viewFile)
    {
        string source = ReadViewSource(viewFile);
        Assert.Matches(@"void\s+ImageImport_Click[\s\S]*?SkillConfigIconIoHelper", source);
    }

    // FE8N v1 is OUT of scope: its handlers must NOT call the helper and must
    // remain read-only Log.Debug no-ops stating they're read-only.
    [Fact]
    public void FE8NVer1_Import_StaysReadOnly_DoesNotCallHelper()
    {
        string source = ReadViewSource("SkillConfigFE8NSkillView.axaml.cs");
        string importBody = ExtractHandlerBody(source, "ImageImport_Click");
        string exportBody = ExtractHandlerBody(source, "ImageExport_Click");

        Assert.DoesNotContain("SkillConfigIconIoHelper", importBody);
        Assert.DoesNotContain("SkillConfigIconIoHelper", exportBody);
        Assert.Contains("Log.Debug", importBody);
        Assert.Contains("read-only", importBody);
        Assert.Contains("read-only", exportBody);
    }

    // FE8UC09x must re-dereference the icon pointer fresh (entry+0 -> toOffset),
    // never write through a cached pointer (approved-plan correction).
    [Fact]
    public void FE8UC09x_Import_ReDereferencesIconPointerFresh()
    {
        string source = ReadViewSource("SkillConfigFE8UCSkillSys09xView.axaml.cs");
        string body = ExtractHandlerBody(source, "ImageImport_Click");

        Assert.Contains("rom.u32(_vm.CurrentAddr", body);
        Assert.Contains("U.toOffset", body);
    }

    // ---- Helpers (mirror SkillConfigJumpWiringParityTests) ----

    static string ReadViewSource(string viewFile)
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", viewFile);
        Assert.True(File.Exists(path), $"View source not found at {path}");
        return File.ReadAllText(path);
    }

    static string ExtractHandlerBody(string source, string handlerName)
    {
        // Allow an optional `async` modifier before the return type so the
        // wired `async void` handlers are matched as well as plain `void`.
        Match decl = Regex.Match(
            source,
            $@"void\s+{Regex.Escape(handlerName)}\s*\(",
            RegexOptions.Compiled);
        Assert.True(decl.Success, $"Handler '{handlerName}' not found in source");
        int sigIdx = decl.Index;

        int openBrace = source.IndexOf('{', sigIdx);
        Assert.True(openBrace >= 0, $"Opening brace for '{handlerName}' not found");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }
        throw new InvalidOperationException(
            $"Unbalanced braces extracting '{handlerName}' body");
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
