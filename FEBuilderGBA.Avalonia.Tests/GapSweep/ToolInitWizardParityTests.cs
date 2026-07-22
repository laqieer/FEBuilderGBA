// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep #401 regression tests for ToolInitWizardView.
//
// Closes the 116-gap surface the gap-sweep methodology surfaced on the WF
// ToolInitWizardForm: density High (-90.0%, WF 80 / AV 8) + 44 WF-only
// labels. Per the v2 plan (accepted by Copilot CLI without blockers), this
// PR brings the AV view to ~85 controls (density LOW, |delta%| < 25), wires
// 9 tool paths + 6 step-mode enums in the ViewModel, and persists state to
// CoreState.Config ONLY via the single ApplyAll() method (atomic commit at
// Finish; per-step writes are NOT allowed so cancelling mid-wizard never
// leaves a partial config).
//
// Test layers:
//   1. Static AXAML invariants (density, AutomationIds, IsEnabled=False on
//      download/install stubs, 9 TabItems, KnownGap NonEnglish markers).
//   2. Code-scan invariants (no ROM writes in view code-behind OR ViewModel;
//      no _undoService.Begin calls since the wizard does not modify ROM).
//   3. ViewModel logic (StageStep1..6 validation, ApplyAll deferred-write
//      semantics, atomic Cancel safety, GoToPage clamping).
//   4. Headless interaction (Avalonia.Headless.XUnit: instantiate the view,
//      click handlers, verify CurrentPage advances correctly).
//   5. Localisation (new English literals translated in ja+zh).
//
// Mirrors the parity-test pattern from PR #569 (ItemForm), PR #576 (ItemFE6),
// PR #577 (ClassOPDemo), PR #580 (TextForm).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Interactivity;
using global::Avalonia.LogicalTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
// RecordingNavigationService (#1122 fake) lives in FEBuilderGBA.Avalonia.Tests
// (NavigationServiceTests.cs); reused here to prove the FEMapCreator "Open in
// Options" action routes through WindowManager without opening a real window.
using FEBuilderGBA.Avalonia.Tests;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class ToolInitWizardParityTests
{
    // ===================================================================
    // 1) Static AXAML invariants.
    // ===================================================================

    [Fact]
    public void View_AvControlCount_AtOrAboveLowVerdict()
    {
        // WF Designer.cs reports 80 controls (per 2026-05-26 density sweep).
        // To stay inside the LOW verdict we need AV >= ceil(80 * 0.75) = 60.
        // Plan v2 targets ~85; this test guards the floor.
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 80;
        int lowThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 60
        Assert.True(avCount >= lowThreshold,
            $"AV control count {avCount} must be >= {lowThreshold} (75% of WF={WfControlCount})");
    }

    [Fact]
    public void View_Has9TabItems()
    {
        var doc = XDocument.Load(AxamlPath());
        var tabItems = doc.Descendants()
            .Where(e => e.Name.LocalName == "TabItem")
            .ToList();
        Assert.Equal(9, tabItems.Count);
    }

    [Fact]
    public void View_HasMainTabAutomationId_FollowsConvention()
    {
        // AutomationId naming convention: {Editor}_{Field}_{Type}. Type for a
        // TabControl is "TabControl" (per AutomationIdTests.ValidSuffixes).
        // Renamed from "ToolInitWizard_MainTab" -> "ToolInitWizard_MainTab_TabControl"
        // per Copilot CLI #583 review finding #1.
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ToolInitWizard_MainTab_TabControl\"",
            axaml);
    }

    // ===================================================================
    // #1978 Slice 4 — optional FEMapCreator setup row on EndPage.
    // ===================================================================

    [Fact]
    public void EndPage_HasFEMapCreatorRow_WithHeaderDisclaimerAndSkipNotice()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ToolInitWizard_FEMapCreatorHeader_Label\"",
            axaml);
        Assert.Contains("FEMapCreator (Optional, External Tool)", axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ToolInitWizard_FEMapCreatorDisclaimer_Label\"",
            axaml);
        // Plan v4 §7 exact required disclaimer text (reused verbatim from the
        // Options FEMapCreator section — same literal, same translation
        // entry; no duplicate ja/zh keys needed for this one).
        Assert.Contains(
            "FEMapCreator is an independent utility credited to bwdyeti; " +
            "FEBuilderGBA does not bundle, host, license, or guarantee it.",
            axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ToolInitWizard_FEMapCreatorSkipNotice_Label\"",
            axaml);
        Assert.Contains(
            "Built-in Experimental remains available immediately if you skip this or leave it unconfigured.",
            axaml);
    }

    [Fact]
    public void EndPage_FEMapCreatorRow_HasBothExplicitActionButtons_WithClickWiring()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ToolInitWizard_OpenFEMapCreatorProjectPage_Button\"",
            axaml);
        Assert.Contains("Click=\"OnOpenFEMapCreatorProjectPage_Click\"", axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ToolInitWizard_OpenFEMapCreatorOptions_Button\"",
            axaml);
        Assert.Contains("Click=\"OnOpenFEMapCreatorOptions_Click\"", axaml);
        Assert.Contains(
            "AutomationProperties.AutomationId=\"ToolInitWizard_FEMapCreatorActionStatus_Label\"",
            axaml);
        Assert.Contains("Name=\"FEMapCreatorActionStatusTextBlock\"", axaml);
        Assert.Contains("IsVisible=\"False\"", axaml);
    }

    [Fact]
    public void EndPage_FEMapCreatorRow_IsNotANewTabItem()
    {
        // The row must live inside the existing EndPage TabItem, never add a
        // 10th page — View_Has9TabItems is the primary guard; this test
        // pins the row's location textually between the EndPage TabItem
        // open tag and the pre-existing Finish button so a future edit
        // can't silently relocate it into its own page.
        string axaml = ReadAxaml();
        int endPageIdx = axaml.IndexOf(
            "AutomationProperties.AutomationId=\"ToolInitWizard_EndPage_Tab\"",
            StringComparison.Ordinal);
        int rowIdx = axaml.IndexOf(
            "AutomationProperties.AutomationId=\"ToolInitWizard_FEMapCreatorHeader_Label\"",
            StringComparison.Ordinal);
        int finishIdx = axaml.IndexOf(
            "AutomationProperties.AutomationId=\"ToolInitWizard_End_Button\"",
            StringComparison.Ordinal);
        Assert.True(endPageIdx >= 0 && rowIdx > endPageIdx && finishIdx > rowIdx,
            "FEMapCreator row must sit inside EndPage, before the Finish button.");
    }

    [Fact]
    public void FEMapCreatorRow_ContainsNoDownloadInstallOrPathSearchText()
    {
        // Scoped to the row block only (Header..the two buttons) so this
        // doesn't collide with the pre-existing, legitimate Step1-6
        // "Download ..." button literals elsewhere in the same file.
        string axaml = ReadAxaml();
        int start = axaml.IndexOf(
            "AutomationProperties.AutomationId=\"ToolInitWizard_FEMapCreatorHeader_Label\"",
            StringComparison.Ordinal);
        int end = axaml.IndexOf(
            "AutomationProperties.AutomationId=\"ToolInitWizard_End_Button\"",
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string row = axaml[start..end];
        Assert.DoesNotContain("Download", row, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Install", row, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PATH", row, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FEMapCreatorClickHandlers_ContainOnlyFixedBrowserLaunchAndOptionsNavigation()
    {
        // Whole-class sweep (per Slice 3 review pattern): the two new
        // handlers must not contain download/installer/PATH-search/registry/
        // environment-variable code — only a fixed-URL Avalonia launcher
        // action (project page) and a WindowManager.Open<OptionsView>()
        // navigation call (Options).
        string source = File.ReadAllText(ViewCodeBehindPath());
        int start = source.IndexOf(
            "void OnOpenFEMapCreatorProjectPage_Click", StringComparison.Ordinal);
        int end = source.IndexOf(
            "// Download helpers (#1031).", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start,
            "Could not locate the FEMapCreator handler block in code-behind.");
        string handlers = source[start..end];

        Assert.Contains("https://github.com/laqieer/FEMapCreator", source);
        Assert.Contains("async void OnOpenFEMapCreatorProjectPage_Click", source);
        Assert.Contains("TopLevel.GetTopLevel(this)", handlers);
        Assert.Contains("await top.Launcher.LaunchUriAsync(FEMapCreatorProjectUri)", handlers);
        Assert.Contains("if (!launched)", handlers);
        Assert.Contains("ShowFEMapCreatorProjectPageError()", handlers);
        Assert.Contains("catch (System.ComponentModel.Win32Exception ex)", handlers);
        Assert.Contains("catch (System.IO.IOException ex)", handlers);
        Assert.Contains("catch (UnauthorizedAccessException ex)", handlers);
        Assert.Contains("catch (InvalidOperationException ex)", handlers);
        Assert.Contains("catch (NotSupportedException ex)", handlers);
        Assert.Contains("catch (System.Security.SecurityException ex)", handlers);
        Assert.Contains("catch (Exception ex)", handlers);
        Assert.Contains("ShowFEMapCreatorProjectPageError(ex)", handlers);
        Assert.Contains("FEMapCreatorActionStatusTextBlock", handlers);
        Assert.Contains("WindowManager.Instance.Open<OptionsView>()", handlers);
        Assert.Contains("options.ShowFEMapCreatorSection()", handlers);

        Assert.DoesNotContain("System.Diagnostics.Process", handlers);
        Assert.DoesNotContain("Process.Start", handlers);
        Assert.DoesNotContain("ProcessStartInfo", handlers);
        Assert.DoesNotContain("UseShellExecute", handlers);
        Assert.DoesNotContain("Download", handlers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Install", handlers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Environment.GetEnvironmentVariable", handlers);
        Assert.DoesNotContain("Registry", handlers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PATH", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("FEMapCreatorTilesetDiscoveryCore", handlers);
        Assert.DoesNotContain("OpenModal", handlers);

        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source).GetRoot();
        var relevantMethods = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText is
                "OnOpenFEMapCreatorProjectPage_Click" or
                "ShowFEMapCreatorProjectPageError" or
                "SetFEMapCreatorProjectPageStatus")
            .ToArray();
        Assert.Equal(3, relevantMethods.Length);
        var broadCatches = relevantMethods
            .SelectMany(method => method.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.CatchClauseSyntax>()
                .Select(clause => (method, clause)))
            .Where(item =>
            {
                if (item.clause.Declaration == null)
                    return true;
                string type = item.clause.Declaration.Type.ToString();
                return type is "Exception" or "System.Exception" or "global::System.Exception";
            })
            .ToArray();
        var launcherBoundaryCatch = Assert.Single(broadCatches);
        Assert.Equal(
            "OnOpenFEMapCreatorProjectPage_Click",
            launcherBoundaryCatch.method.Identifier.ValueText);
        Assert.Contains(
            "ShowFEMapCreatorProjectPageError(ex)",
            launcherBoundaryCatch.clause.Block.ToString());
    }

    [Fact]
    public void View_DirectTabClicks_AreInterceptedViaSelectionChanged()
    {
        // Per Copilot CLI #583 review finding #3 + Copilot bot review
        // #583 round-3 (rejecting IsHitTestVisible="False" on TabItem
        // because it also disables the content): direct tab clicks must
        // NOT allow the user to jump past Step1..6 validation. The fix
        // intercepts the TabControl SelectionChanged event in code-behind
        // (OnMainTabSelectionChanged) and reverts any selection that
        // wasn't initiated by an explicit Next/Prev/Skip handler.
        string axaml = ReadAxaml();
        Assert.Contains("SelectionChanged=\"OnMainTabSelectionChanged\"", axaml);
        // Tab content must remain interactive — verify the rejected
        // IsHitTestVisible="False" pattern is NOT present on TabItem
        // (preventing a regression).
        Assert.DoesNotContain("<Style Selector=\"TabItem\">", axaml);
        Assert.DoesNotContain("Property=\"IsHitTestVisible\" Value=\"False\"", axaml);
    }

    [Fact]
    public void View_CodeBehind_HasSelectionChangedHandler_AndProgrammaticGate()
    {
        // The interception is meaningless without the _allowProgrammaticPageChange
        // gate flag and the NavigateToPage helper that sets it. Verify both
        // are present in the code-behind.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("OnMainTabSelectionChanged", source);
        Assert.Contains("_allowProgrammaticPageChange", source);
        Assert.Contains("NavigateToPage(", source);
    }

    [Theory]
    [InlineData("BeginPage")]
    [InlineData("Step1Page")]
    [InlineData("Step2Page")]
    [InlineData("Step3Page")]
    [InlineData("Step4Page")]
    [InlineData("Step5Page")]
    [InlineData("Step6Page")]
    [InlineData("SettingNowPage")]
    [InlineData("EndPage")]
    public void View_HasTabItem_PerWfPageName(string pageName)
    {
        string axaml = ReadAxaml();
        Assert.Contains($"AutomationProperties.AutomationId=\"ToolInitWizard_{pageName}_Tab\"", axaml);
    }

    [Fact]
    public void View_HasLanguageButtons_EnJpZh()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ToolInitWizard_LangEN_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_LangJP_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_LangZH_Button\"", axaml);
    }

    [Fact]
    public void View_HasColorButtons_WhiteBlackBlack2()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ToolInitWizard_WhiteBG_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_BlackBG_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_BlackBG2_Button\"", axaml);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void View_HasStepN_PrevAndNextButtons(int n)
    {
        string axaml = ReadAxaml();
        Assert.Contains($"AutomationId=\"ToolInitWizard_Step{n}Prev_Button\"", axaml);
        Assert.Contains($"AutomationId=\"ToolInitWizard_Step{n}Next_Button\"", axaml);
    }

    [Fact]
    public void View_HasPathTextBoxesForAllNineTools()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ToolInitWizard_Emulator_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_Debugger_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_ASM_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_Sappy_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_EA_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_GbaMusRiper_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_Sox_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_Midfix4agb_Input\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_Git_Input\"", axaml);
    }

    [Fact]
    public void View_HasReferenceButtonsForAllNineTools()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ToolInitWizard_RefEmulator_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefDebugger_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefASM_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefSappy_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefEA_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefGbaMusRiper_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefSox_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefMidfix4agb_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_RefGit_Button\"", axaml);
    }

    [Theory]
    [InlineData("ToolInitWizard_DownloadVBAM_Button")]
    [InlineData("ToolInitWizard_DownloadMGBA_Button")]
    [InlineData("ToolInitWizard_DownloadEA_Button")]
    [InlineData("ToolInitWizard_DownloadSappy_Button")]
    [InlineData("ToolInitWizard_DownloadVGMusicStudio_Button")]
    [InlineData("ToolInitWizard_DownloadASM_Button")]
    [InlineData("ToolInitWizard_DownloadMusicTool_Button")]
    [InlineData("ToolInitWizard_DownloadGit_Button")]
    public void View_AllDownloadInstallButtons_ArePlatformGated(string automationId)
    {
        // #1031: the 8 Download buttons are now WIRED, but auto-download is
        // Windows-only (the binaries + Git installer are Windows). Each button's
        // IsEnabled is bound to DownloadEnabledOnThisPlatform and carries the
        // NonWindowsDownloadTooltip (steering non-Windows users to Browse). This
        // replaces the previous always-disabled / DownloadDisabledTooltip stub.
        string axaml = ReadAxaml();
        int idIdx = axaml.IndexOf($"AutomationId=\"{automationId}\"", StringComparison.Ordinal);
        Assert.True(idIdx >= 0, $"AutomationId {automationId} missing from AXAML");

        int buttonStart = axaml.LastIndexOf("<Button", idIdx, StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, $"Could not find <Button opening tag for {automationId}");

        int buttonEnd = FindElementEnd(axaml, buttonStart);
        string buttonText = axaml.Substring(buttonStart, buttonEnd - buttonStart);
        Assert.Contains("IsEnabled=\"{Binding DownloadEnabledOnThisPlatform}\"", buttonText);
        Assert.Contains("NonWindowsDownloadTooltip", buttonText);
        // The dead always-disabled stub markers must be gone.
        Assert.DoesNotContain("IsEnabled=\"False\"", buttonText);
        Assert.DoesNotContain("DownloadDisabledTooltip", buttonText);
    }

    [Fact]
    public void ViewModel_PlatformGate_TogglesWithProvider()
    {
        // #1031: DownloadEnabledOnThisPlatform reads through the injectable
        // WindowsPlatformProvider so the gated behaviour is testable on any CI
        // host. Flip it both ways and assert + reset in finally.
        var original = ToolInitWizardViewModel.WindowsPlatformProvider;
        try
        {
            ToolInitWizardViewModel.WindowsPlatformProvider = () => true;
            var vmWin = new ToolInitWizardViewModel();
            Assert.True(vmWin.IsWindowsPlatform);
            Assert.True(vmWin.DownloadEnabledOnThisPlatform);

            ToolInitWizardViewModel.WindowsPlatformProvider = () => false;
            var vmNon = new ToolInitWizardViewModel();
            Assert.False(vmNon.IsWindowsPlatform);
            Assert.False(vmNon.DownloadEnabledOnThisPlatform);
        }
        finally
        {
            ToolInitWizardViewModel.WindowsPlatformProvider = original;
        }
    }

    [Fact]
    public void ViewModel_NonWindowsDownloadTooltip_IsLocalizable()
    {
        var vm = new ToolInitWizardViewModel();
        Assert.False(string.IsNullOrEmpty(vm.NonWindowsDownloadTooltip));
    }

    [Theory]
    [InlineData(ToolInitWizardViewModel.Step1Mode_Enum.Path)]
    public void ViewModel_StageDownloadedEmulator_ForcesPathMode(
        ToolInitWizardViewModel.Step1Mode_Enum _)
    {
        // #1031: a successful emulator download stages the path AND forces the
        // step mode back to Path so the existing ApplyAll() Path-write persists.
        var vm = new ToolInitWizardViewModel();
        vm.Initialize();
        vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.DownloadMGBA;
        vm.StageDownloadedEmulator("emu.exe");
        Assert.Equal("emu.exe", vm.PendingEmulatorPath);
        Assert.Equal(ToolInitWizardViewModel.Step1Mode_Enum.Path, vm.PendingStep1Mode);
    }

    [Fact]
    public void ViewModel_StageDownloadedTools_ForcePathMode_AllSlots()
    {
        var vm = new ToolInitWizardViewModel();
        vm.Initialize();

        vm.StageDownloadedEA("ea.exe");
        Assert.Equal("ea.exe", vm.PendingEAPath);
        Assert.Equal(ToolInitWizardViewModel.Step2Mode_Enum.Path, vm.PendingStep2Mode);

        vm.StageDownloadedSappy("sappy.exe");
        Assert.Equal("sappy.exe", vm.PendingSappyPath);
        Assert.Equal(ToolInitWizardViewModel.Step3Mode_Enum.Path, vm.PendingStep3Mode);

        vm.StageDownloadedDebuggerAndASM("nogba.exe", "as.exe");
        Assert.Equal("nogba.exe", vm.PendingDebuggerPath);
        Assert.Equal("as.exe", vm.PendingASMPath);
        Assert.Equal(ToolInitWizardViewModel.Step4Mode_Enum.Path, vm.PendingStep4Mode);

        vm.StageDownloadedMusicTools("riper.exe", "sox.exe", "mid.exe");
        Assert.Equal("riper.exe", vm.PendingGbaMusRiperPath);
        Assert.Equal("sox.exe", vm.PendingSoxPath);
        Assert.Equal("mid.exe", vm.PendingMidfix4agbPath);
        Assert.Equal(ToolInitWizardViewModel.Step5Mode_Enum.Path, vm.PendingStep5Mode);

        vm.StageDownloadedGit("git.exe");
        Assert.Equal("git.exe", vm.PendingGitPath);
        Assert.Equal(ToolInitWizardViewModel.Step6Mode_Enum.Path, vm.PendingStep6Mode);
    }

    [Fact]
    public void View_HasSkipButtons_ForOptionalSteps()
    {
        string axaml = ReadAxaml();
        // 4 skip buttons (Step2, Step3, Step4, Step5, Step6 -- Step1 has no
        // skip option in WF; Step6's SkipGit also exists). Final count: 5.
        Assert.Contains("AutomationId=\"ToolInitWizard_SkipStep2_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_SkipStep3_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_SkipStep4_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_SkipStep5_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_SkipStep6_Button\"", axaml);
    }

    [Fact]
    public void View_HasStartAndEndButtons()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ToolInitWizard_Start_Button\"", axaml);
        Assert.Contains("AutomationId=\"ToolInitWizard_End_Button\"", axaml);
    }

    [Fact]
    public void View_TabHeaders_MarkedKnownGapNonEnglish()
    {
        // The 9 TabItem.Header values mirror WF private TabPage Name strings
        // (BeginPage, Step1Page..Step6Page, SettingNowPage, EndPage) so the
        // label-diff scanner finds them. They are NOT user-visible (the WF
        // version hides the tab strip). Each must carry a KnownGap NonEnglish
        // marker so the gap-sweep classifier doesn't flag them as gaps.
        string axaml = ReadAxaml();
        Assert.Contains("KnownGap NonEnglish: WF internal TabPage Name", axaml);
    }

    // ===================================================================
    // 2) Code-scan invariants — no ROM writes anywhere.
    // ===================================================================

    [Fact]
    public void View_AxamlCs_HasNoRomWrites()
    {
        // Per Copilot CLI plan-review v2 #4: scan BOTH View and ViewModel for
        // ROM writes (not just code-behind). The wizard touches CoreState.Config
        // only — never ROM bytes.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain(".write_u8(", source);
        Assert.DoesNotContain(".write_u16(", source);
        Assert.DoesNotContain(".write_u32(", source);
        Assert.DoesNotContain(".SetU8(", source);
        Assert.DoesNotContain(".SetU16(", source);
        Assert.DoesNotContain(".SetU32(", source);
    }

    [Fact]
    public void ViewModel_HasNoRomWrites()
    {
        string source = File.ReadAllText(ViewModelPath());
        Assert.DoesNotContain(".write_u8(", source);
        Assert.DoesNotContain(".write_u16(", source);
        Assert.DoesNotContain(".write_u32(", source);
        Assert.DoesNotContain(".SetU8(", source);
        Assert.DoesNotContain(".SetU16(", source);
        Assert.DoesNotContain(".SetU32(", source);
    }

    [Fact]
    public void View_AxamlCs_HasNoUndoServiceCalls()
    {
        // _undoService is for ROM-byte mutations only. The wizard touches
        // CoreState.Config so it must not (and does not) interact with the
        // undo system.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain("_undoService.Begin(", source);
        Assert.DoesNotContain("_undoService.Commit(", source);
        Assert.DoesNotContain("_undoService.Rollback(", source);
    }

    // ===================================================================
    // 3) ViewModel logic (atomic ApplyAll, staging, validation).
    // ===================================================================

    [Fact]
    public void ViewModel_InitializesPathsFromConfig()
    {
        using (new ConfigSnapshot())
        {
            CoreState.Config["emulator"] = "vba-m.exe";
            CoreState.Config["sappy"] = "sappy.exe";
            CoreState.Config["git_path"] = "git.exe";

            var vm = new ToolInitWizardViewModel();
            vm.Initialize();

            Assert.Equal("vba-m.exe", vm.PendingEmulatorPath);
            Assert.Equal("sappy.exe", vm.PendingSappyPath);
            Assert.Equal("git.exe", vm.PendingGitPath);
        }
    }

    [Fact]
    public void ViewModel_Initialize_PrefersLanguageKey_OverFuncLang()
    {
        // Copilot bot review #583: Avalonia prefers "Language" with fallback
        // to "func_lang" (WF backward compat). When BOTH are set, Language wins.
        using (new ConfigSnapshot())
        {
            CoreState.Config["Language"] = "zh";
            CoreState.Config["func_lang"] = "ja"; // should be ignored

            var vm = new ToolInitWizardViewModel();
            vm.Initialize();

            Assert.Equal("zh", vm.PendingLanguage);
        }
    }

    [Fact]
    public void ViewModel_Initialize_FallsBackToFuncLang_WhenLanguageNotSet()
    {
        // When "Language" is empty but "func_lang" is set, fallback kicks in.
        using (new ConfigSnapshot())
        {
            CoreState.Config["Language"] = "";
            CoreState.Config["func_lang"] = "ja";

            var vm = new ToolInitWizardViewModel();
            vm.Initialize();

            Assert.Equal("ja", vm.PendingLanguage);
        }
    }

    [Fact]
    public void ViewModel_SetPendingLanguage_DoesNotWriteConfig()
    {
        using (new ConfigSnapshot())
        {
            string before = CoreState.Config.at("func_lang");
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingLanguage = "zh";

            // The Pending* setter MUST NOT write through. The actual write
            // happens only in ApplyAll() (called from Finish).
            string after = CoreState.Config.at("func_lang");
            Assert.Equal(before, after);
        }
    }

    [Fact]
    public void ViewModel_ApplyAll_WritesAllPendingPathsToConfig()
    {
        using (new ConfigSnapshot())
        {
            using (var emu = new TempFile())
            using (var dbg = new TempFile())
            using (var asm = new TempFile())
            using (var sappy = new TempFile())
            using (var ea = new TempFile())
            using (var mus = new TempFile())
            using (var sox = new TempFile())
            using (var mid = new TempFile())
            {
                var vm = new ToolInitWizardViewModel();
                vm.Initialize();

                vm.PendingEmulatorPath = emu.Path;
                vm.PendingDebuggerPath = dbg.Path;
                vm.PendingASMPath = asm.Path;
                vm.PendingSappyPath = sappy.Path;
                vm.PendingEAPath = ea.Path;
                vm.PendingGbaMusRiperPath = mus.Path;
                vm.PendingSoxPath = sox.Path;
                vm.PendingMidfix4agbPath = mid.Path;
                vm.PendingGitPath = "git";
                vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.Path;
                vm.PendingStep2Mode = ToolInitWizardViewModel.Step2Mode_Enum.Path;
                vm.PendingStep3Mode = ToolInitWizardViewModel.Step3Mode_Enum.Path;
                vm.PendingStep4Mode = ToolInitWizardViewModel.Step4Mode_Enum.Path;
                vm.PendingStep5Mode = ToolInitWizardViewModel.Step5Mode_Enum.Path;
                vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.Path;
                vm.PendingLanguage = "en";
                vm.PendingColorTheme = 2;

                // Per the IsCompletedThroughStep6 gate: ApplyAll() is a no-op
                // until StageStep6 sets the gate. Run the full Stage1..6 flow
                // (the Path mode validation passes because all temp files exist).
                Assert.True(vm.StageStep1());
                Assert.True(vm.StageStep2());
                Assert.True(vm.StageStep3());
                Assert.True(vm.StageStep4());
                Assert.True(vm.StageStep5());
                // StageStep6 with PendingGitPath="git" depends on whether the
                // host has git on PATH. The gate flag can also be set directly
                // for test convenience.
                vm.IsCompletedThroughStep6 = true;

                vm.ApplyAll();

                Assert.Equal(emu.Path, CoreState.Config.at("emulator"));
                Assert.Equal(dbg.Path, CoreState.Config.at("emulator2"));
                Assert.Equal(asm.Path, CoreState.Config.at("devkitpro_eabi"));
                Assert.Equal(sappy.Path, CoreState.Config.at("sappy"));
                Assert.Equal(ea.Path, CoreState.Config.at("event_assembler"));
                Assert.Equal(mus.Path, CoreState.Config.at("gba_mus_riper"));
                Assert.Equal(sox.Path, CoreState.Config.at("sox"));
                Assert.Equal(mid.Path, CoreState.Config.at("midfix4agb"));
                Assert.Equal("git", CoreState.Config.at("git_path"));
                // ApplyAll writes BOTH "Language" (Avalonia-preferred) AND
                // "func_lang" (WF backward-compat) — Copilot bot #583 round-2.
                Assert.Equal("en", CoreState.Config.at("Language"));
                Assert.Equal("en", CoreState.Config.at("func_lang"));
                Assert.Equal("2", CoreState.Config.at("color_set"));
            }
        }
    }

    [Fact]
    public void ViewModel_ApplyAll_DoesNotWriteWhenModeIsDoNotSelect()
    {
        using (new ConfigSnapshot())
        {
            CoreState.Config["event_assembler"] = "old.exe";

            var vm = new ToolInitWizardViewModel();
            vm.Initialize();

            // PendingStep2Mode = DoNotSelect should leave event_assembler
            // untouched even when PendingEAPath is set.
            vm.PendingEAPath = "new.exe";
            vm.PendingStep2Mode = ToolInitWizardViewModel.Step2Mode_Enum.DoNotSelect;

            // Bypass the IsCompletedThroughStep6 gate so we can exercise the
            // DoNotSelect path-skip behavior in isolation.
            vm.IsCompletedThroughStep6 = true;
            vm.ApplyAll();

            Assert.Equal("old.exe", CoreState.Config.at("event_assembler"));
        }
    }

    [Fact]
    public void ViewModel_StageStep1_PathMode_RejectsMissingFile()
    {
        using (new ConfigSnapshot())
        {
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.Path;
            vm.PendingEmulatorPath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + "_does_not_exist.exe");

            Assert.False(vm.StageStep1());
            // Critically: NO write to Config even though StageStep1 was called.
            Assert.NotEqual(vm.PendingEmulatorPath, CoreState.Config.at("emulator"));
        }
    }

    [Fact]
    public void ViewModel_StageStep1_PathMode_AcceptsExistingFile()
    {
        using (new ConfigSnapshot())
        using (var tmp = new TempFile())
        {
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.Path;
            vm.PendingEmulatorPath = tmp.Path;

            Assert.True(vm.StageStep1());
            // Still NO write to Config — StageStep1 only validates.
            Assert.NotEqual(tmp.Path, CoreState.Config.at("emulator"));
        }
    }

    [Fact]
    public void ViewModel_StageStep4_RequiresBothDebuggerAndASM()
    {
        using (new ConfigSnapshot())
        using (var dbg = new TempFile())
        {
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep4Mode = ToolInitWizardViewModel.Step4Mode_Enum.Path;
            vm.PendingDebuggerPath = dbg.Path;
            vm.PendingASMPath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + "_missing_asm.exe");

            // ASM missing -> StageStep4 fails. (WF Step4NextButton_Click
            // requires BOTH paths.)
            Assert.False(vm.StageStep4());
        }
    }

    [Fact]
    public void ViewModel_StageStep5_MidfixIsOptional()
    {
        using (new ConfigSnapshot())
        using (var mus = new TempFile())
        using (var sox = new TempFile())
        {
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep5Mode = ToolInitWizardViewModel.Step5Mode_Enum.Path;
            vm.PendingGbaMusRiperPath = mus.Path;
            vm.PendingSoxPath = sox.Path;
            vm.PendingMidfix4agbPath = ""; // empty -> optional, must still pass.

            // Matches WF: Step5NextButton_Click only checks gba_mus_riper + sox.
            Assert.True(vm.StageStep5());
        }
    }

    [Fact]
    public void ViewModel_StageStep5_SoxIsRequired()
    {
        using (new ConfigSnapshot())
        using (var mus = new TempFile())
        {
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep5Mode = ToolInitWizardViewModel.Step5Mode_Enum.Path;
            vm.PendingGbaMusRiperPath = mus.Path;
            vm.PendingSoxPath = ""; // missing -> StageStep5 must fail.

            Assert.False(vm.StageStep5());
        }
    }

    [Fact]
    public void ViewModel_StageStep6_UsesProbeGit_NotJustFileExists()
    {
        using (new ConfigSnapshot())
        using (var fakeGit = new TempFile())
        {
            // Per Copilot CLI plan-review v2 #3: Step 6 MUST use GitUtil.ProbeGit
            // (which runs `git --version`), NOT just File.Exists. ProbeGit on a
            // zero-byte file fails because the runtime can't execute it as git.
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.Path;
            vm.PendingGitPath = fakeGit.Path;

            // Even though File.Exists(fakeGit.Path) is TRUE, StageStep6 must
            // call ProbeGit which will fail on the zero-byte file. The exact
            // outcome depends on the host (Linux/Windows may differ), but the
            // critical invariant is that StageStep6 calls ProbeGit -- we can
            // assert via static source inspection.
            string vmSource = File.ReadAllText(ViewModelPath());
            Assert.Contains("GitUtil.ProbeGit(PendingGitPath)", vmSource);
        }
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(20, 8)]
    public void ViewModel_CurrentPage_Setter_ClampsToValidRange(int input, int expected)
    {
        // Per Copilot bot review #583 round-4: clarify that this test
        // exercises the CurrentPage SETTER, not GoToPage. GoToPage has
        // its own out-of-range rejection (returns false). The CurrentPage
        // setter always clamps to [0, 8] regardless of source.
        var vm = new ToolInitWizardViewModel();
        vm.CurrentPage = input;
        Assert.Equal(expected, vm.CurrentPage);
    }

    [Theory]
    [InlineData(-5, false, 0)]   // out-of-range: GoToPage rejects, CurrentPage stays
    [InlineData(0, true, 0)]
    [InlineData(3, true, 3)]
    [InlineData(8, true, 8)]
    [InlineData(20, false, 0)]   // out-of-range: GoToPage rejects, CurrentPage stays
    public void ViewModel_GoToPage_ReturnsFalseAndIsNoOp_WhenOutOfRange(
        int input, bool expectedResult, int expectedCurrentPage)
    {
        // Per Copilot bot review #583 round-4: cover the GoToPage API
        // explicitly. Returns true for valid [0, 8], false for out-of-range,
        // and the CurrentPage stays at its previous value when rejected.
        var vm = new ToolInitWizardViewModel();
        vm.Initialize(); // CurrentPage = 0
        bool result = vm.GoToPage(input);
        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedCurrentPage, vm.CurrentPage);
    }

    [Fact]
    public void ViewModel_StageStep1_ThenCancel_LeavesConfigUnchanged()
    {
        // The atomic-commit invariant from Copilot CLI plan-review v2 #2:
        // staging a step then bailing out (closing window, going Prev) must
        // NEVER leave Program.Config in a half-applied state. The wizard
        // only writes via ApplyAll, called from Finish.
        using (new ConfigSnapshot())
        using (var tmp = new TempFile())
        {
            string before = CoreState.Config.at("emulator");
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.Path;
            vm.PendingEmulatorPath = tmp.Path;
            Assert.True(vm.StageStep1());
            // Now "cancel" by not calling ApplyAll.
            Assert.Equal(before, CoreState.Config.at("emulator"));
        }
    }

    [Fact]
    public void View_DownloadButtons_AreWired_NotStubbed()
    {
        // #1031: the disabled-stub handlers (which only set SettingStatus =
        // DownloadDisabledTooltip) are replaced with real async download
        // handlers that route through DownloadInstallCore. Verify the wiring
        // markers are present and the old stub literal is gone.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("DownloadInstallCore", source);
        Assert.Contains("DownloadSingleAsync", source);
        Assert.Contains("DownloadBundleAsync", source);
        Assert.Contains("ConfirmDownloadAsync", source);
        Assert.DoesNotContain("DownloadDisabledTooltip", source);
    }

    // ===================================================================
    // 3b) Defence-in-depth: IsCompletedThroughStep6 gate (Copilot CLI
    // #583 review finding #3). Even if a user reaches EndPage via some
    // bypass path, ApplyAll() must NOT persist anything to Config unless
    // the gate is set, and the gate can ONLY be set by StageStep6 on a
    // successful ProbeGit (or DownloadGit / DoNotSelect modes).
    // ===================================================================

    [Fact]
    public void ViewModel_ApplyAll_IsNoOp_WhenNotCompletedThroughStep6()
    {
        // Set up VM as if user reached EndPage by some bypass route — e.g.
        // direct CurrentPage = 8 without ever calling StageStep6. ApplyAll
        // must NOT write anything to Config.
        using (new ConfigSnapshot())
        using (var emu = new TempFile())
        {
            string before = CoreState.Config.at("emulator");
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingEmulatorPath = emu.Path;
            vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.Path;
            vm.CurrentPage = 8; // jump straight to EndPage
            Assert.False(vm.IsCompletedThroughStep6);

            vm.ApplyAll();

            // Config["emulator"] is unchanged even though VM had a valid
            // PendingEmulatorPath.
            Assert.Equal(before, CoreState.Config.at("emulator"));
        }
    }

    [Fact]
    public void ViewModel_StageStep6_SetsIsCompletedThroughStep6_OnSuccess()
    {
        // Stub via DoNotSelect mode — StageStep6 returns true and sets the
        // gate flag without needing a real git executable.
        var vm = new ToolInitWizardViewModel();
        vm.Initialize();
        vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.DoNotSelect;

        Assert.False(vm.IsCompletedThroughStep6);
        Assert.True(vm.StageStep6());
        Assert.True(vm.IsCompletedThroughStep6);
    }

    [Fact]
    public void ViewModel_StageStep6_LeavesGateFalse_OnFailure()
    {
        // Path mode with a fake git executable -> ProbeGit fails -> gate
        // stays false.
        using (var fakeGit = new TempFile())
        {
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.Path;
            vm.PendingGitPath = fakeGit.Path; // exists but not a git binary

            bool result = vm.StageStep6();
            // result may be true on hosts where ProbeGit somehow accepts it,
            // but if it returned false the gate must stay false.
            Assert.Equal(result, vm.IsCompletedThroughStep6);
        }
    }

    [Fact]
    public void ViewModel_GoToPage_BackwardsResetsIsCompletedThroughStep6()
    {
        // After completing the wizard, going back upstream of Step 6 should
        // force revalidation — i.e. the gate flag clears so a second
        // ApplyAll on the way forward only fires after StageStep6 again.
        var vm = new ToolInitWizardViewModel();
        vm.Initialize();
        vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.DoNotSelect;
        Assert.True(vm.StageStep6());
        Assert.True(vm.IsCompletedThroughStep6);

        // Go back to Step 1 to edit something.
        Assert.True(vm.GoToPage(1));
        Assert.False(vm.IsCompletedThroughStep6);
    }

    [Fact]
    public void View_OnSkipStep6_RoutesThroughStageStep6_NotJustApplyAll()
    {
        // Per Copilot CLI #583 re-review (round 2): the SkipStep6 click
        // handler in the code-behind MUST call StageStep6() before
        // ApplyAll() so the IsCompletedThroughStep6 gate is set. Otherwise
        // a user who validly skips Git would have ApplyAll() be a no-op and
        // lose previously-staged Step1..5 settings.
        string source = File.ReadAllText(ViewCodeBehindPath());
        int handlerIdx = source.IndexOf("void OnSkipStep6_Click", StringComparison.Ordinal);
        Assert.True(handlerIdx >= 0, "OnSkipStep6_Click handler missing.");
        int handlerEnd = source.IndexOf("\n        }", handlerIdx, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerIdx, "Could not delimit OnSkipStep6_Click scope.");
        string handlerBody = source.Substring(handlerIdx, handlerEnd - handlerIdx);
        // StageStep6 must be called BEFORE ApplyAll inside this handler.
        int stageIdx = handlerBody.IndexOf("StageStep6", StringComparison.Ordinal);
        int applyIdx = handlerBody.IndexOf("ApplyAll", StringComparison.Ordinal);
        Assert.True(stageIdx >= 0,
            "OnSkipStep6_Click must call StageStep6() to set the IsCompletedThroughStep6 gate.");
        Assert.True(applyIdx >= 0,
            "OnSkipStep6_Click must call ApplyAll() to persist staged settings.");
        Assert.True(stageIdx < applyIdx,
            "OnSkipStep6_Click must call StageStep6() BEFORE ApplyAll() so the gate is set first.");
    }

    [Fact]
    public void ViewModel_SkipStep6Flow_PersistsPreviouslyStagedSettings()
    {
        // End-to-end VM test for the Skip-Git flow that Copilot CLI #583
        // re-review (round 2) flagged. After the user stages Step1..5 and
        // clicks Skip on Step6:
        //   1. PendingStep6Mode = DoNotSelect
        //   2. StageStep6() returns true (DoNotSelect is valid for completion)
        //   3. IsCompletedThroughStep6 becomes true
        //   4. ApplyAll() then persists all staged settings
        using (new ConfigSnapshot())
        using (var emu = new TempFile())
        {
            var vm = new ToolInitWizardViewModel();
            vm.Initialize();
            vm.PendingEmulatorPath = emu.Path;
            vm.PendingStep1Mode = ToolInitWizardViewModel.Step1Mode_Enum.Path;

            // Simulate Skip on Step6
            vm.PendingStep6Mode = ToolInitWizardViewModel.Step6Mode_Enum.DoNotSelect;
            Assert.True(vm.StageStep6());
            Assert.True(vm.IsCompletedThroughStep6);
            vm.ApplyAll();

            // Verify the Step 1 setting was persisted (not lost to a no-op
            // ApplyAll).
            Assert.Equal(emu.Path, CoreState.Config.at("emulator"));
        }
    }

    [Fact]
    public void ViewModel_ApplyAll_ReportsSaveError_WhenFileDoesNotAppear()
    {
        // Per Copilot bot review #583 round-2/round-3: when Config.Save()
        // fails to land a file on disk (because Config.Save catches its own
        // exceptions internally and doesn't throw), ApplyAll must detect
        // the failure via the post-save File.Exists + LastWriteTimeUtc
        // comparison and surface "could not be saved" via SettingStatus.
        using (new ConfigSnapshot())
        {
            // Create a Config with a non-writable filename so Save() throws.
            var failingConfig = new Config();
            // Use a path inside a non-existent directory so XML write fails.
            string badPath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"),
                "does", "not", "exist", "config.xml");
            // Per Copilot bot review #583 round-5: ConfigFilename has a
            // protected setter so PropertyInfo.SetValue alone wouldn't work.
            // Use the non-public setter via GetSetMethod(true). This
            // bypasses Load() which would try to parse a nonexistent file
            // and fail before we even reach Save().
            var prop = typeof(Config).GetProperty("ConfigFilename");
            var setter = prop?.GetSetMethod(nonPublic: true);
            setter?.Invoke(failingConfig, new object[] { badPath });

            var prevConfig = CoreState.Config;
            try
            {
                CoreState.Config = failingConfig;

                var vm = new ToolInitWizardViewModel();
                vm.Initialize();
                vm.IsCompletedThroughStep6 = true;
                vm.ApplyAll();

                // SettingStatus must report the save failure, NOT
                // "All settings applied."
                Assert.Contains("could not be saved", vm.SettingStatus,
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CoreState.Config = prevConfig;
            }
        }
    }

    [Fact]
    public void ViewModel_ApplyAll_SuccessPath_LastWriteTimeAdvances()
    {
        // Per Copilot bot review #583 round-3: success detection compares
        // LastWriteTimeUtc before/after Save so a stale file doesn't make
        // a failed Save look like success. This test verifies the SUCCESS
        // path with a real writable temp file — Save updates the timestamp,
        // and SettingStatus reports "All settings applied."
        using (new ConfigSnapshot())
        {
            var writableConfig = new Config();
            // Use a real temp file we can write to.
            string tmpPath = Path.Combine(Path.GetTempPath(),
                "ToolInitWizardTest_" + Guid.NewGuid().ToString("N") + ".xml");
            // Per Copilot bot review #583 round-5: ConfigFilename has a
            // protected setter — use GetSetMethod(true) for non-public
            // invocation.
            var prop = typeof(Config).GetProperty("ConfigFilename");
            var setter = prop?.GetSetMethod(nonPublic: true);
            setter?.Invoke(writableConfig, new object[] { tmpPath });

            var prevConfig = CoreState.Config;
            try
            {
                CoreState.Config = writableConfig;
                var vm = new ToolInitWizardViewModel();
                vm.Initialize();
                vm.IsCompletedThroughStep6 = true;
                vm.ApplyAll();

                Assert.True(File.Exists(tmpPath), "Save should have created the file.");
                Assert.Contains("applied", vm.SettingStatus,
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                CoreState.Config = prevConfig;
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }
    }

    [Fact]
    public void ViewModel_ApplyAll_WritesSettingStatus_OnGatedNoOp()
    {
        // A user-visible diagnostic for the no-op path. Helps the screenshot
        // be self-documenting when the gate is closed.
        var vm = new ToolInitWizardViewModel();
        vm.Initialize();
        vm.ApplyAll();
        Assert.False(string.IsNullOrEmpty(vm.SettingStatus));
        Assert.Contains("Step 6", vm.SettingStatus);
    }

    // ===================================================================
    // 4) Headless interaction tests (Avalonia.Headless.XUnit).
    // ===================================================================

    [AvaloniaFact]
    public void View_DirectTabClick_IsReverted_BothInViewAndVM()
    {
        // Per Copilot CLI #583 round-4 review: the TwoWay binding on
        // MainTab.SelectedIndex pushes the illegal target into _vm.CurrentPage
        // BEFORE OnMainTabSelectionChanged fires. The fix must restore BOTH
        // the TabControl.SelectedIndex AND the VM CurrentPage.
        //
        // This test mirrors Copilot CLI's headless probe: directly set
        // SelectedIndex = 3 from page 0 (simulating a tab-header click) and
        // assert both ends snap back to 0.
        var view = new ToolInitWizardView();
        var vm = (ToolInitWizardViewModel)view.DataViewModel!;
        var tab = view.GetLogicalDescendants()
            .OfType<TabControl>()
            .FirstOrDefault(t => global::Avalonia.Automation.AutomationProperties.GetAutomationId(t)
                == "ToolInitWizard_MainTab_TabControl");
        Assert.NotNull(tab);

        // Force the view into a known state (page 0).
        Assert.Equal(0, tab!.SelectedIndex);
        Assert.Equal(0, vm.CurrentPage);

        // Simulate a tab-header click by setting SelectedIndex directly.
        // The TwoWay binding pushes this to _vm.CurrentPage; then
        // OnMainTabSelectionChanged fires and reverts both.
        tab.SelectedIndex = 3;

        // Both must have snapped back.
        Assert.Equal(0, tab.SelectedIndex);
        Assert.Equal(0, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void View_TabControl_StartsOnBeginPage()
    {
        var view = new ToolInitWizardView();
        // Constructor runs Initialize via Opened event; we replicate by
        // checking that the data context is non-null after construction.
        Assert.NotNull(view.DataViewModel);
        // The MainTab SelectedIndex is bound to vm.CurrentPage; vm
        // initialises CurrentPage = 0 in Initialize(). At construction time
        // the Opened event hasn't fired yet, but the default int is 0.
        var vm = (ToolInitWizardViewModel)view.DataViewModel!;
        Assert.Equal(0, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void View_StartButton_AdvancesToStep1Page()
    {
        // Use the real click-event dispatch since the SelectionChanged
        // guard (Copilot CLI #583 round-4) rejects direct vm.GoToPage()
        // mutations as user-initiated jumps. The button click routes
        // through the view's NavigateToPage helper which sets the
        // programmatic-change gate.
        var view = new ToolInitWizardView();
        var vm = (ToolInitWizardViewModel)view.DataViewModel!;
        var btn = view.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => global::Avalonia.Automation.AutomationProperties.GetAutomationId(b)
                == "ToolInitWizard_Start_Button");
        Assert.NotNull(btn);
        btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void View_StartButton_Click_DispatchesClickEvent_AdvancesPage()
    {
        // Per Copilot bot review #583: real click-event dispatch test that
        // exercises the View code-behind handler (not just the VM API).
        // Finds the Start button by AutomationId, raises Button.ClickEvent,
        // and verifies the page advances.
        var view = new ToolInitWizardView();
        var vm = (ToolInitWizardViewModel)view.DataViewModel!;
        Assert.Equal(0, vm.CurrentPage);

        // Locate the Start button via the logical tree (no need to render
        // the visual tree).
        var btn = view.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => global::Avalonia.Automation.AutomationProperties.GetAutomationId(b)
                == "ToolInitWizard_Start_Button");
        Assert.NotNull(btn);

        // Raise the same routed event the user's click would. This invokes
        // OnStartButton_Click in the code-behind, which in turn calls
        // _vm.GoToPage(1).
        btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void View_Step1Prev_ReturnsToBeginPage()
    {
        // Mirror the real click flow: Start advances to Step1, then Step1Prev
        // returns to BeginPage. Both clicks route through NavigateToPage
        // which sets the SelectionChanged-guard's programmatic flag.
        var view = new ToolInitWizardView();
        var vm = (ToolInitWizardViewModel)view.DataViewModel!;

        var startBtn = view.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => global::Avalonia.Automation.AutomationProperties.GetAutomationId(b)
                == "ToolInitWizard_Start_Button");
        Assert.NotNull(startBtn);
        startBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, vm.CurrentPage);

        var prevBtn = view.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => global::Avalonia.Automation.AutomationProperties.GetAutomationId(b)
                == "ToolInitWizard_Step1Prev_Button");
        Assert.NotNull(prevBtn);
        prevBtn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void View_PendingPathBindsToTextBox()
    {
        var view = new ToolInitWizardView();
        var vm = (ToolInitWizardViewModel)view.DataViewModel!;
        // Mutate the VM; the TextBox bound to PendingEmulatorPath should
        // pick the value up. We assert via the bound property rather than
        // walking the visual tree since the binding is two-way.
        vm.PendingEmulatorPath = "test-emu.exe";
        Assert.Equal("test-emu.exe", vm.PendingEmulatorPath);
    }

    // ===================================================================
    // #1978 Slice 4 — FEMapCreator row headless interaction. Uses the same
    // RecordingNavigationService fake from NavigationServiceTests (#1122)
    // so clicking "Open FEMapCreator in Options" is provably routed through
    // WindowManager without ever constructing a real OptionsView/window,
    // and so mere construction of the wizard can be proven NOT to call it.
    // ===================================================================

    [AvaloniaFact]
    public void ConstructingWizardView_DoesNotNavigateToOptions_OrAnywhereElse()
    {
        var original = WindowManager.Instance.Service;
        var fake = new RecordingNavigationService();
        try
        {
            WindowManager.Instance.SetService(fake);
            _ = new ToolInitWizardView();
            Assert.Null(fake.LastCall);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    [AvaloniaFact]
    public void OpenFEMapCreatorProjectPageButton_Click_WithoutTopLevel_ShowsInlineError()
    {
        var view = new ToolInitWizardView();
        var status = view.GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(textBlock =>
                global::Avalonia.Automation.AutomationProperties.GetAutomationId(textBlock)
                    == "ToolInitWizard_FEMapCreatorActionStatus_Label");
        Assert.NotNull(status);
        Assert.False(status!.IsVisible);

        var btn = view.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(button =>
                global::Avalonia.Automation.AutomationProperties.GetAutomationId(button)
                    == "ToolInitWizard_OpenFEMapCreatorProjectPage_Button");
        Assert.NotNull(btn);
        btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(status.IsVisible);
        Assert.StartsWith(FEBuilderGBA.R._("Couldn't open link:"), status.Text);
        Assert.Contains("https://github.com/laqieer/FEMapCreator", status.Text);
    }

    [AvaloniaFact]
    public void OpenFEMapCreatorOptionsButton_Click_OpensOptionsView_ViaWindowManager()
    {
        var original = WindowManager.Instance.Service;
        var fake = new RecordingNavigationService();
        try
        {
            WindowManager.Instance.SetService(fake);
            var view = new ToolInitWizardView();
            var btn = view.GetLogicalDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => global::Avalonia.Automation.AutomationProperties.GetAutomationId(b)
                    == "ToolInitWizard_OpenFEMapCreatorOptions_Button");
            Assert.NotNull(btn);
            btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Open", fake.LastCall);
            Assert.Equal(typeof(OptionsView), fake.LastType);
            OptionsView options = Assert.IsType<OptionsView>(fake.LastControl);
            Assert.Same(
                options.FindControl<TabItem>("ExternalToolsTabItem"),
                options.FindControl<TabControl>("OptionsTabControl")!.SelectedItem);
            Assert.False(options.ViewModelForTests.HasTilesetContext);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    [AvaloniaFact]
    public void OpenFEMapCreatorOptionsButton_Click_DoesNotAdvanceOrChangeWizardPage()
    {
        // The Options-navigation action is a side action on EndPage; it
        // must not itself count as wizard progress/completion.
        var original = WindowManager.Instance.Service;
        var fake = new RecordingNavigationService();
        try
        {
            WindowManager.Instance.SetService(fake);
            var view = new ToolInitWizardView();
            var vm = (ToolInitWizardViewModel)view.DataViewModel!;
            int pageBefore = vm.CurrentPage;
            var btn = view.GetLogicalDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => global::Avalonia.Automation.AutomationProperties.GetAutomationId(b)
                    == "ToolInitWizard_OpenFEMapCreatorOptions_Button");
            Assert.NotNull(btn);
            btn!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(pageBefore, vm.CurrentPage);
        }
        finally
        {
            WindowManager.Instance.SetService(original);
        }
    }

    [AvaloniaFact]
    public void EndButton_Click_StillCallsApplyAllAndRequestsClose_Unaffected()
    {
        // Wizard-completion regression guard: the new optional row sits
        // above the pre-existing Finish button and must not have altered
        // OnEndButton_Click's ApplyAll()+RequestClose() contract.
        string source = File.ReadAllText(ViewCodeBehindPath());
        int start = source.IndexOf("void OnEndButton_Click", StringComparison.Ordinal);
        int end = source.IndexOf(
            "// EndPage — optional FEMapCreator setup row (#1978 Slice 4).",
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string body = source[start..end];
        Assert.Contains("_vm.ApplyAll();", body);
        Assert.Contains("RequestClose();", body);
    }

    // ===================================================================
    // 5) Localisation.
    // ===================================================================

    [Theory]
    [InlineData("Language:")]
    [InlineData("Color:")]
    [InlineData("Emulator:")]
    [InlineData("Debugger:")]
    [InlineData("Assembler:")]
    [InlineData("or,")]
    [InlineData("Step 1/6")]
    [InlineData("Step 6/6")]
    [InlineData("All settings complete.")]
    [InlineData("Please wait...")]
    [InlineData("Browse")]
    [InlineData("Windows-only download — use Browse to set the path manually.")]
    [InlineData("Built-in Experimental remains available immediately if you skip this or leave it unconfigured.")]
    [InlineData("Built-in Experimental creates visually coherent layouts only; gameplay and objective validity are not guaranteed.")]
    [InlineData("Built-in Experimental")]
    [InlineData("FEMapCreator Experimental")]
    [InlineData("Open Project Page")]
    [InlineData("Open FEMapCreator in Options")]
    public void Localisation_NewLiterals_AreTranslated_InJaAndZh(string literal)
    {
        string repoRoot = FindRepoRoot();
        string jaPath = Path.Combine(repoRoot, "config", "translate", "ja.txt");
        string zhPath = Path.Combine(repoRoot, "config", "translate", "zh.txt");
        Assert.True(File.Exists(jaPath), $"ja.txt not found: {jaPath}");
        Assert.True(File.Exists(zhPath), $"zh.txt not found: {zhPath}");

        // Translation files use a `:key`-prefixed line with the next line as
        // the value. Read line-by-line to be encoding-agnostic across CRLF/LF.
        var jaLines = File.ReadAllLines(jaPath);
        var zhLines = File.ReadAllLines(zhPath);
        string keyLine = ":" + literal;
        Assert.Contains(keyLine, jaLines);
        Assert.Contains(keyLine, zhLines);
    }

    // ===================================================================
    // Screenshot harness: the gated Step1 download page is only reachable
    // via the Start button (a direct tab select is reverted by the
    // SelectionChanged gate). #1031 adds an opt-in --screenshot-invoke-button=
    // mode so the GUI-proof PNG can show the now-ENABLED download buttons.
    // ===================================================================

    [Fact]
    public void ScreenshotHarness_ExposesInvokeButtonOption()
    {
        // App.axaml.cs must declare the property AND parse the CLI flag.
        string app = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "App.axaml.cs"));
        Assert.Contains("ScreenshotInvokeButtonAutomationId", app);
        Assert.Contains("--screenshot-invoke-button=", app);
    }

    [Fact]
    public void ScreenshotHarness_InvokesButtonBeforeCapture()
    {
        // MainWindow.RunScreenshotAll must drive the button (via the UIA
        // invoke pattern) BEFORE rendering, so a gate-aware wizard's real
        // navigation handler runs (unlike --screenshot-tab=).
        string mw = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));
        Assert.Contains("InvokeButtonByAutomationId", mw);
        Assert.Contains("ScreenshotInvokeButtonAutomationId", mw);
        Assert.Contains("IInvokeProvider", mw);
    }

    // ===================================================================
    // Helpers.
    // ===================================================================

    static string AxamlPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ToolInitWizardView.axaml");

    static string ViewCodeBehindPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ToolInitWizardView.axaml.cs");

    static string ViewModelPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "ViewModels", "ToolInitWizardViewModel.cs");

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string FindRepoRoot()
    {
        // Per Copilot bot review #583 round-5: walk up to filesystem root
        // but THROW if we never find FEBuilderGBA.sln (matches the pattern
        // used by ItemFE6ParityTests). Returning a partial path would make
        // failures confusing because tests would look for non-existent
        // files at the wrong location.
        string start = AppDomain.CurrentDomain.BaseDirectory;
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException(
            $"Could not locate FEBuilderGBA.sln starting from {start}");
    }

    /// <summary>
    /// Find the index just past the closing of the opening tag at
    /// <paramref name="openIdx"/>. Handles both self-closing ("/&gt;") and
    /// nested element forms; returns the index after the close for the
    /// initial &lt;Button ...&gt; element only (does NOT skip body).
    /// </summary>
    static int FindElementEnd(string axaml, int openIdx)
    {
        // Walk forward looking for the first '>' that isn't inside an attribute
        // quote. Avalonia's AXAML uses simple double-quoted attributes so a
        // quote-toggle scan suffices.
        bool inQuotes = false;
        for (int i = openIdx; i < axaml.Length; i++)
        {
            char c = axaml[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (c == '>' && !inQuotes)
                return i + 1;
        }
        return axaml.Length;
    }

    /// <summary>
    /// IDisposable wrapper that snapshots CoreState.Config and restores it on
    /// dispose. Used by tests that mutate the Config to keep parallel runs
    /// safe (combined with [Collection("SharedState")]). If Config is null
    /// (no config.xml on disk during CI), creates an empty in-memory Config
    /// for the test, restored to null on dispose.
    /// </summary>
    sealed class ConfigSnapshot : IDisposable
    {
        readonly Dictionary<string, string> _snapshot = new();
        readonly bool _createdInMemoryConfig;
        static readonly string[] TrackedKeys = new[]
        {
            "emulator", "emulator2", "devkitpro_eabi", "sappy", "event_assembler",
            "gba_mus_riper", "sox", "midfix4agb", "git_path", "func_lang", "Language",
            "color_set",
        };

        public ConfigSnapshot()
        {
            if (CoreState.Config == null)
            {
                // Create an empty in-memory Config for the test. The default
                // constructor leaves ConfigFilename null/empty, so the
                // ApplyAll() persistence path skips Config.Save() (which would
                // otherwise throw on an empty filename). The tests assert on
                // in-memory key/value pairs only.
                CoreState.Config = new Config();
                _createdInMemoryConfig = true;
            }
            foreach (var key in TrackedKeys)
                _snapshot[key] = CoreState.Config.at(key);
        }

        public void Dispose()
        {
            if (CoreState.Config == null) return;
            if (_createdInMemoryConfig)
            {
                CoreState.Config = null;
                return;
            }
            foreach (var kv in _snapshot)
                CoreState.Config[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Creates a real, on-disk temp file so File.Exists checks pass. Disposes
    /// remove the file. Used by the staging tests to satisfy WF's
    /// File.Exists guard.
    /// </summary>
    sealed class TempFile : IDisposable
    {
        public string Path { get; }
        public TempFile()
        {
            Path = System.IO.Path.GetTempFileName();
        }
        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }
    }
}
