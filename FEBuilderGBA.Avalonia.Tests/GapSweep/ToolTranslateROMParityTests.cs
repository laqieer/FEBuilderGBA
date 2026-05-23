// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1+2 gap-sweep regression tests for ToolTranslateROMView. (#422)
//
// Closes the 54 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `ToolTranslateROMForm` (HIGH density 3/35 == -91.4 %, 22 WF-only labels,
// 0 common labels). This view is a Tool dialog (no ROM-data B#/W#/D#/P# fields)
// so the parity raise is layout + labels only; real ROM-translation execution
// stays WF-coupled until #536 lands.
//
// Mirrors the [Collection("SharedState")] + previous-CoreState-restore pattern
// from MapExitPointParityTests / SkillConfigSkillSystemParityTests.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ToolTranslateROM parity raise (#422) is permanent.
/// Density target: AV &gt;= ceil(WF * 0.75) = 27.
///
/// Marked [Collection("SharedState")] because ROM-label tests mutate
/// CoreState.ROM, CoreState.Language, and CoreState.TextEncoding.
/// </summary>
[Collection("SharedState")]
public class ToolTranslateROMParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer reports 35 controls; the MEDIUM threshold is
    /// `ceil(35 * 0.75) = 27`. Falling below this re-enters HIGH territory.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 35;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 27
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // AutomationIds - every WF-only label has a corresponding AV control.
    // -----------------------------------------------------------------

    /// <summary>
    /// The AXAML must include every AutomationId we depend on for
    /// PrintWindow-based screenshot capture and label-coverage validation.
    /// Each id maps back to one of the 22 WF-only labels listed in
    /// docs/avalonia-gaps/2026-05-22-labels-sweep.md.
    /// </summary>
    [Theory]
    [InlineData("ToolTranslateROM_TabControl")]
    [InlineData("ToolTranslateROM_Simple_Tab")]
    [InlineData("ToolTranslateROM_Detail_Tab")]
    [InlineData("ToolTranslateROM_ExportGroup_Expander")]
    [InlineData("ToolTranslateROM_ImportGroup_Expander")]
    [InlineData("ToolTranslateROM_FontGroup_Expander")]
    [InlineData("ToolTranslateROM_SimpleFromRom_Input")]
    [InlineData("ToolTranslateROM_SimpleToRom_Input")]
    [InlineData("ToolTranslateROM_SimpleFromRom_Button")]
    [InlineData("ToolTranslateROM_SimpleToRom_Button")]
    [InlineData("ToolTranslateROM_SimpleFromRom_Label")]
    [InlineData("ToolTranslateROM_SimpleToRom_Label")]
    [InlineData("ToolTranslateROM_TranslateData_Input")]
    [InlineData("ToolTranslateROM_TranslateData_Button")]
    [InlineData("ToolTranslateROM_TranslateData_Label")]
    [InlineData("ToolTranslateROM_ExtraFontRom_Input")]
    [InlineData("ToolTranslateROM_ExtraFontRom_Button")]
    [InlineData("ToolTranslateROM_ExtraFontRom_Label")]
    [InlineData("ToolTranslateROM_SimpleOverrideJpFont_Check")]
    [InlineData("ToolTranslateROM_SimpleFire_Button")]
    [InlineData("ToolTranslateROM_FromLanguage_Combo")]
    [InlineData("ToolTranslateROM_ToLanguage_Combo")]
    [InlineData("ToolTranslateROM_FromLanguage_Label")]
    [InlineData("ToolTranslateROM_ToLanguage_Label")]
    [InlineData("ToolTranslateROM_UseAutoTranslate_Check")]
    [InlineData("ToolTranslateROM_OneLiner_Check")]
    [InlineData("ToolTranslateROM_ModifiedOnly_Check")]
    [InlineData("ToolTranslateROM_DetailFromRom_Input")]
    [InlineData("ToolTranslateROM_DetailToRom_Input")]
    [InlineData("ToolTranslateROM_DetailFromRom_Button")]
    [InlineData("ToolTranslateROM_DetailToRom_Button")]
    [InlineData("ToolTranslateROM_DetailFromRom_Label")]
    [InlineData("ToolTranslateROM_DetailToRom_Label")]
    [InlineData("ToolTranslateROM_ExportAllText_Button")]
    [InlineData("ToolTranslateROM_ImportAllText_Button")]
    [InlineData("ToolTranslateROM_OverrideJpFont_Check")]
    [InlineData("ToolTranslateROM_FontRom_Input")]
    [InlineData("ToolTranslateROM_FontRom_Button")]
    [InlineData("ToolTranslateROM_FontAutoGenerate_Check")]
    [InlineData("ToolTranslateROM_UseFontName_Input")]
    [InlineData("ToolTranslateROM_UseFontName_Button")]
    [InlineData("ToolTranslateROM_UseFontName_Label")]
    [InlineData("ToolTranslateROM_ImportFont_Button")]
    public void Axaml_HasExpectedAutomationId(string automationId)
    {
        string axamlSource = File.ReadAllText(AxamlPath());
        Assert.Contains($"AutomationId=\"{automationId}\"", axamlSource);
    }

    // -----------------------------------------------------------------
    // ViewModel - Initialize safety + defaults
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_Initialize_NoRom_DoesNotThrow()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ToolTranslateROMViewModel();
            // Must not throw; labels stay empty.
            vm.Initialize();
            Assert.Equal(string.Empty, vm.FromRomLabel);
            Assert.Equal(string.Empty, vm.ToRomLabel);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Defaults_MatchWf()
    {
        // WF constructor / Designer.cs defaults: useAutoTranslateCheckBox,
        // X_MODIFIED_TEXT_ONLY, FontAutoGenelateCheckBox all start checked;
        // the JP-font override checkboxes start unchecked.
        var vm = new ToolTranslateROMViewModel();
        Assert.True(vm.UseAutoTranslate);
        Assert.True(vm.ModifiedTextOnly);
        Assert.True(vm.FontAutoGenerate);
        Assert.False(vm.OneLinerCheck);
        Assert.False(vm.SimpleOverrideJpFont);
        Assert.False(vm.OverrideJpFont);
    }

    [Theory]
    // (is_multibyte, version) -> ShowJpFontOverride
    // WF rule: hide when (!is_multibyte && version >= 7)
    [InlineData(true, 8, true)]    // FE8J - multibyte = show
    [InlineData(false, 8, false)]  // FE8U - non-multibyte v8 = hide
    [InlineData(true, 7, true)]    // FE7J - multibyte = show
    [InlineData(false, 7, false)]  // FE7U - non-multibyte v7 = hide
    [InlineData(true, 6, true)]    // FE6 multibyte = show
    [InlineData(false, 6, true)]   // FE6 non-multibyte v6 = show (version < 7)
    public void ViewModel_ShowJpFontOverride_FollowsWfRule(bool isMultibyte, int version, bool expected)
    {
        Assert.Equal(expected,
            ToolTranslateROMViewModel.CalcShowJpFontOverride(isMultibyte, version));
    }

    // -----------------------------------------------------------------
    // ViewModel - MakeROMName parity (FE7/FE8 x multibyte/non-multibyte)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_Initialize_PopulatesRomLabels_FE8U()
    {
        // FE8U is version 8, non-multibyte. WF: FROM="無改造 FE8U", TO="無改造 FE8J".
        ROM rom = MakeRom(version: 8, isMultibyte: false);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ToolTranslateROMViewModel();
            vm.Initialize();
            Assert.Equal("無改造 FE8U", vm.FromRomLabel);
            Assert.Equal("無改造 FE8J", vm.ToRomLabel);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Initialize_PopulatesRomLabels_FE8J()
    {
        ROM rom = MakeRom(version: 8, isMultibyte: true);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ToolTranslateROMViewModel();
            vm.Initialize();
            Assert.Equal("無改造 FE8J", vm.FromRomLabel);
            Assert.Equal("無改造 FE8U", vm.ToRomLabel);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Initialize_PopulatesRomLabels_FE7J()
    {
        // WF: when FE7 + multibyte (FE7J), FROM=FE7U / TO=FE7J. Mirrors WF MakeROMName.
        ROM rom = MakeRom(version: 7, isMultibyte: true);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ToolTranslateROMViewModel();
            vm.Initialize();
            Assert.Equal("無改造 FE7U", vm.FromRomLabel);
            Assert.Equal("無改造 FE7J", vm.ToRomLabel);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_Initialize_PopulatesRomLabels_FE7U()
    {
        // WF: when FE7 + non-multibyte (FE7U), FROM=FE7J / TO=FE7U. Mirrors WF MakeROMName.
        ROM rom = MakeRom(version: 7, isMultibyte: false);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ToolTranslateROMViewModel();
            vm.Initialize();
            Assert.Equal("無改造 FE7J", vm.FromRomLabel);
            Assert.Equal("無改造 FE7U", vm.ToRomLabel);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel - language-default index parity with WF TranslateLanguageAutoSelect
    // -----------------------------------------------------------------

    [Theory]
    // (is_multibyte, textEncoding, lang, expectedFrom, expectedTo)
    // FROM rule: multibyte+ZH_TBL => 2; multibyte+other => 0; non-multibyte => 1.
    // TO rule: lang "zh" => 2; lang "en" => 1; otherwise => 0.
    // "If from == to, swap to to its peer (0 <-> 1)."
    [InlineData(false, TextEncodingEnum.Auto, "ja", 1, 0)]   // FE8U + jp lang => from=en, to=ja
    [InlineData(false, TextEncodingEnum.Auto, "en", 1, 0)]   // FE8U + en lang => from=en, to=en => collision => to=ja
    [InlineData(false, TextEncodingEnum.Auto, "zh", 1, 2)]   // FE8U + zh lang => from=en, to=zh
    [InlineData(true,  TextEncodingEnum.Auto, "ja", 0, 1)]   // FE8J + jp lang => from=ja, to=ja => collision => to=en
    [InlineData(true,  TextEncodingEnum.Auto, "en", 0, 1)]   // FE8J + en lang => from=ja, to=en
    [InlineData(true,  TextEncodingEnum.ZH_TBL, "zh", 2, 0)] // FE8J zh_tbl + zh lang => from=zh, to=zh => collision => to=ja
    public void ViewModel_CalcDefaultLanguageIndexes_MatchesWfLogic(
        bool isMultibyte, TextEncodingEnum textEncoding, string lang,
        int expectedFrom, int expectedTo)
    {
        var (from, to) = ToolTranslateROMViewModel.CalcDefaultLanguageIndexes(
            isMultibyte, textEncoding, lang);
        Assert.Equal(expectedFrom, from);
        Assert.Equal(expectedTo, to);
    }

    // -----------------------------------------------------------------
    // Deferred-button policy - all real-action buttons disabled + tooltip #536
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("ToolTranslateROM_SimpleFire_Button")]
    [InlineData("ToolTranslateROM_ExportAllText_Button")]
    [InlineData("ToolTranslateROM_ImportAllText_Button")]
    [InlineData("ToolTranslateROM_UseFontName_Button")]
    [InlineData("ToolTranslateROM_ImportFont_Button")]
    public void View_DeferredButton_IsDisabledAndReferencesFollowupIssue(string automationId)
    {
        var doc = XDocument.Load(AxamlPath());

        // Avalonia attached properties show up in XLinq as a single LocalName
        // that contains the dot - e.g. `AutomationProperties.AutomationId` or
        // `ToolTip.Tip`. So we match the LocalName directly.
        var button = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Button"
                && e.Attributes().Any(a =>
                    a.Name.LocalName == "AutomationProperties.AutomationId"
                    && a.Value == automationId));

        Assert.NotNull(button);

        // IsEnabled must be False (literal).
        var isEnabled = button!.Attribute("IsEnabled");
        Assert.NotNull(isEnabled);
        Assert.Equal("False", isEnabled!.Value);

        // ToolTip.Tip must reference issue #536.
        var toolTip = button.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "ToolTip.Tip");
        Assert.NotNull(toolTip);
        Assert.Contains("#536", toolTip!.Value);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeRom(int version, bool isMultibyte)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        // ROMFE detection via Rom.LoadLow: matches IndexOf on the version string.
        // Codes available in this project: BE8E01 (FE8U), BE8J01 (FE8J),
        // AE7E01 (FE7U), AE7J01 (FE7J), AFEJ01 (FE6J). There is no FE6 US
        // variant - the test caller never asks for (6, false).
        string gameCode = (version, isMultibyte) switch
        {
            (8, false) => "BE8E01",
            (8, true)  => "BE8J01",
            (7, false) => "AE7E01",
            (7, true)  => "AE7J01",
            (6, true)  => "AFEJ01",
            _ => throw new ArgumentException(
                $"Unsupported (version={version}, isMultibyte={isMultibyte}) - no matching ROMFE class"),
        };
        rom.LoadLow($"synthetic-{gameCode}.gba", bytes, gameCode);
        return rom;
    }

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ToolTranslateROMView.axaml");
    }

    /// <summary>Walk parents from test bin dir until we find FEBuilderGBA.sln.</summary>
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
