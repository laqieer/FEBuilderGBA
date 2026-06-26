// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep regression tests for MonsterWMapProbabilityViewerView. (#1464)
//
// The WinForms `MonsterWMapProbabilityForm` has FOUR editing surfaces in one form
// (base point list / stage spread / per-base probabilities / skirmish events).
// Before this PR the Avalonia counterpart `MonsterWMapProbabilityViewerView` only
// covered the base-point list, so FE8 users could not edit world-map skirmish
// spawn probabilities, the per-stage monster spread, or the skirmish start/end
// event pointers. This test suite locks in the three restored surfaces.

using System;
using System.IO;
using System.Text.RegularExpressions;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the MonsterWMapProbabilityForm parity raise (#1464) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM.
/// </summary>
[Collection("SharedState")]
public class MonsterWMapProbabilityParityTests
{
    // -----------------------------------------------------------------
    // AXAML surface assertions.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasStageSpreadSurface()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_Stage_List\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_StageMapId_Input\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_StageWrite_Button\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_Stage_Combo\"", axaml);
    }

    [Fact]
    public void View_HasProbabilitySurface_With9CellsAndSum()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_Prob_List\"", axaml);
        for (int i = 0; i < 9; i++)
        {
            Assert.Contains($"AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_Prob{i}_Input\"", axaml);
        }
        // Live SUM% label (mirrors WF N2_SUM -> SUM label).
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_ProbSum_Label\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_ProbWrite_Button\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_Prob_Combo\"", axaml);
    }

    [Fact]
    public void View_HasSkirmishEventSurface_WithJumpButtons()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_SkirmishStart_Input\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_SkirmishEnd_Input\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_SkirmishStartJump_Button\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_SkirmishEndJump_Button\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MonsterWMapProbabilityViewer_SkirmishWrite_Button\"", axaml);
    }

    // -----------------------------------------------------------------
    // Code-behind assertions.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AllWriteHandlers_UseUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"void StageWrite_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?WriteStage\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
        Assert.Matches(new Regex(
            @"void ProbWrite_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?WriteProbability\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
        Assert.Matches(new Regex(
            @"void SkirmishWrite_Click[\s\S]*?_undoService\.Begin\([^)]*\)[\s\S]*?WriteSkirmishEvents\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    [Fact]
    public void View_SkirmishJump_FlagsWorldMapEventKind()
    {
        // The skirmish jump must stage the world-map event kind BEFORE NavigateTo,
        // matching WorldMapEventPointerView semantics (Copilot plan review #1).
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Matches(new Regex(
            @"SetEventKind\(\s*isWorldMapEvent:\s*true",
            RegexOptions.Singleline), code);
        Assert.Contains("Open<EventScriptView>()", code);
    }

    [Fact]
    public void View_FiltersAndRows_StayInSync()
    {
        // WinForms keeps N1_Filter<->N2_Filter and N1<->N2 rows in sync
        // (Copilot plan review #2).
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Contains("MirrorSelection", code);
        Assert.Matches(new Regex(@"ProbFilter\.SelectedIndex\s*=\s*StageFilter\.SelectedIndex", RegexOptions.Singleline), code);
        Assert.Matches(new Regex(@"StageFilter\.SelectedIndex\s*=\s*ProbFilter\.SelectedIndex", RegexOptions.Singleline), code);
        // Probability labels refresh after a base-point write (N2_BaseNameUpdate).
        Assert.Contains("RefreshProbLabels", code);
    }

    // -----------------------------------------------------------------
    // ListParityHelper coverage (Copilot plan review #3).
    // -----------------------------------------------------------------

    [Fact]
    public void ListParityHelper_DiscoversStageAndProbabilityRouteLists()
    {
        string code = File.ReadAllText(ListParityHelperPath());
        Assert.Contains("BuildMonsterWMapStageEirikaList", code);
        Assert.Contains("BuildMonsterWMapStageEphraimList", code);
        Assert.Contains("BuildMonsterWMapProbabilityEirikaList", code);
        Assert.Contains("BuildMonsterWMapProbabilityEphraimList", code);
    }

    // -----------------------------------------------------------------
    // ViewModel behavior tests (synthetic ROM).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_StageSpread_RoundTrips()
    {
        var (rom, stageEirikaBase, _) = MakeWMapRom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterWMapProbabilityViewerViewModel { StageIsEphraim = false };
            var list = vm.LoadStageList();
            Assert.Equal(MonsterWMapProbabilityCore.StageCount, list.Count);

            vm.LoadStage(stageEirikaBase + 2);
            Assert.Equal(rom.u8(stageEirikaBase + 2), vm.StageMapId);

            vm.StageMapId = 0x55;
            vm.WriteStage();
            Assert.Equal((uint)0x55, rom.u8(stageEirikaBase + 2));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_Probability_RoundTrips_AndSums()
    {
        var (rom, _, probEirikaBase) = MakeWMapRom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterWMapProbabilityViewerViewModel { ProbIsEphraim = false };
            var list = vm.LoadProbabilityList();
            Assert.Equal(MonsterWMapProbabilityCore.ProbabilityCount, list.Count);

            vm.LoadProbability(probEirikaBase + 9); // row #1 (stride 9)
            vm.Prob0 = 10; vm.Prob1 = 20; vm.Prob2 = 30; vm.Prob3 = 5; vm.Prob4 = 0;
            vm.Prob5 = 0; vm.Prob6 = 0; vm.Prob7 = 0; vm.Prob8 = 35;
            Assert.Equal("100%", vm.ProbSum);

            vm.WriteProbability();
            Assert.Equal((uint)10, rom.u8(probEirikaBase + 9 + 0));
            Assert.Equal((uint)35, rom.u8(probEirikaBase + 9 + 8));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_SkirmishEvents_RoundTrip()
    {
        var (rom, _, _) = MakeWMapRom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterWMapProbabilityViewerViewModel();
            vm.SkirmishStartEvent = 0x00112233u;
            vm.SkirmishEndEvent = 0x00445566u;
            vm.WriteSkirmishEvents();

            vm.SkirmishStartEvent = 0; vm.SkirmishEndEvent = 0;
            vm.LoadSkirmishEvents();
            Assert.Equal(0x00112233u, vm.SkirmishStartEvent);
            Assert.Equal(0x00445566u, vm.SkirmishEndEvent);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_IsSupported_TrueOnFE8()
    {
        var (rom, _, _) = MakeWMapRom();
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MonsterWMapProbabilityViewerViewModel();
            Assert.True(vm.IsSupported);
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM with the stage + probability tables planted
    /// and their pointer slots repointed.
    /// </summary>
    static (ROM rom, uint stageEirikaBase, uint probEirikaBase) MakeWMapRom()
    {
        var rom = new ROM();
        rom.LoadLow("synth-wmap.gba", new byte[0x1100000], "BE8E01");

        uint stageEirikaBase = 0x00900000u;
        uint stageEphraimBase = 0x00901000u;
        uint probEirikaBase = 0x00910000u;
        uint probEphraimBase = 0x00911000u;

        WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_stage_1_pointer, stageEirikaBase | 0x08000000u);
        WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_stage_2_pointer, stageEphraimBase | 0x08000000u);
        WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_probability_1_pointer, probEirikaBase | 0x08000000u);
        WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_probability_2_pointer, probEphraimBase | 0x08000000u);

        for (int i = 0; i < MonsterWMapProbabilityCore.StageCount; i++)
        {
            rom.Data[(int)(stageEirikaBase + i)] = (byte)(0x10 + i);
            rom.Data[(int)(stageEphraimBase + i)] = (byte)(0x40 + i);
        }

        return (rom, stageEirikaBase, probEirikaBase);
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string AxamlPath() => Path.Combine(AvaloniaDir, "Views", "MonsterWMapProbabilityViewerView.axaml");
    static string CodeBehindPath() => Path.Combine(AvaloniaDir, "Views", "MonsterWMapProbabilityViewerView.axaml.cs");
    static string ListParityHelperPath() => Path.Combine(AvaloniaDir, "Services", "ListParityHelper.cs");

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string AvaloniaDir
    {
        get
        {
            string baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "FEBuilderGBA.Avalonia");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new InvalidOperationException(
                $"Could not locate FEBuilderGBA.Avalonia/ from base {baseDir}");
        }
    }
}
