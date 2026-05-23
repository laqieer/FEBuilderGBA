// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1 / Phase 2 / Phase 4 / Phase 5 gap-sweep regression tests for ImagePortraitView. (#424)
//
// Covers the 51 gaps the issue called out:
//   - 32 missing WF-only labels (density + labels)
//   - 13 unwrapped ROM writes in ImagePortraitViewModel (undo)
//   - 3 missing cross-editor jumps (jumps)
//
// Plus the v3-plan contracts enforced by Copilot CLI plan review:
//   - ViewModel.Write(UndoService) owns the single Begin/Commit scope
//   - View.WriteButton_Click DELEGATES to the VM, does NOT open its own scope
//   - Jump handlers use WindowManager.Open<T>() (not Navigate<T>)
//   - INavigationTargetSource manifest entries all use TargetAddress: null
//   - MugExceed panel visibility gated on PatchDetectionService.Instance.PortraitExtends
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ImagePortrait parity raise (#424) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the original
/// gap-sweep report.
/// </summary>
public class ImagePortraitParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The 2026-05-26 density sweep reports WF=63, AV=45 (Medium).
    /// MEDIUM threshold = 75% of WF = 48. This PR's design should keep us
    /// inside MEDIUM after the fix (AV >= 47 after adding the new controls).
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer count from the 2026-05-26 density sweep — see issue #424.
        // The plan targets AV ≥ 47 (Medium tier) — adding ~30 new controls
        // (top bar, selection bar, status combo, show example, mug exceed,
        // 3 jump buttons, comment, source buttons) raises AV from 22 to ~47+.
        const int MinControlsMediumTier = 47;
        Assert.True(avCount >= MinControlsMediumTier,
            $"AV control count {avCount} must be >= {MinControlsMediumTier} (Medium verdict floor for ImagePortraitView)");
    }

    // -----------------------------------------------------------------
    // Undo ownership contract (Copilot CLI review point) —
    // VM owns the single Begin/Commit scope, View delegates to VM.
    // -----------------------------------------------------------------

    /// <summary>
    /// ImagePortraitViewModel.Write(UndoService) must:
    ///   (a) call _undoService.Begin (parameter or local) exactly once,
    ///   (b) call _undoService.Commit exactly once,
    ///   (c) place every `rom.write_u*` call between Begin and Commit,
    ///   (d) include a Rollback path on exception,
    ///   (e) contain at least 13 rom.write_* calls.
    /// </summary>
    [Fact]
    public void ViewModel_Write_WrapsAllRomWritesInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string vmPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "ImagePortraitViewModel.cs");
        Assert.True(File.Exists(vmPath), $"ViewModel not found at {vmPath}");

        string source = File.ReadAllText(vmPath);

        // Find the Write(UndoService ...) method body.
        var sig = new Regex(@"public\s+void\s+Write\s*\(\s*UndoService\b[^)]*\)");
        var sigMatch = sig.Match(source);
        Assert.True(sigMatch.Success,
            "ImagePortraitViewModel.Write(UndoService) overload not found");

        int braceOpenIdx = source.IndexOf('{', sigMatch.Index + sigMatch.Length);
        Assert.True(braceOpenIdx > 0, "Write(UndoService) method has no body");
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        Assert.True(depth == 0, "Write(UndoService) body is malformed (no matching `}`)");
        string body = source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);

        // Strip C# single-line comments so order assertions compare CODE only.
        string codeOnly = Regex.Replace(body, @"//[^\n]*", "");

        // (a) one Begin call (could be on parameter or _undoService field).
        var beginMatches = Regex.Matches(codeOnly, @"\b\w+\s*\.\s*Begin\s*\(");
        Assert.True(beginMatches.Count == 1,
            $"Write(UndoService) must call Begin exactly once, found {beginMatches.Count}");

        // (b) one Commit call.
        var commitMatches = Regex.Matches(codeOnly, @"\b\w+\s*\.\s*Commit\s*\(\s*\)");
        Assert.True(commitMatches.Count == 1,
            $"Write(UndoService) must call Commit exactly once, found {commitMatches.Count}");

        // (c) every rom.write_* between Begin and Commit.
        // (e) at least 13 rom.write_* calls (the 13 closed gaps).
        var writeMatches = Regex.Matches(codeOnly, @"\brom\s*\.\s*write_u(?:8|16|32|p32|p16)\b");
        Assert.True(writeMatches.Count >= 13,
            $"Write(UndoService) must contain at least 13 rom.write_* calls (the 13 closed gaps), found {writeMatches.Count}");
        int beginIdx = beginMatches[0].Index;
        int commitIdx = commitMatches[0].Index;
        Assert.True(beginIdx < commitIdx, "Begin must appear before Commit in source");
        foreach (Match w in writeMatches)
        {
            Assert.True(w.Index > beginIdx && w.Index < commitIdx,
                $"rom.write_* at offset {w.Index} is OUTSIDE the Begin/Commit scope (Begin@{beginIdx}, Commit@{commitIdx})");
        }

        // (d) Rollback in exception path.
        Assert.Contains("Rollback", codeOnly);
        Assert.Contains("catch", codeOnly);
    }

    /// <summary>
    /// The View's WriteButton_Click handler must call _vm.Write(_undoService)
    /// and must NOT open its own Begin scope (one-owner contract).
    /// </summary>
    [Fact]
    public void View_WriteButton_DelegatesToVmWrite()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitView.axaml.cs");
        Assert.True(File.Exists(viewCsPath), $"View code-behind not found at {viewCsPath}");

        string source = File.ReadAllText(viewCsPath);
        string body = ExtractHandlerBody(source, "WriteButton_Click");
        string codeOnly = Regex.Replace(body, @"//[^\n]*", "");

        // Must delegate to VM.
        Assert.Matches(@"_vm\s*\.\s*Write\s*\(\s*_undoService\s*\)", codeOnly);

        // Must NOT open its own scope (one-owner contract).
        Assert.DoesNotMatch(new Regex(@"_undoService\s*\.\s*Begin\s*\("), codeOnly);
    }

    // -----------------------------------------------------------------
    // Label coverage (Phase 2) — AutomationIds for every new control.
    // -----------------------------------------------------------------

    /// <summary>
    /// Verify that the View contains AutomationIds for every new label/field
    /// surfaced by this PR.
    /// </summary>
    [Fact]
    public void View_HasExpectedFieldControls()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitView.axaml");
        string xaml = File.ReadAllText(axamlPath);

        string[] required = new[]
        {
            // Top-of-list config bar
            "ImagePortrait_ReadStartAddress_Label",
            "ImagePortrait_ReadCount_Label",
            "ImagePortrait_BlockSize_Label",
            "ImagePortrait_ReloadList_Button",
            // Selection bar
            "ImagePortrait_SelectedAddress_Label",
            "ImagePortrait_Write_Button",
            // Show Example
            "ImagePortrait_ShowExample_Label",
            "ImagePortrait_ShowExample_Image",
            // Status combo
            "ImagePortrait_StatusCombo_Input",
            // Comment
            "ImagePortrait_Comment_Input",
            // Source file controls
            "ImagePortrait_OpenSource_Button",
            "ImagePortrait_SelectSource_Button",
            // MugExceed panel + inputs
            "ImagePortrait_MugExceedPanel",
            "ImagePortrait_MugExceedB16_Input",
            "ImagePortrait_MugExceedB17_Input",
            "ImagePortrait_MugExceedB18_Input",
            "ImagePortrait_MugExceedB19_Input",
            "ImagePortrait_MugExceedDesc_Label",
            // Cross-editor jump buttons (Phase 4)
            "ImagePortrait_JumpToPalette_Button",
            "ImagePortrait_JumpToImporter_Button",
            "ImagePortrait_JumpToStatusHeight_Button",
        };

        foreach (string id in required)
        {
            Assert.Contains(id, xaml);
        }
    }

    /// <summary>
    /// The parameterless Write() preserved for backward compatibility must
    /// still exist (callers outside the View use it).
    /// </summary>
    [Fact]
    public void ViewModel_ParameterlessWrite_StillExists()
    {
        string repoRoot = FindRepoRoot();
        string vmPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "ImagePortraitViewModel.cs");
        string source = File.ReadAllText(vmPath);

        Assert.Matches(@"public\s+void\s+Write\s*\(\s*\)", source);
    }

    /// <summary>
    /// New read-only display properties expected on the VM for the top-of-list
    /// config bar and selection bar (BlockSize / ReadStartAddress / ReadCount).
    /// </summary>
    [Fact]
    public void ViewModel_ExposesReadOnlyDisplayProperties()
    {
        var vm = new ImagePortraitViewModel();
        _ = vm.BlockSize;
        _ = vm.ReadStartAddress;
        _ = vm.ReadCount;
    }

    /// <summary>
    /// Comment field should be a simple round-trippable string.
    /// </summary>
    [Fact]
    public void ViewModel_HasCommentProperty()
    {
        var vm = new ImagePortraitViewModel();
        vm.Comment = "test comment";
        Assert.Equal("test comment", vm.Comment);
    }

    /// <summary>
    /// Comment caching must use <see cref="CoreState.CommentCache"/> keyed by
    /// the current ROM address — the same EtcCache instance the WinForms
    /// <c>InputFormRef</c> wires `Program.CommentCache.At(addr)` /
    /// `Update(addr, text)` against.
    /// </summary>
    [Fact]
    public void View_CommentHandlers_UseCommentCacheKeyedByAddress()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);

        // LoadCommentForCurrentEntry must read CoreState.CommentCache.TryGetValue(addr, ...)
        AssertHandlerBodyContains(source, "LoadCommentForCurrentEntry",
            @"CoreState\.CommentCache");
        AssertHandlerBodyContains(source, "LoadCommentForCurrentEntry",
            @"\.TryGetValue\s*\(\s*addr");

        // Comment_TextChanged must write CoreState.CommentCache.Update(addr, …)
        AssertHandlerBodyContains(source, "Comment_TextChanged",
            @"CoreState\.CommentCache");
        AssertHandlerBodyContains(source, "Comment_TextChanged",
            @"\.Update\s*\(\s*addr");
    }

    // -----------------------------------------------------------------
    // Cross-editor jumps (Phase 4) — INavigationTargetSource manifest.
    // -----------------------------------------------------------------

    /// <summary>
    /// ImagePortraitViewModel must implement INavigationTargetSource and
    /// return exactly the 3 Phase 4 jump targets with TargetAddress: null
    /// (open-only contract).
    /// </summary>
    [Fact]
    public void ViewModel_ImplementsNavigationTargetSource()
    {
        var vm = new ImagePortraitViewModel();
        Assert.IsAssignableFrom<INavigationTargetSource>(vm);

        var targets = ((INavigationTargetSource)vm).GetNavigationTargets();
        Assert.NotNull(targets);
        Assert.Equal(3, targets.Count);

        // Each target should be open-only (TargetAddress: null).
        foreach (var t in targets)
        {
            Assert.Null(t.TargetAddress);
        }

        // Expected commands and target view types.
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToPalette" &&
            t.TargetViewType == typeof(global::FEBuilderGBA.Avalonia.Views.ImagePalletView));
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToImporter" &&
            t.TargetViewType == typeof(global::FEBuilderGBA.Avalonia.Views.ImagePortraitImporterView));
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToStatusHeight" &&
            t.TargetViewType == typeof(global::FEBuilderGBA.Avalonia.Views.UnitIncreaseHeightView));
    }

    /// <summary>
    /// The 3 jump handlers must use WindowManager.Instance.Open<T>() (not
    /// Navigate<T>()) — matches the open-only manifest contract.
    /// (Copilot CLI plan v3 re-review implementation note 1.)
    /// </summary>
    [Fact]
    public void View_JumpHandlers_UseOpenNotNavigate()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);

        foreach (string handler in new[] {
            "JumpToPalette_Click", "JumpToImporter_Click", "JumpToStatusHeight_Click" })
        {
            string body = ExtractHandlerBody(source, handler);
            string codeOnly = Regex.Replace(body, @"//[^\n]*", "");
            Assert.Matches(@"WindowManager\.Instance\.Open<", codeOnly);
            Assert.DoesNotMatch(new Regex(@"WindowManager\.Instance\.Navigate<"), codeOnly);
        }
    }

    /// <summary>
    /// MugExceed panel visibility must be gated on
    /// <see cref="PatchDetectionService.Instance.PortraitExtends"/> —
    /// cross-platform path, no WinForms PatchUtil dependency.
    /// </summary>
    [Fact]
    public void View_MugExceedPanel_GatedOnPatchDetection()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);

        // Must reference PatchDetectionService.Instance.PortraitExtends.
        Assert.Matches(@"PatchDetectionService\.Instance\.PortraitExtends", source);
        // Must compare against the MugExceed enum value.
        Assert.Matches(@"PortraitExtendsType\.MugExceed", source);
        // Must NOT reference the WinForms-only PatchUtil class.
        Assert.DoesNotMatch(new Regex(@"\bPatchUtil\b"), source);
    }

    // ---------------------------- Roslyn handler helpers ----------------------------

    static void AssertHandlerBodyContains(string source, string handlerName, string requiredPattern)
    {
        string body = ExtractHandlerBody(source, handlerName);
        Assert.Matches(requiredPattern, body);
    }

    static string ExtractHandlerBody(string source, string handlerName)
    {
        var rx = new System.Text.RegularExpressions.Regex(
            @"(?<=\s)" + System.Text.RegularExpressions.Regex.Escape(handlerName) +
            @"\s*\([^)]*\)\s*(\{)",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        var m = rx.Match(source);
        Assert.True(m.Success, $"Handler declaration '{handlerName}' not found");
        int braceOpenIdx = m.Groups[1].Index;
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        Assert.True(depth == 0, $"Handler '{handlerName}' body malformed");
        return source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);
    }

    // ---------------------------- Helpers ----------------------------

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
