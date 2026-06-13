// SPDX-License-Identifier: GPL-3.0-or-later
// #895 — SkillConfig Jump-button wiring parity tests.
//
// #1115 UPDATE: the skill carve-out of #996 is now implemented. The 4 anime-
// capable SkillConfig views (SkillSystem, FE8N Ver2/Ver3, CSkillSys09x) SEED the
// Animation Creator from the selected skill's animation via the shared
// SkillConfigAnimeJumpHelper (probe-before-open — a 0/empty pointer shows an honest
// message, never a blank Creator). FE8N Ver1 has NO per-skill animation pointer in
// either WinForms or Avalonia (render-only), so its jump shows an honest render-only
// message and wires NO helper. JumpToCombatArt_Click (FE8NVer3 only) → PatchManagerView
// is unchanged.
//
// These are Roslyn-static source-text assertions (no Avalonia head, no ROM):
// each anime view's JumpToEditor_Click must route through SkillConfigAnimeJumpHelper;
// FE8N Ver1's must show the render-only message; none may keep the old #996 carve-out
// string or a Log.Debug no-op.
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
    // JumpToEditor_Click → seed the Animation Creator via the shared helper
    // (#1115). The 4 anime-capable views route through SkillConfigAnimeJumpHelper.
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("SkillConfigFE8NVer2SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8NVer3SkillView.axaml.cs")]
    [InlineData("SkillConfigFE8UCSkillSys09xView.axaml.cs")]
    [InlineData("SkillConfigSkillSystemView.axaml.cs")]
    public void JumpToEditor_AnimeVariants_SeedViaSharedHelper(string viewFile)
    {
        string body = ExtractHandlerBody(ReadViewSource(viewFile), "JumpToEditor_Click");

        // #1115: the handler routes through the shared probe-before-open helper,
        // passing the selected skill id + its resolved animation pointer.
        Assert.Contains("SkillConfigAnimeJumpHelper.JumpToCreator", body);
        Assert.Contains("_vm.SelectedId", body);
        Assert.Contains("_vm.AnimationPointer", body);

        // The stale #996 carve-out string and the stub Log.Debug no-op must be gone.
        Assert.DoesNotContain("not yet supported for Skill animations", body);
        Assert.DoesNotContain("Log.Debug", body);
    }

    // -----------------------------------------------------------------
    // FE8N Ver1 → honest render-only message (no animation pointer; #1115).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpToEditor_FE8NVer1_ShowsRenderOnlyMessage_NoCreatorOpen()
    {
        string body = ExtractHandlerBody(
            ReadViewSource("SkillConfigFE8NSkillView.axaml.cs"), "JumpToEditor_Click");

        // FE8N Ver1 has NO per-skill animation pointer (render-only) — the handler
        // shows an honest message and does NOT seed the Creator.
        Assert.Contains("ShowInfo", body);
        Assert.Contains("render-only", body);

        // It must NOT actually seed the Creator: no helper CALL, no direct open.
        // (The explanatory comment may NAME the helper, so assert on the CALL.)
        Assert.DoesNotContain("SkillConfigAnimeJumpHelper.JumpToCreator", body);
        Assert.DoesNotContain("Open<ToolAnimationCreatorView>", body);
        // The stale #996 carve-out message string must be gone (the message shown
        // is the new render-only one). Match the message-text fragment only — the
        // comment legitimately references the old behaviour by issue context.
        Assert.DoesNotContain("not yet supported for Skill animations.\"", body);
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
