// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1 / Phase 5 gap-sweep regression tests for ImagePortraitFE6View. (#435)
//
// Covers the 28 gaps the issue called out:
//   - 21 missing WF-only labels (density + labels)
//   - 7 unwrapped ROM writes in ImagePortraitFE6ViewModel (undo)
//
// Plus the v2-plan undo-ownership contract enforced by Copilot CLI review
// (https://github.com/laqieer/FEBuilderGBA/issues/435#issuecomment-4522969432):
//   - ViewModel.Write(UndoService) owns the single Begin/Commit scope
//   - View.WriteButton_Click DELEGATES to the VM, does NOT open its own scope
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ImagePortraitFE6 parity raise (#435) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the original
/// gap-sweep report.
/// </summary>
public class ImagePortraitFE6ParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The 2026-05-22 density sweep reports WF=34, AV=22 (Δ=-35.3%, Medium).
    /// To leave HIGH we need AV ≥ 26 (75% of WF=34). The issue acceptance
    /// criterion is "MEDIUM verdict or better" so MEDIUM is acceptable, but
    /// this PR's design should keep us inside MEDIUM after the fix even if
    /// WF count is recalculated slightly.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitFE6View.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer count from the 2026-05-22 density sweep — see issue #435.
        const int WfControlCount = 34;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 26
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // Undo ownership contract (Copilot CLI review point 1) —
    // VM owns the single Begin/Commit scope, View delegates to VM.
    // -----------------------------------------------------------------

    /// <summary>
    /// ImagePortraitFE6ViewModel.Write(UndoService) must:
    ///   (a) call _undoService.Begin (parameter or local) exactly once,
    ///   (b) call _undoService.Commit exactly once,
    ///   (c) place every `rom.write_u*` call between Begin and Commit,
    ///   (d) include a Rollback path on exception.
    /// </summary>
    [Fact]
    public void ViewModel_Write_WrapsAllRomWritesInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string vmPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "ImagePortraitFE6ViewModel.cs");
        Assert.True(File.Exists(vmPath), $"ViewModel not found at {vmPath}");

        string source = File.ReadAllText(vmPath);

        // Find the Write(UndoService ...) method body.
        var sig = new Regex(@"public\s+void\s+Write\s*\(\s*UndoService\b[^)]*\)");
        var sigMatch = sig.Match(source);
        Assert.True(sigMatch.Success,
            "ImagePortraitFE6ViewModel.Write(UndoService) overload not found");

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

        // (c) every rom.write_* between Begin and Commit (textual order).
        var writeMatches = Regex.Matches(codeOnly, @"\brom\s*\.\s*write_u(?:8|16|32|p32|p16)\b");
        Assert.True(writeMatches.Count >= 7,
            $"Write(UndoService) must contain at least 7 rom.write_* calls (the 7 closed gaps), found {writeMatches.Count}");
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
            "ImagePortraitFE6View.axaml.cs");
        Assert.True(File.Exists(viewCsPath), $"View code-behind not found at {viewCsPath}");

        string source = File.ReadAllText(viewCsPath);

        int sigIdx = source.IndexOf("WriteButton_Click(", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0, "WriteButton_Click handler not found");

        int braceOpenIdx = source.IndexOf('{', sigIdx);
        Assert.True(braceOpenIdx > sigIdx, "WriteButton_Click has no body");
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        Assert.True(depth == 0, "WriteButton_Click body malformed (no matching `}`)");
        string body = source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);
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
    /// surfaced by this PR. Using AutomationIds (not just label Text) so the
    /// test survives renaming/translation churn.
    /// </summary>
    [Fact]
    public void View_HasExpectedFieldControls()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitFE6View.axaml");
        string xaml = File.ReadAllText(axamlPath);

        // Required AutomationIds for new controls.
        string[] required = new[]
        {
            // Top-of-list config bar
            "ImagePortraitFE6_ReadStartAddress_Label",
            "ImagePortraitFE6_ReadCount_Label",
            "ImagePortraitFE6_ReloadList_Button",
            // Selection bar
            "ImagePortraitFE6_BlockSize_Label",
            "ImagePortraitFE6_SelectedAddress_Label",
            "ImagePortraitFE6_Write_Button",
            // Editable fields
            "ImagePortraitFE6_MouthX_Input",
            "ImagePortraitFE6_MouthY_Input",
            "ImagePortraitFE6_Unused14_Input",
            "ImagePortraitFE6_Unused15_Input",
            // Show frame
            "ImagePortraitFE6_ShowFrame_Input",
            "ImagePortraitFE6_ShowFrame_Label",
            // Comment
            "ImagePortraitFE6_Comment_Input",
            // Source file controls (visible when ResourceCache has an entry)
            "ImagePortraitFE6_OpenSource_Button",
            "ImagePortraitFE6_SelectSource_Button",
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
            "ImagePortraitFE6ViewModel.cs");
        string source = File.ReadAllText(vmPath);

        // Must have a `public void Write()` (zero-arg) method.
        Assert.Matches(@"public\s+void\s+Write\s*\(\s*\)", source);
    }

    /// <summary>
    /// New read-only display properties expected on the VM for the top-of-list
    /// config bar and selection bar (BlockSize / ReadStartAddress / ReadCount).
    /// </summary>
    [Fact]
    public void ViewModel_ExposesReadOnlyDisplayProperties()
    {
        var vm = new ImagePortraitFE6ViewModel();
        // These properties should compile-time exist (test compile guards them).
        _ = vm.BlockSize;
        _ = vm.ReadStartAddress;
        _ = vm.ReadCount;
    }

    /// <summary>
    /// Comment field should be a simple round-trippable string (no ROM write
    /// — the WF Comment field is a Resource Cache lookup, not a ROM field).
    /// </summary>
    [Fact]
    public void ViewModel_HasCommentProperty()
    {
        var vm = new ImagePortraitFE6ViewModel();
        vm.Comment = "test comment";
        Assert.Equal("test comment", vm.Comment);
    }

    /// <summary>
    /// Comment caching must use <see cref="CoreState.CommentCache"/> keyed by
    /// the current ROM address — the same EtcCache instance the WinForms
    /// <c>InputFormRef</c> wires `Program.CommentCache.At(addr)` /
    /// `Update(addr, text)` against. (Copilot CLI PR #504 review point 1.)
    /// </summary>
    [Fact]
    public void View_CommentHandlers_UseCommentCacheKeyedByAddress()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitFE6View.axaml.cs");
        string source = File.ReadAllText(viewCsPath);

        // LoadCommentForCurrentEntry must read CoreState.CommentCache.TryGetValue(addr, ...)
        AssertHandlerBodyContains(source, "LoadCommentForCurrentEntry",
            @"CoreState\.CommentCache");
        AssertHandlerBodyContains(source, "LoadCommentForCurrentEntry",
            @"\.TryGetValue\s*\(\s*addr");
        // And must NOT use the prior "PortraitFE6Comment_" custom key.
        AssertHandlerBodyDoesNotContain(source, "LoadCommentForCurrentEntry",
            @"PortraitFE6Comment_");

        // Comment_TextChanged must write CoreState.CommentCache.Update(addr, …)
        AssertHandlerBodyContains(source, "Comment_TextChanged",
            @"CoreState\.CommentCache");
        AssertHandlerBodyContains(source, "Comment_TextChanged",
            @"\.Update\s*\(\s*addr");
        AssertHandlerBodyDoesNotContain(source, "Comment_TextChanged",
            @"PortraitFE6Comment_");
    }

    /// <summary>
    /// The filter row visible above the entry list MUST be either removed
    /// (AddressListControl ships its own SearchBox + Find button) OR wired
    /// to a real filter handler. Inert UI affordances are rejected.
    /// (Copilot CLI PR #504 review point 2.)
    /// </summary>
    [Fact]
    public void View_DoesNotIntroduceInertFilterRow()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitFE6View.axaml");
        string xaml = File.ReadAllText(axamlPath);
        // The earlier draft introduced `LabelFilterInput` / `LabelFilterLabel`
        // — those AutomationIds must not be present (AddressListControl
        // already provides the search box).
        Assert.DoesNotContain("ImagePortraitFE6_LabelFilter_Input", xaml);
        Assert.DoesNotContain("ImagePortraitFE6_LabelFilter_Label", xaml);
    }

    /// <summary>
    /// `UpdateShowFrameLabel` must wrap the user-visible string in
    /// <see cref="R._(string, object[])"/> so it localizes — assignments
    /// after <c>TranslatedWindow.TranslateAll()</c> need explicit translation
    /// (Copilot bot PR #504 review point on .axaml.cs:97).
    /// </summary>
    [Fact]
    public void View_UpdateShowFrameLabel_WrapsInRUnderscore()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitFE6View.axaml.cs");
        string source = File.ReadAllText(viewCsPath);
        AssertHandlerBodyContains(source, "UpdateShowFrameLabel",
            @"ShowFrameLabel\.Text\s*=\s*R\._\s*\(");
    }

    /// <summary>
    /// The PNG-import body must record the source file path to
    /// <see cref="CoreState.ResourceCache"/> after a successful import so
    /// the Open / Select Source File buttons surface afterwards.
    /// (Copilot bot PR #504 review point on .axaml.cs:177.)
    ///
    /// #1397: the import body was extracted into the shared
    /// <c>ImportImageFromFile</c> method (reused by both the file-picker
    /// <c>ImportPng_Click</c> and the new FE-Repo button), so the source-path
    /// recording now lives there — exactly one import path.
    /// </summary>
    [Fact]
    public void View_ImportPng_RecordsSourcePathToResourceCache()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ImagePortraitFE6View.axaml.cs");
        string source = File.ReadAllText(viewCsPath);
        AssertHandlerBodyContains(source, "ImportImageFromFile",
            @"loadResult\.SourcePath");
        AssertHandlerBodyContains(source, "ImportImageFromFile",
            @"cache\.Update\s*\(\s*srcKey");
    }

    // ---------------------------- Roslyn handler helpers ----------------------------

    static void AssertHandlerBodyContains(string source, string handlerName, string requiredPattern)
    {
        string body = ExtractHandlerBody(source, handlerName);
        Assert.Matches(requiredPattern, body);
    }

    static void AssertHandlerBodyDoesNotContain(string source, string handlerName, string forbiddenPattern)
    {
        string body = ExtractHandlerBody(source, handlerName);
        Assert.DoesNotMatch(forbiddenPattern, body);
    }

    static string ExtractHandlerBody(string source, string handlerName)
    {
        // Find the method DECLARATION, not the call site. A declaration ends
        // its parameter list with `)` followed by optional whitespace and `{`,
        // while a call ends with `)` and `;` or `)` and `,` or `)` and ` =>`.
        // Use a regex that allows the multiline method body to follow.
        // Pattern: <space|tab><handlerName>(...)<ws>{
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

    /// <summary>
    /// Walk parent directories from the test bin/ folder until we find the
    /// repo root (identified by FEBuilderGBA.sln).
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
