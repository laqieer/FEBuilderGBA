// SPDX-License-Identifier: GPL-3.0-or-later
// Parity regression tests for the SkillConfig per-frame animation preview
// (#1010). Asserts the 4 SkillConfig views now render the per-frame preview
// via the shared SkillConfigAnimePreview helper (instead of the old always-
// blank SetPreviewBitmap(null)), and that the 4 VMs no longer claim the
// preview is unavailable.
//
// Roslyn-static source-text checks only — no Avalonia head needed.
using System.IO;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

public class SkillConfigAnimePreviewParityTests
{
    /// <summary>
    /// Each of the 4 views must reference the shared SkillConfigAnimePreview
    /// helper (via the _animePreview field).
    /// </summary>
    [Theory]
    [InlineData("SkillConfigSkillSystemView")]
    [InlineData("SkillConfigFE8NVer2SkillView")]
    [InlineData("SkillConfigFE8NVer3SkillView")]
    [InlineData("SkillConfigFE8UCSkillSys09xView")]
    public void View_ReferencesSkillConfigAnimePreview(string view)
    {
        string src = ReadViewSource(view);
        Assert.Contains("SkillConfigAnimePreview", src);
        Assert.Contains("_animePreview", src);
    }

    /// <summary>
    /// Each view must render the selected frame via
    /// SetPreviewBitmap(_animePreview.TryGetFrameBitmap( in BOTH UpdateUI and
    /// ShowFrameUpDown_ValueChanged — i.e. the valid branch no longer ONLY
    /// calls SetPreviewBitmap(null).
    /// </summary>
    [Theory]
    [InlineData("SkillConfigSkillSystemView")]
    [InlineData("SkillConfigFE8NVer2SkillView")]
    [InlineData("SkillConfigFE8NVer3SkillView")]
    [InlineData("SkillConfigFE8UCSkillSys09xView")]
    public void View_RendersFrameBitmapInUpdateUIAndFrameChanged(string view)
    {
        string src = ReadViewSource(view);

        // The actual render call, present at least twice (UpdateUI valid branch
        // + ShowFrameUpDown_ValueChanged).
        const string renderCall = "_animePreview.TryGetFrameBitmap(";
        int idx1 = src.IndexOf(renderCall, System.StringComparison.Ordinal);
        Assert.True(idx1 >= 0, $"{view} must call {renderCall}");
        int idx2 = src.IndexOf(renderCall, idx1 + 1, System.StringComparison.Ordinal);
        Assert.True(idx2 >= 0, $"{view} must call {renderCall} in BOTH UpdateUI and ShowFrameUpDown_ValueChanged");

        // The ShowFrameUpDown_ValueChanged handler specifically routes the
        // selected frame through SetPreviewBitmap(_animePreview.TryGetFrameBitmap(.
        Assert.Contains("SetPreviewBitmap(_animePreview.TryGetFrameBitmap(", src);

        // The cache must be invalidated + disposed on window close.
        Assert.Contains("_animePreview.Clear()", src);
    }

    /// <summary>
    /// The stale always-blank preview comments must be gone from the 4 views.
    /// </summary>
    [Theory]
    [InlineData("SkillConfigSkillSystemView")]
    [InlineData("SkillConfigFE8NVer2SkillView")]
    [InlineData("SkillConfigFE8NVer3SkillView")]
    [InlineData("SkillConfigFE8UCSkillSys09xView")]
    public void View_NoStalePreviewBlankComment(string view)
    {
        string src = ReadViewSource(view);
        Assert.DoesNotContain("leave the preview Image blank", src);
        Assert.DoesNotContain("Real per-frame rendering pending #500", src);
        Assert.DoesNotContain("Real frame rendering depends", src);
    }

    /// <summary>
    /// The 4 VMs must no longer claim the preview is unavailable / tracked by
    /// #500 (the BinInfoText wording fix).
    /// </summary>
    [Theory]
    [InlineData("SkillConfigSkillSystemViewModel")]
    [InlineData("SkillConfigFE8NVer2SkillViewModel")]
    [InlineData("SkillConfigFE8NVer3SkillViewModel")]
    [InlineData("SkillConfigFE8UCSkillSys09xViewModel")]
    public void ViewModel_NoPreviewUnavailableOr500(string vm)
    {
        string src = ReadViewModelSource(vm);
        Assert.DoesNotContain("preview unavailable", src);
        Assert.DoesNotContain("#500", src);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string ReadViewSource(string viewName)
    {
        string repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "Views", viewName + ".axaml.cs"));
    }

    static string ReadViewModelSource(string vmName)
    {
        string repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "ViewModels", vmName + ".cs"));
    }

    static string FindRepoRoot()
    {
        string? dir = System.AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        if (dir == null)
            throw new System.InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
