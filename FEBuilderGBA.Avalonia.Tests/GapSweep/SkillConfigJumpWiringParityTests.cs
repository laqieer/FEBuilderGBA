// SPDX-License-Identifier: GPL-3.0-or-later
// #895 — SkillConfig Jump-button wiring parity tests.
//
// #996 UPDATE: the 5 SkillConfig views' JumpToEditor_Click no longer opens the
// Animation Creator blank/with-garbage. Skill-animation Creator seeding is NOT
// yet supported (no populated/verifiable skill-animation editor context with the
// available ROMs), so the handler now shows an HONEST "not yet supported" message
// (#996 follow-up) instead of an empty/garbage Creator. JumpToCombatArt_Click
// (FE8NVer3 only) → PatchManagerView is unchanged.
//
// These are Roslyn-static source-text assertions (no Avalonia head, no ROM):
// each view's `.axaml.cs` JumpToEditor_Click body must show the honest message
// and must NOT open ToolAnimationCreatorView, and must NO LONGER contain a
// `Log.Debug` no-op for that handler.
//
// NOTE: the NavigationTargets manifest / IssueRef KnownGap / button-wired
// AutomationId tests already exist in the per-view parity files — this file
// adds ONLY the new handler-body open-source assertion value (per the #895
// approved-plan correction: do not duplicate the manifest/button tests).
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

public class SkillConfigJumpWiringParityTests
{
    // -----------------------------------------------------------------
    // JumpToEditor_Click → honest "not yet supported" message (#996).
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("SkillConfigFE8NSkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs")]
    [InlineData("SkillConfigSkillSystemView.axaml.cs")]
    public void JumpToEditor_ShowsNotSupportedMessage_NotBlankCreatorOpen(string viewFile)
    {
        string body = ExtractHandlerBody(ReadViewSource(viewFile), "JumpToEditor_Click");

        // #996: skill-animation Creator seeding is not supported — the handler must
        // surface an honest ShowInfo message instead of opening a blank Creator.
        Assert.Contains("ShowInfo", body);
        Assert.Contains("not yet supported for Skill animations", body);

        // It must NOT open the Animation Creator (would be blank/garbage).
        Assert.DoesNotContain("Open<ToolAnimationCreatorView>", body);

        // The stub Log.Debug no-op must be gone from THIS handler.
        Assert.DoesNotContain("Log.Debug", body);
    }

    // -----------------------------------------------------------------
    // JumpToCombatArt_Click → PatchManagerView (FE8NVer3 only).
    // -----------------------------------------------------------------

    [Fact]
    public void FE8NVer3_JumpToCombatArt_OpensPatchManager_NotLogDebugNoop()
    {
        string body = ExtractHandlerBody(
            ReadViewSource("SkillConfigFE8NVer3SkillView.axaml.cs"),
            "JumpToCombatArt_Click");

        // #1009: the handler now FILTERS + selects the combat-art patch via the
        // PatchManagerView.JumpTo (#428) seam, instead of a bare unfiltered open.
        Assert.Matches(@"WindowManager\.Instance\.Navigate<PatchManagerView>", body);
        Assert.Contains("WindowManager.Instance.FindOpen<PatchManagerView>", body);
        Assert.Contains("JumpTo(\"FE8N SKILL COMBAT ART\", 0)", body);
        Assert.DoesNotContain("Log.Debug", body);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string ReadViewSource(string viewFile)
    {
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views", viewFile);
        Assert.True(File.Exists(path), $"View source not found at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Extract the brace-balanced body of the named click handler from the
    /// code-behind source text. Returns the substring between the handler's
    /// opening `{` and its matching `}` so source assertions are scoped to the
    /// single handler (not the whole file).
    ///
    /// The handler declaration is located by a regex on `void &lt;name&gt;(` rather
    /// than a hard-coded full signature, so it stays robust to parameter
    /// nullability/modifier/type changes (matches the regex-declaration style
    /// the other GapSweep parity tests use, e.g. ClassOPDemoParityTests).
    /// </summary>
    static string ExtractHandlerBody(string source, string handlerName)
    {
        Match decl = Regex.Match(source, $@"void\s+{Regex.Escape(handlerName)}\s*\(", RegexOptions.Compiled);
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

    /// <summary>
    /// Walk parent directories from the test bin/ folder until we find the
    /// repo root (identified by FEBuilderGBA.sln). Mirrors the existing
    /// per-view parity tests' FindRepoRoot helper.
    /// </summary>
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
