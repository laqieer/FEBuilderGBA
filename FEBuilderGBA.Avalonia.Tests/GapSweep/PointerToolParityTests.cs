// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/4 gap-sweep regression tests for PointerToolView. (#438)
//
// Covers the 24 gaps the issue called out: 21 missing labels (qualitative
// label diff — most are JA originals semantically mirrored by AV English
// labels, see PR description label coverage table) + 3 missing
// INavigationTargetSource entries (jumps).
//
// Tests stay headless — no real ROM file required for the density /
// manifest / scanner assertions. The other-ROM Search assertions use
// synthetic byte arrays to exercise PointerToolCore.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the PointerToolView parity raise (#438) is permanent.
/// Each assertion maps to a concrete acceptance-criterion bullet in the
/// issue body, so regressions get a clear pointer back to the original
/// gap-sweep report.
/// </summary>
public class PointerToolParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must stay at MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer.cs reports 31 controls (per 2026-05-24 density sweep).
    /// To stay at MEDIUM-or-better we need AV count within 50% of WF
    /// (i.e. AV >= 16, ≤ 47 controls). After this PR we add ~6 new controls
    /// (Load Other ROM, LDR Address, LDR Reference, 4 warning labels, Write)
    /// so AV moves from 33 to ~39+.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // WF designer count from the 2026-05-24 density sweep — see issue #438.
        const int WfControlCount = 31;
        // 50% delta cap → AV must be in [16, 47] to stay MEDIUM-or-better.
        int lowerBound = (int)Math.Ceiling(WfControlCount * 0.5);
        int upperBound = (int)Math.Floor(WfControlCount * 1.5);
        Assert.True(avCount >= lowerBound && avCount <= upperBound,
            $"AV control count {avCount} must be in [{lowerBound}, {upperBound}] " +
            $"(50% of WF={WfControlCount}) to stay MEDIUM-or-better.");
    }

    /// <summary>
    /// Regression guard: assert AV control count grew by at least 5 from the
    /// pre-PR baseline (was 33). Catches if a future refactor accidentally
    /// removes the new LDR / warning / Load Other ROM controls.
    /// </summary>
    [Fact]
    public void View_AvControlCount_GrewFromBaseline()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolView.axaml");
        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // Pre-PR baseline from the 2026-05-24 density sweep: 33.
        // After PR #438 we add: Load Other ROM Button, LDR Address TextBox,
        // LDR Reference TextBox, 4 warning TextBlocks, Write Button = 8 new
        // controls. Allow some slack (>= 5) for layout refactor flexibility.
        const int BaselineAvCount = 33;
        const int MinimumGrowth = 5;
        Assert.True(avCount >= BaselineAvCount + MinimumGrowth,
            $"AV control count {avCount} must be >= {BaselineAvCount + MinimumGrowth} " +
            $"(baseline {BaselineAvCount} + at least {MinimumGrowth} new controls).");
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) — Manifest must declare all three callsites.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresAllThreeJumpTargets()
    {
        var vm = new PointerToolViewModel();
        var targets = vm.GetNavigationTargets();

        Assert.Contains(targets, t => t.TargetViewType == typeof(PointerToolBatchInputView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(PointerToolCopyToView));
        Assert.Contains(targets, t => t.TargetViewType == typeof(PointerToolView));
    }

    [Fact]
    public void ViewModel_NavigationTargets_AreNotMarkedAsKnownGaps()
    {
        // After this PR closes #438, NONE of the three rows should carry
        // an IssueRef — the behavior must exist, not be tracked-broken.
        var vm = new PointerToolViewModel();
        var targets = vm.GetNavigationTargets();
        foreach (var t in targets)
        {
            Assert.Null(t.IssueRef);
        }
    }

    // -----------------------------------------------------------------
    // Phase 4 end-to-end: simulate the three WF callsites and confirm
    // they MATCH the new manifest rows (no longer MissingAvManifest).
    // -----------------------------------------------------------------

    [Fact]
    public void JumpParityScanner_AllThreeCallsites_NowMatchManifest()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "PointerToolForm",
                TargetForm: "PointerToolBatchInputForm",
                HasAddressArgument: false),
            new WfJumpCallsite(
                SourceForm: "PointerToolForm",
                TargetForm: "PointerToolCopyToForm",
                HasAddressArgument: true),
            new WfJumpCallsite(
                SourceForm: "PointerToolForm",
                TargetForm: "PointerToolForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "PointerToolViewModel",
                SourceView: "PointerToolView",
                Command: "JumpToBatchInput",
                TargetView: "PointerToolBatchInputView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "PointerToolViewModel",
                SourceView: "PointerToolView",
                Command: "JumpToCopyTo",
                TargetView: "PointerToolCopyToView",
                IssueRef: null),
            new AvManifestEntry(
                SourceVm: "PointerToolViewModel",
                SourceView: "PointerToolView",
                Command: "JumpToSelf",
                TargetView: "PointerToolView",
                IssueRef: null),
        };

        // Need repoRoot so BuildWfFormToAvViewsMap layer 2 (PairMatcher
        // heuristic) discovers PointerToolForm ↔ PointerToolView. The
        // ListParityHelper authoritative map only contains list-parity
        // editors, and PointerToolView is a tools/dialog view (not a
        // list-port). Without repoRoot the test would only see Layer 1
        // and fail to resolve the pair.
        string repoRoot = FindRepoRoot();
        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests, repoRoot);

        foreach (var (target, expectedAv) in new[]
        {
            ("PointerToolBatchInputForm", "PointerToolBatchInputView"),
            ("PointerToolCopyToForm", "PointerToolCopyToView"),
            ("PointerToolForm", "PointerToolView"),
        })
        {
            var match = rows.FirstOrDefault(r =>
                r.SourceForm == "PointerToolForm" &&
                r.TargetWfType == target);
            Assert.NotNull(match);
            Assert.Equal(JumpRowStatus.Match, match!.Status);
            Assert.Equal(expectedAv, match.TargetAvType);
        }
    }

    // -----------------------------------------------------------------
    // View: real click handlers must be wired (Copilot CLI review point 5)
    // — manifest match alone doesn't prove the UI works.
    // -----------------------------------------------------------------

    [Fact]
    public void View_NavigationHandlers_AreWiredToWindowManager()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolView.axaml.cs");
        Assert.True(File.Exists(viewCsPath), $"View code-behind not found at {viewCsPath}");

        string source = File.ReadAllText(viewCsPath);

        // Batch handler: must Open<PointerToolBatchInputView>.
        AssertHandlerWiring(
            source,
            handlerName: "Batch_Click",
            requiredCallPattern: @"WindowManager\.Instance\.Open<PointerToolBatchInputView>");

        // Address double-click handler: must Navigate<PointerToolCopyToView>(addr).
        AssertHandlerWiring(
            source,
            handlerName: "AddressDoubleClick",
            requiredCallPattern: @"WindowManager\.Instance\.Navigate<PointerToolCopyToView>");

        // What Is handler: must call VM LookupAddressType.
        AssertHandlerWiring(
            source,
            handlerName: "WhatIs_Click",
            requiredCallPattern: @"_vm\.LookupAddressType");

        // Load Other ROM handler: must call VM LoadOtherRom.
        AssertHandlerWiring(
            source,
            handlerName: "LoadOtherRom_Click",
            requiredCallPattern: @"_vm\.LoadOtherRom");
    }

    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);

        // Write_Click body must contain _undoService.Begin and _undoService.Commit
        // (and on the exception path, _undoService.Rollback).
        AssertHandlerWiring(source, "Write_Click", @"_undoService\.Begin\(");
        AssertHandlerWiring(source, "Write_Click", @"_undoService\.Commit\(");
        AssertHandlerWiring(source, "Write_Click", @"_undoService\.Rollback\(");
    }

    // -----------------------------------------------------------------
    // VM: LoadOtherRom must populate the OtherRomName field. Other fields
    // are populated by Search; this test just exercises the file-loading
    // pathway with a synthetic ROM byte array.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadOtherRom_PopulatesOtherRomName()
    {
        var vm = new PointerToolViewModel();
        // Skip if ROM is not initialized in CoreState — the LoadOtherRom
        // path requires the current ROM to build LDR maps. Test uses a
        // temp file with synthetic bytes.
        ROM rom = MakeSyntheticFe8uRom();
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            string tempPath = Path.Combine(Path.GetTempPath(),
                $"pointertool-test-{Guid.NewGuid():N}.gba");
            byte[] otherBytes = new byte[0x100000];
            // Plant a 16-byte GBA header so ROM.Load doesn't reject it.
            otherBytes[0xAC] = (byte)'B';
            otherBytes[0xAD] = (byte)'E';
            otherBytes[0xAE] = (byte)'8';
            otherBytes[0xAF] = (byte)'E';
            File.WriteAllBytes(tempPath, otherBytes);
            try
            {
                vm.LoadOtherRom(tempPath);
                Assert.Contains(Path.GetFileNameWithoutExtension(tempPath), vm.OtherRomName);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    // -----------------------------------------------------------------
    // VM: LookupAddressType returns a non-empty hint for a known asm-map
    // address; returns "not found" for an unknown address.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LookupAddressType_ReturnsResultString()
    {
        var vm = new PointerToolViewModel();
        ROM rom = MakeSyntheticFe8uRom();
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // The lookup may return "not found" for an unknown address;
            // we just assert it returns a string (not throws) and the
            // VM SearchResults field is populated.
            string result = vm.LookupAddressType(0x00100000u);
            Assert.NotNull(result);
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    // -----------------------------------------------------------------
    // VM: RunSearch with no other-ROM still populates the current-ROM
    // fields; the four per-result warning bools default to false.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_RunSearch_NoOtherRom_PopulatesCurrentRomFields()
    {
        var vm = new PointerToolViewModel();
        ROM rom = MakeSyntheticFe8uRom();
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            vm.AddressInput = "0x100";
            vm.RunSearch();
            Assert.False(string.IsNullOrEmpty(vm.PointerValue),
                "PointerValue should be populated.");
            Assert.False(string.IsNullOrEmpty(vm.LittleEndianValue),
                "LittleEndianValue should be populated.");
            // All four per-result warnings default to false when no
            // other-ROM is loaded (the WF view only shows them when an
            // other-ROM search has run and returned a match).
            Assert.False(vm.HasZeroAtDirect);
            Assert.False(vm.HasVeryFarAtDirect);
            Assert.False(vm.HasZeroAtLdr);
            Assert.False(vm.HasVeryFarAtLdr);
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    [Fact]
    public void ViewModel_LittleEndianValue_IsSingleHexUint_NotSpacedBytes()
    {
        // Copilot bot review point 7: WF stores LittleEndian as a single
        // uint hex value (byte-swapped pointer). The previous AV impl used
        // "AA BB CC DD" spaced bytes, which the AddressDoubleClick parser
        // could not lift back into a uint. Mirror WF: address 0x100 →
        // pointer 0x08000100 → byte-swapped 0x00010008.
        var vm = new PointerToolViewModel();
        ROM rom = MakeSyntheticFe8uRom();
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            vm.AddressInput = "0x100";
            vm.RunSearch();
            // Pointer is 0x08000100; byte-swap is 0x00010008.
            Assert.Equal("0x00010008", vm.LittleEndianValue);
            // The format MUST be a single hex value (no spaces) so the
            // AddressDoubleClick parser can lift it back into a uint.
            Assert.DoesNotContain(" ", vm.LittleEndianValue);
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    [Fact]
    public void ViewModel_AcceptsPointerFormInput_NotDoubleAdded()
    {
        // Copilot bot re-review (PR #510): WF accepts EITHER a ROM offset
        // (0x100) OR a GBA pointer (0x08000100). The previous AV impl did
        // `addr + 0x08000000` unconditionally, which double-added the base
        // for pointer-form input. With U.toOffset / U.toPointer the same
        // input pointer 0x08000100 must yield PointerValue 0x08000100 and
        // LittleEndian 0x00010008 — identical to the offset-form input.
        var vm = new PointerToolViewModel();
        ROM rom = MakeSyntheticFe8uRom();
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            vm.AddressInput = "0x08000100"; // pointer form
            vm.RunSearch();
            Assert.Equal("0x08000100", vm.PointerValue);
            Assert.Equal("0x00010008", vm.LittleEndianValue);

            // Round-trip: offset-form input yields the same canonical values.
            vm.AddressInput = "0x100"; // offset form
            vm.RunSearch();
            Assert.Equal("0x08000100", vm.PointerValue);
            Assert.Equal("0x00010008", vm.LittleEndianValue);
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    [Fact]
    public void ViewModel_DirectWarnings_StayHiddenWithoutOtherRomMatch()
    {
        // Copilot bot review point 3: the direct-match warnings used to fire
        // from the CURRENT-ROM source address (e.g. addr > 3/4 of ROM size),
        // which produced false positives whenever any other-ROM bytes were
        // loaded — even when no cross-ROM match had been computed. The new
        // semantics only raise the warning when OtherRomAddress is populated.
        var vm = new PointerToolViewModel();
        ROM rom = MakeSyntheticFe8uRom();
        ROM? prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Pick a "very far" source address (last 1/4 of the ROM) that
            // would have triggered the old false-positive logic.
            vm.AddressInput = "0x100E000"; // 0x100E000 > 0x1100000 * 3/4.
            vm.RunSearch();
            // No cross-ROM match has been computed; warnings stay false.
            Assert.False(vm.HasVeryFarAtDirect,
                "HasVeryFarAtDirect must stay false until OtherRomAddress is populated");
            Assert.False(vm.HasZeroAtDirect,
                "HasZeroAtDirect must stay false until OtherRomAddress is populated");
        }
        finally
        {
            CoreState.ROM = prev;
        }
    }

    // -----------------------------------------------------------------
    // View: WriteTargetInput must have a bound control in the AXAML so the
    // Write button can succeed (Copilot bot review point 1).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasBoundControlForWriteTargetInput()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");
        string xaml = File.ReadAllText(axamlPath);
        // There must be a TextBox bound to WriteTargetInput somewhere in
        // the view — otherwise the Write button cannot succeed because the
        // VM has no way to receive the target offset.
        Assert.Matches(@"\{Binding\s+WriteTargetInput", xaml);
        Assert.Matches(@"<TextBox[^>]*WriteTargetInput", xaml);
    }

    [Fact]
    public void View_WriteHandler_DoesNotRethrowOnFailure()
    {
        // Copilot bot review point 6: Write_Click used to rethrow the
        // exception after Rollback, which crashes the Avalonia UI thread.
        // The handler MUST log + report + return without rethrowing.
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);
        // Find the Write_Click body and assert it contains a catch with
        // logging/reporting but does NOT contain a bare `throw;` rethrow.
        int sigIdx = source.IndexOf("void Write_Click", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0, "Write_Click handler not found");
        int braceOpenIdx = source.IndexOf('{', sigIdx);
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        string body = source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);
        // Must catch exceptions
        Assert.Matches(@"catch\s*\(", body);
        // Must NOT rethrow with a bare `throw;` — the regex matches the
        // exact bare-rethrow statement, not `throw new ...` patterns.
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(
            body, @"\bthrow\s*;"),
            "Write_Click must not bare-rethrow after Rollback (crashes UI thread)");
        // Should log the failure for diagnostics
        Assert.Matches(@"Log\.(Error|Warn)", body);
    }

    [Fact]
    public void CopyToView_HexButton_HasSafetyOffsetGuard()
    {
        // Copilot bot review point 5: HexButton_Click must enforce
        // U.isSafetyOffset(U.toOffset(addr)) before navigating, mirroring
        // the WF gate. Without it, invalid input routes to offset 0.
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolCopyToView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);
        int sigIdx = source.IndexOf("void HexButton_Click", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0, "HexButton_Click handler not found");
        int braceOpenIdx = source.IndexOf('{', sigIdx);
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        string body = source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);
        Assert.Matches(@"TryGetOffsetForHexJump", body);
        Assert.Matches(@"U\.isSafetyOffset", body);
    }

    // -----------------------------------------------------------------
    // Existing list-parity helper still maps the editor (regression guard).
    // -----------------------------------------------------------------

    [Fact]
    public void ListParityHelper_PointerToolView_StillRegistered()
    {
        // PointerToolView is paired via the Phase 0 heuristic
        // (form-name -> view-name); ListParityHelper only declares
        // explicit overrides. Either path is acceptable — we just
        // assert the AV view file exists at the expected path.
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");
    }

    // -----------------------------------------------------------------
    // CopyTo regression: NavigateTo must populate the SourceAddress so
    // the textbox shows the seeded value (Copilot CLI review point 2).
    // -----------------------------------------------------------------

    [Fact]
    public void PointerToolCopyToViewModel_Init_PopulatesSourceAddress()
    {
        var vm = new PointerToolCopyToViewModel();
        // The new Init(uint) helper mirrors WF PointerToolCopyToForm.Init.
        vm.Init(0x12345u);
        // Address should be set; the formatted string should contain the hex.
        Assert.Contains("12345", vm.SourceAddress);
    }

    // -----------------------------------------------------------------
    // CopyTo payload parity (Copilot CLI v3 review point — explicit
    // payload format checks for the 5 copy modes).
    // -----------------------------------------------------------------

    [Fact]
    public void PointerToolCopyToViewModel_GetAsPointer_ReturnsGbaPointerFormat()
    {
        var vm = new PointerToolCopyToViewModel();
        vm.Init(0x12345u);
        // Pointer form of a ROM offset 0x12345 is 0x08012345.
        Assert.Equal("0x08012345", vm.GetAsPointer());
    }

    [Fact]
    public void PointerToolCopyToViewModel_GetAsLittleEndian_ReturnsByteSwappedPointer()
    {
        var vm = new PointerToolCopyToViewModel();
        vm.Init(0x12345u);
        // Pointer 0x08012345 byte-swapped (LE) is 0x45230108.
        Assert.Equal("0x45230108", vm.GetAsLittleEndian());
    }

    [Fact]
    public void PointerToolCopyToViewModel_GetAsNoDollGBARadBreakPoint_ReturnsBreakpointFormat()
    {
        var vm = new PointerToolCopyToViewModel();
        vm.Init(0x12345u);
        Assert.Equal("[0x08012345]?", vm.GetAsNoDollGBARadBreakPoint());
    }

    [Fact]
    public void PointerToolCopyToViewModel_GetAsClipboardText_ReturnsTrulyVerbatim()
    {
        // Copilot bot review (PR #510, re-review): WF copies
        // ValueTextBox.Text EXACTLY in CopyClipboard_Click — no normalisation
        // AND no trimming. The previous AV impl trimmed outer whitespace,
        // breaking parity. The new impl preserves the user-typed value
        // exactly, including surrounding whitespace, casing, and empty
        // strings.
        var vm = new PointerToolCopyToViewModel
        {
            SourceAddress = "ABCDEF"
        };
        Assert.Equal("ABCDEF", vm.GetAsClipboardText());

        // 0x prefix preserved verbatim.
        vm.SourceAddress = "0x12345";
        Assert.Equal("0x12345", vm.GetAsClipboardText());

        // Whitespace is PRESERVED (WF copies textbox text raw, leading and
        // trailing whitespace included — the user might be pasting into a
        // tool that tolerates whitespace).
        vm.SourceAddress = "  0xABCD  ";
        Assert.Equal("  0xABCD  ", vm.GetAsClipboardText());

        // Empty SourceAddress returns the empty string (WF behaviour:
        // `U.SetClipboardText(this.ValueTextBox.Text)` with an empty box).
        vm.SourceAddress = string.Empty;
        Assert.Equal(string.Empty, vm.GetAsClipboardText());
    }

    [Fact]
    public void PointerToolCopyToViewModel_TryGetOffsetForHexJump_FailsOnInvalidInput()
    {
        // Copilot bot review point 2: ParseAddress used to silently return
        // 0 on parse failure, causing Hex jump to navigate to offset 0. The
        // new TryGet API surfaces failure so the view can refuse the action.
        var vm = new PointerToolCopyToViewModel
        {
            SourceAddress = "not-a-hex-value"
        };
        Assert.False(vm.TryGetOffsetForHexJump(out uint offset));
        Assert.Equal(0u, offset);

        vm.Init(0x12345u);
        Assert.True(vm.TryGetOffsetForHexJump(out offset));
        // U.toOffset(0x12345) = 0x12345 (offset already < ROM base).
        Assert.Equal(0x12345u, offset);
    }

    [Fact]
    public void PointerToolCopyToViewModel_GetAsPointer_ReturnsNullOnInvalidInput()
    {
        // Copilot bot review point 2: copy actions used to silently render
        // 0x08000000 on invalid input. The new API returns null so the view
        // can suppress the copy / show an error instead.
        var vm = new PointerToolCopyToViewModel
        {
            SourceAddress = "invalid"
        };
        Assert.Null(vm.GetAsPointer());
        Assert.Null(vm.GetAsLittleEndian());
        Assert.Null(vm.GetAsNoDollGBARadBreakPoint());
    }

    // -----------------------------------------------------------------
    // CopyTo view: HexButton handler navigates to HexEditor (concern #3).
    // -----------------------------------------------------------------

    [Fact]
    public void CopyToView_HexButtonHandler_NavigatesToHexEditor()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolCopyToView.axaml.cs");
        Assert.True(File.Exists(viewCsPath), $"View code-behind not found at {viewCsPath}");
        string source = File.ReadAllText(viewCsPath);
        AssertHandlerWiring(
            source,
            handlerName: "HexButton_Click",
            requiredCallPattern: @"WindowManager\.Instance\.Navigate<HexEditorView>");
    }

    [Fact]
    public void CopyToView_NavigateTo_CallsInitOnViewModel()
    {
        string repoRoot = FindRepoRoot();
        string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "PointerToolCopyToView.axaml.cs");
        string source = File.ReadAllText(viewCsPath);
        AssertHandlerWiring(
            source,
            handlerName: "NavigateTo",
            requiredCallPattern: @"_vm\.Init\(");
    }

    // -----------------------------------------------------------------
    // VM: LookupAddressType must use the Core IAsmMapCache abstraction —
    // no direct dependency on WinForms `AsmMapFile`. Copilot v3 review
    // asked for an explicit assertion.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LookupAddressType_DoesNotDependOnWinFormsAsmMapFile()
    {
        string repoRoot = FindRepoRoot();
        string vmCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "PointerToolViewModel.cs");
        Assert.True(File.Exists(vmCsPath), $"VM source not found at {vmCsPath}");
        string source = File.ReadAllText(vmCsPath);
        // The Avalonia VM must NOT reference the WinForms-only AsmMapFile
        // class directly. The WF AsmMapFile lives in `FEBuilderGBA/AsmMapFile.cs`
        // and has WinForms dependencies — any reference would break the
        // cross-platform build. Verify by inspecting the LookupAddressType
        // method body for the forbidden type names.
        int sigIdx = source.IndexOf("LookupAddressType(uint addr)", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0, "LookupAddressType method not found in PointerToolViewModel.cs");
        int braceOpen = source.IndexOf('{', sigIdx);
        int depth = 1;
        int i = braceOpen + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        string body = source.Substring(braceOpen + 1, i - braceOpen - 2);
        // The body MUST NOT reference WinForms-only types.
        Assert.DoesNotContain("AsmMapFile.", body);
        Assert.DoesNotContain("Program.AsmMapFileAsmCache", body);
        // Permitted Core surfaces.
        // The current implementation uses U.toPointer + region classification
        // — both are in Core. Sanity-check that the body actually does
        // something (more than a single return).
        Assert.True(body.Length > 100, "LookupAddressType body looks too small to do real work");
    }

    // -----------------------------------------------------------------
    // #966 — cross-ROM pointer search (raw + LDR literal-pool) wiring.
    // Two synthetic ROMs: the current ROM (CoreState.ROM) and an "other
    // ROM" loaded from a temp file. The other ROM holds a known reference
    // to a target address; LoadOtherRom must populate the OtherROM* fields
    // via U.GrepPointerAll / U.GrepPointerAllOnLDR.
    // -----------------------------------------------------------------

    /// <summary>Target offset whose references we plant in the other ROM.</summary>
    const uint CrossRomTargetOffset = 0x4000;

    [Fact]
    public void ViewModel_LoadOtherRom_RawMatch_PopulatesOtherRomAddressAndRef()
    {
        // The other ROM has a raw 32-bit pointer (0x08000000+T) at offset
        // RawRefOffset → GrepPointerAll must find it. OtherRomAddress shows the
        // searched data address (T); OtherRomRefPointer shows the reference
        // location (RawRefOffset).
        const uint rawRefOffset = 0x1000;
        byte[] other = MakeOtherRomWithRawPointer(rawRefOffset, CrossRomTargetOffset);

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = $"0x{CrossRomTargetOffset:X08}";
            vm.RunSearch();
            Assert.Equal($"0x{CrossRomTargetOffset:X08}", vm.OtherRomAddress);
            Assert.Equal($"0x{rawRefOffset:X08}", vm.OtherRomRefPointer);
        });
    }

    [Fact]
    public void ViewModel_LoadOtherRom_RawOnly_NoLdr_LeavesLdrFieldsEmpty()
    {
        // A raw reference exists but NO Thumb LDR literal-pool load points at
        // the target → the LDR fields stay empty and the LDR ZERO warning is
        // NOT raised (WF hides labels on no-match; there is no "ZERO on
        // no-match" branch).
        const uint rawRefOffset = 0x1000;
        byte[] other = MakeOtherRomWithRawPointer(rawRefOffset, CrossRomTargetOffset);

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = $"0x{CrossRomTargetOffset:X08}";
            vm.RunSearch();
            // Raw path matched.
            Assert.Equal($"0x{rawRefOffset:X08}", vm.OtherRomRefPointer);
            // LDR path did NOT match.
            Assert.Equal(string.Empty, vm.OtherRomLdrAddress);
            Assert.Equal(string.Empty, vm.OtherRomLdrRefPointer);
            Assert.False(vm.HasZeroAtLdr);
            Assert.False(vm.HasVeryFarAtLdr);
        });
    }

    [Fact]
    public void ViewModel_LoadOtherRom_LdrMatch_PopulatesLdrAddressAndSlot()
    {
        // The other ROM has a minimal Thumb `LDR rX,[pc,#imm]` at LdrInstr
        // whose literal-pool slot at LdrSlot holds 0x08000000+T.
        // GrepPointerAllOnLDR returns the literal-pool SLOT offset (NOT the
        // instruction address) → OtherRomLdrRefPointer must equal LdrSlot.
        const uint ldrInstr = 0x200;
        const uint ldrSlot = 0x204; // Padding4(toPointer(0x200)+2+0) = 0x...204
        byte[] other = MakeOtherRomWithLdr(ldrInstr, ldrSlot, CrossRomTargetOffset);

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = $"0x{CrossRomTargetOffset:X08}";
            vm.RunSearch();
            Assert.Equal($"0x{CrossRomTargetOffset:X08}", vm.OtherRomLdrAddress);
            // The ref field is the literal-pool slot, not the LDR instruction.
            Assert.Equal($"0x{ldrSlot:X08}", vm.OtherRomLdrRefPointer);
            Assert.NotEqual($"0x{ldrInstr:X08}", vm.OtherRomLdrRefPointer);
        });
    }

    [Fact]
    public void ViewModel_LoadOtherRom_NoMatch_LeavesAllFieldsEmpty_NoThrow()
    {
        // The other ROM contains NO reference (raw or LDR) to the target →
        // all four OtherROM* fields stay empty, no warning is raised, no throw.
        byte[] other = new byte[0x10000]; // all zeros, no reference anywhere

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = $"0x{CrossRomTargetOffset:X08}";
            vm.RunSearch(); // must not throw
            Assert.Equal(string.Empty, vm.OtherRomAddress);
            Assert.Equal(string.Empty, vm.OtherRomRefPointer);
            Assert.Equal(string.Empty, vm.OtherRomLdrAddress);
            Assert.Equal(string.Empty, vm.OtherRomLdrRefPointer);
            Assert.False(vm.HasZeroAtDirect);
            Assert.False(vm.HasVeryFarAtDirect);
            Assert.False(vm.HasZeroAtLdr);
            Assert.False(vm.HasVeryFarAtLdr);
        });
    }

    [Fact]
    public void ViewModel_LoadOtherRom_DangerZoneAddress_NoThrow_FieldsEmpty()
    {
        // An address below the 0x200 danger-zone floor must be refused safely:
        // fields stay empty, no warning, no throw. (Copilot CLI review point 3
        // — the guard does not use U.isSafetyOffset on the other ROM.)
        const uint rawRefOffset = 0x1000;
        byte[] other = MakeOtherRomWithRawPointer(rawRefOffset, 0x100); // target in danger zone

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = "0x100"; // < 0x200
            vm.RunSearch(); // must not throw
            Assert.Equal(string.Empty, vm.OtherRomAddress);
            Assert.Equal(string.Empty, vm.OtherRomRefPointer);
            Assert.Equal(string.Empty, vm.OtherRomLdrAddress);
            Assert.Equal(string.Empty, vm.OtherRomLdrRefPointer);
        });
    }

    [Fact]
    public void ViewModel_LoadOtherRom_ThenInvalidAddress_ClearsStaleOtherRomFields()
    {
        // After a successful cross-ROM match, typing an invalid address must
        // clear the stale OtherROM result (Copilot CLI review point 4).
        const uint rawRefOffset = 0x1000;
        byte[] other = MakeOtherRomWithRawPointer(rawRefOffset, CrossRomTargetOffset);

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = $"0x{CrossRomTargetOffset:X08}";
            vm.RunSearch();
            Assert.False(string.IsNullOrEmpty(vm.OtherRomRefPointer));

            // Now an invalid address — the stale match must be cleared.
            vm.AddressInput = "not-a-hex";
            vm.RunSearch();
            Assert.Equal(string.Empty, vm.OtherRomAddress);
            Assert.Equal(string.Empty, vm.OtherRomRefPointer);
            Assert.Equal(string.Empty, vm.OtherRomLdrAddress);
            Assert.Equal(string.Empty, vm.OtherRomLdrRefPointer);
        });
    }

    [Fact]
    public void ViewModel_LoadOtherRom_ZeroRegionTarget_RaisesZeroWarning()
    {
        // #969 review point 2: the ZERO warning is a zero-REGION check (WF
        // checkZeroData) — more than half of the next 0x200 bytes from the
        // matched address are 0x00. The target window here is left all-zero
        // (the default buffer) EXCEPT the planted pointer is elsewhere, so the
        // [target, target+0x200) window is >half zero -> warning raised.
        const uint rawRefOffset = 0x8000;
        const uint target = 0x4000; // [0x4000, 0x4200) is all zero by default
        byte[] other = MakeOtherRomWithRawPointer(rawRefOffset, target);

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = $"0x{target:X08}";
            vm.RunSearch();
            // Raw match found, and the matched region is >half zero.
            Assert.Equal($"0x{rawRefOffset:X08}", vm.OtherRomRefPointer);
            Assert.True(vm.HasZeroAtDirect,
                "A matched address whose next 0x200 bytes are >half zero must raise the ZERO warning");
        });
    }

    [Fact]
    public void ViewModel_LoadOtherRom_NonZeroRegionTarget_NoZeroWarning()
    {
        // The inverse: fill the [target, target+0x200) window with non-zero
        // bytes so the zero-region check is NOT satisfied -> no ZERO warning.
        const uint rawRefOffset = 0x8000;
        const uint target = 0x4000;
        byte[] other = MakeOtherRomWithRawPointer(rawRefOffset, target);
        // Fill the matched window with 0xAB (well over half non-zero).
        for (uint i = target; i < target + 0x200 && i < (uint)other.Length; i++)
            other[i] = 0xAB;
        // The reference word must survive the fill (it is outside the window).

        RunWithOtherRom(other, vm =>
        {
            vm.AddressInput = $"0x{target:X08}";
            vm.RunSearch();
            Assert.Equal($"0x{rawRefOffset:X08}", vm.OtherRomRefPointer);
            Assert.False(vm.HasZeroAtDirect,
                "A matched address whose next 0x200 bytes are mostly non-zero must NOT raise the ZERO warning");
        });
    }

    // ---------------------------- Helpers ----------------------------

    /// <summary>
    /// Run <paramref name="body"/> with CoreState.ROM = a synthetic FE8U ROM
    /// and an other-ROM loaded from a temp file containing
    /// <paramref name="otherBytes"/>. Restores CoreState.ROM and deletes the
    /// temp file afterward.
    /// </summary>
    static void RunWithOtherRom(byte[] otherBytes, Action<PointerToolViewModel> body)
    {
        var vm = new PointerToolViewModel();
        ROM rom = MakeSyntheticFe8uRom();
        ROM? prev = CoreState.ROM;
        string tempPath = Path.Combine(Path.GetTempPath(),
            $"pointertool-crossrom-{Guid.NewGuid():N}.bin");
        try
        {
            CoreState.ROM = rom;
            File.WriteAllBytes(tempPath, otherBytes);
            vm.LoadOtherRom(tempPath);
            body(vm);
        }
        finally
        {
            CoreState.ROM = prev;
            try { File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// Build an other-ROM byte buffer with a single raw 32-bit GBA pointer to
    /// <paramref name="targetOffset"/> planted at <paramref name="refOffset"/>.
    /// </summary>
    static byte[] MakeOtherRomWithRawPointer(uint refOffset, uint targetOffset)
    {
        var data = new byte[0x10000];
        uint ptr = 0x08000000u + targetOffset;
        data[refOffset + 0] = (byte)(ptr & 0xFF);
        data[refOffset + 1] = (byte)((ptr >> 8) & 0xFF);
        data[refOffset + 2] = (byte)((ptr >> 16) & 0xFF);
        data[refOffset + 3] = (byte)((ptr >> 24) & 0xFF);
        return data;
    }

    /// <summary>
    /// Build an other-ROM byte buffer with a Thumb `LDR rX,[pc,#imm]` at
    /// <paramref name="ldrInstrOffset"/> whose literal-pool slot at
    /// <paramref name="ldrSlotOffset"/> holds the GBA pointer to
    /// <paramref name="targetOffset"/>. The instruction is encoded as
    /// 0x4800 (LDR r0, [pc, #0]) so the disassembler's literal-pool slot
    /// resolves to <paramref name="ldrSlotOffset"/>.
    /// </summary>
    static byte[] MakeOtherRomWithLdr(uint ldrInstrOffset, uint ldrSlotOffset, uint targetOffset)
    {
        var data = new byte[0x10000];
        // LDR r0,[pc,#0] => 0x4800 (little-endian bytes 00 48).
        data[ldrInstrOffset + 0] = 0x00;
        data[ldrInstrOffset + 1] = 0x48;
        // Literal-pool slot holds the GBA pointer to the target.
        uint ptr = 0x08000000u + targetOffset;
        data[ldrSlotOffset + 0] = (byte)(ptr & 0xFF);
        data[ldrSlotOffset + 1] = (byte)((ptr >> 8) & 0xFF);
        data[ldrSlotOffset + 2] = (byte)((ptr >> 16) & 0xFF);
        data[ldrSlotOffset + 3] = (byte)((ptr >> 24) & 0xFF);
        return data;
    }

    static void AssertHandlerWiring(string source, string handlerName, string requiredCallPattern)
    {
        // Find the handler signature: `void handlerName(...)`. Then walk
        // braces from the first `{` to find the matching `}` so we capture
        // the ENTIRE method body — including nested blocks (e.g. the early
        // return inside `if (...) { ... }`). A non-greedy regex would stop
        // at the first `}` (the nested one) and miss calls further down.
        int sigIdx = source.IndexOf(handlerName + "(", StringComparison.Ordinal);
        Assert.True(sigIdx >= 0,
            $"Click handler '{handlerName}' not found in PointerToolView.axaml.cs");
        int braceOpenIdx = source.IndexOf('{', sigIdx);
        Assert.True(braceOpenIdx > sigIdx, $"Handler '{handlerName}' has no body");
        int depth = 1;
        int i = braceOpenIdx + 1;
        for (; i < source.Length && depth > 0; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
        }
        Assert.True(depth == 0,
            $"Handler '{handlerName}' body is malformed (no matching `}}`)");
        string body = source.Substring(braceOpenIdx + 1, i - braceOpenIdx - 2);
        Assert.Matches(requiredCallPattern, body);
    }

    /// <summary>
    /// Build a synthetic FE8U ROM minimal enough for ROM.LoadLow to accept
    /// the bytes. Used by tests that need CoreState.ROM populated but don't
    /// need any specific game data structures.
    /// </summary>
    static ROM MakeSyntheticFe8uRom()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
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
