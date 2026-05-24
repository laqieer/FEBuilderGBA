// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep regression tests for MapSettingFE6View. (#389)
//
// Before this PR the WinForms `MapSettingFE6Form` had 126 controls and 65
// distinct labels while the Avalonia counterpart `MapSettingFE6View` was a
// 3-control address-list shell (-97.6 % density, 65 WF-only labels, 50 ROM
// writes without UndoService). The plan accepted on issue #389 rebuilds the
// AXAML surface so it reaches the LOW verdict (within 25 % of WF) and wraps
// the write handler in UndoService.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the MapSettingFE6 parity raise (#389) is permanent.
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap.
/// </summary>
[Collection("SharedState")]
public class MapSettingFE6ParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach LOW verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 126 control instantiations (per 2026-05-21
    /// density sweep). To enter the LOW band we need
    /// AV >= ceil(126 * 0.75) = 95.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveLowVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 126;
        int lowThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 95
        Assert.True(avCount >= lowThreshold,
            $"AV control count {avCount} must be >= {lowThreshold} " +
            $"(75 % of WF={WfControlCount}) to enter the LOW verdict.");
    }

    // -----------------------------------------------------------------
    // AutomationIds — every field surfaces a stable id so UI tests can drive it.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasAllExpectedAutomationIds()
    {
        string axaml = ReadAxaml();

        // CP pointer (offset 0)
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_D0_Input\"", axaml);

        // Section 2 — Map Style / PLIST (W4, B6..B11)
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_W4_Input\"", axaml);
        for (int i = 6; i <= 11; i++)
            Assert.Contains($"AutomationProperties.AutomationId=\"MapSettingFE6_B{i}_Input\"", axaml);

        // Section 3 — Map Properties (B12..B19)
        for (int i = 12; i <= 19; i++)
            Assert.Contains($"AutomationProperties.AutomationId=\"MapSettingFE6_B{i}_Input\"", axaml);

        // Section 4 — BGM (B20..B24)
        for (int i = 20; i <= 24; i++)
            Assert.Contains($"AutomationProperties.AutomationId=\"MapSettingFE6_B{i}_Input\"", axaml);

        // Section 5 — Breakable wall + Asset ratings byte slots (B25..B31)
        for (int i = 25; i <= 31; i++)
            Assert.Contains($"AutomationProperties.AutomationId=\"MapSettingFE6_B{i}_Input\"", axaml);

        // Section 5 cont. — Experience + Strategy rating word slots (W32..W46 step 2)
        foreach (int off in new[] { 32, 34, 36, 38, 40, 42, 44, 46 })
            Assert.Contains($"AutomationProperties.AutomationId=\"MapSettingFE6_W{off}_Input\"", axaml);

        // Section 6 — Text IDs (W48..W56 step 2)
        foreach (int off in new[] { 48, 50, 52, 54, 56 })
            Assert.Contains($"AutomationProperties.AutomationId=\"MapSettingFE6_W{off}_Input\"", axaml);

        // Section 7 — World Map / Event (B58, B59, W60, B62..B66)
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_B58_Input\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_B59_Input\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_W60_Input\"", axaml);
        for (int i = 62; i <= 66; i++)
            Assert.Contains($"AutomationProperties.AutomationId=\"MapSettingFE6_B{i}_Input\"", axaml);

        // Section 8 — Victory BGM (B67)
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_B67_Input\"", axaml);

        // Write button + address/size labels
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_Write_Button\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_Addr_Label\"", axaml);
        Assert.Contains("AutomationProperties.AutomationId=\"MapSettingFE6_Size_Label\"", axaml);
    }

    // -----------------------------------------------------------------
    // WF-correct labels — verify the semantic text appears in AXAML.
    // -----------------------------------------------------------------

    /// <summary>
    /// The labels-sweep report flagged 65 WF-only labels. Assert that the
    /// MOST IMPORTANT WF semantic labels appear in the AXAML so the sweep
    /// detects them on the next regeneration.
    /// </summary>
    [Fact]
    public void View_HasWfOnlyLabels_Covered()
    {
        string axaml = ReadAxaml();
        // Core field labels (from MapSettingFE6Form.Designer.cs J_*.Text)
        Assert.Contains("CP", axaml);
        Assert.Contains("オブジェクトタイプ(Plist)", axaml);
        Assert.Contains("パレット(Plist)", axaml);
        Assert.Contains("チップセットクタイプ(Plist)", axaml);
        Assert.Contains("マップポインタ(Plist)", axaml);
        Assert.Contains("タイルアニメーション1", axaml);
        Assert.Contains("タイルアニメーション2", axaml);
        Assert.Contains("マップ部分変更(Plist)", axaml);
        Assert.Contains("霧レベル", axaml);
        Assert.Contains("戦闘準備の有無", axaml);
        Assert.Contains("章タイトル画像", axaml);
        Assert.Contains("初期座標", axaml);
        Assert.Contains("天気", axaml);
        Assert.Contains("戦闘背景", axaml);
        Assert.Contains("ハードブースト", axaml);
        Assert.Contains("味方フェーズBGM", axaml);
        Assert.Contains("敵フェーズBGM", axaml);
        Assert.Contains("友軍BGM", axaml);
        Assert.Contains("ワールドマップBGM", axaml);
        Assert.Contains("章オープニングBGM", axaml);
        Assert.Contains("壊れる壁HP", axaml);
        Assert.Contains("攻略評価A", axaml);
        Assert.Contains("攻略評価B", axaml);
        Assert.Contains("攻略評価C", axaml);
        Assert.Contains("攻略評価D", axaml);
        Assert.Contains("経験評価A", axaml);
        Assert.Contains("経験評価B", axaml);
        Assert.Contains("経験評価C", axaml);
        Assert.Contains("経験評価D", axaml);
        Assert.Contains("戦力評価A", axaml);
        Assert.Contains("戦力評価B", axaml);
        Assert.Contains("戦力評価C", axaml);
        Assert.Contains("戦力評価D", axaml);
        Assert.Contains("クリア条件(表示のみ)", axaml);
        Assert.Contains("上の軍", axaml);
        Assert.Contains("下の軍", axaml);
        Assert.Contains("敵の軍旗", axaml);
        Assert.Contains("章タイトル", axaml);
        Assert.Contains("イベントID(Plist)", axaml);
        Assert.Contains("ワールドマップ自動イベント", axaml);
        Assert.Contains("ワールドマップ地名", axaml);
        Assert.Contains("Chapter Number", axaml);
        Assert.Contains("ワールドマップX", axaml);
        Assert.Contains("ワールドマップY", axaml);
        Assert.Contains("ワールドマップポイントX", axaml);
        Assert.Contains("ワールドマップポイントY", axaml);
        Assert.Contains("勝利BGMに変わる敵数", axaml);
        // Coordinate slot labels
        Assert.Contains("X:", axaml);
        Assert.Contains("Y:", axaml);
        // Known-gap placeholder literals (song play marker + jump labels)
        Assert.Contains("♪", axaml);
        Assert.Contains("マップエディタへJump", axaml);
        Assert.Contains("離脱ポイントへJump", axaml);
        // Write button + sizing label
        Assert.Contains("書き込み", axaml);
        Assert.Contains("Size:", axaml);
    }

    // -----------------------------------------------------------------
    // Offset-to-label adjacency — verify each critical input is paired with
    // the WF-correct semantic label in the AXAML. Pure substring asserts
    // (above) would pass even if a B17/B18/B19 label swap happened. This
    // test walks every Grid in the AXAML and asserts that the TextBlock
    // immediately to the left of each named NumericUpDown has the WF-correct
    // label text. Catches accidental label / offset mismatches.
    // -----------------------------------------------------------------

    [Theory]
    // (input control name, expected adjacent label literal)
    // The labels come from MapSettingFE6Form.Designer.cs J_*.Text and
    // label*.Text assignments at the matching layout positions. See the
    // "Verified offset/label mapping" table in the plan comment on issue
    // #389 for the full mapping.
    [InlineData("NW4", "オブジェクトタイプ(Plist)")]
    [InlineData("NB6", "パレット(Plist)")]
    [InlineData("NB7", "チップセットクタイプ(Plist)")]
    [InlineData("NB8", "マップポインタ(Plist)")]
    [InlineData("NB9", "タイルアニメーション1")]
    [InlineData("NB10", "タイルアニメーション2")]
    [InlineData("NB11", "マップ部分変更(Plist)")]
    [InlineData("NB12", "霧レベル")]
    [InlineData("NB13", "戦闘準備の有無")]
    [InlineData("NB14", "章タイトル画像")]
    [InlineData("NB17", "天気")]
    [InlineData("NB18", "戦闘背景")]
    [InlineData("NB19", "ハードブースト")]
    [InlineData("NB20", "味方フェーズBGM")]
    [InlineData("NB21", "敵フェーズBGM")]
    [InlineData("NB22", "友軍BGM")]
    [InlineData("NB23", "ワールドマップBGM")]
    [InlineData("NB24", "章オープニングBGM")]
    [InlineData("NB25", "壊れる壁HP")]
    [InlineData("NB27", "攻略評価A")]
    [InlineData("NB28", "攻略評価B")]
    [InlineData("NB29", "攻略評価C")]
    [InlineData("NB30", "攻略評価D")]
    [InlineData("NW32", "経験評価A")]
    [InlineData("NW34", "経験評価B")]
    [InlineData("NW36", "経験評価C")]
    [InlineData("NW38", "経験評価D")]
    [InlineData("NW40", "戦力評価A")]
    [InlineData("NW42", "戦力評価B")]
    [InlineData("NW44", "戦力評価C")]
    [InlineData("NW46", "戦力評価D")]
    [InlineData("NW48", "クリア条件(表示のみ)")]
    [InlineData("NW50", "上の軍")]
    [InlineData("NW52", "下の軍")]
    [InlineData("NW54", "敵の軍旗")]
    [InlineData("NW56", "章タイトル")]
    [InlineData("NB58", "イベントID(Plist)")]
    [InlineData("NB59", "ワールドマップ自動イベント")]
    [InlineData("NW60", "ワールドマップ地名")]
    [InlineData("NB62", "Chapter Number")]
    [InlineData("NB63", "ワールドマップX")]
    [InlineData("NB64", "ワールドマップY")]
    [InlineData("NB65", "ワールドマップポイントX")]
    [InlineData("NB66", "ワールドマップポイントY")]
    [InlineData("NB67", "勝利BGMに変わる敵数")]
    public void View_HasWfCorrectLabels_AtCorrectOffsets(string inputName, string expectedLabel)
    {
        // Walk the AXAML XML and verify the input's PRECEDING-SIBLING
        // TextBlock literal matches the expected WF label.
        var doc = XDocument.Load(AxamlPath());
        // Avalonia XAML namespace
        var avNs = doc.Root.GetDefaultNamespace();

        // Find the NumericUpDown whose Name attribute matches inputName
        var input = doc.Descendants(avNs + "NumericUpDown")
            .FirstOrDefault(e => (string?)e.Attribute("Name") == inputName);
        Assert.True(input != null,
            $"NumericUpDown named '{inputName}' not found in AXAML.");

        // The label is the TextBlock IN THE SAME PARENT Grid that sits at the
        // same Grid.Row and Grid.Column = (input's column - 1). This catches
        // any accidental swap between an input and its label.
        var parent = input.Parent;
        Assert.True(parent != null, $"Input '{inputName}' has no parent.");

        int inputRow = ParseGridIndex(input, "Grid.Row");
        int inputCol = ParseGridIndex(input, "Grid.Column");
        int expectedLabelCol = inputCol - 1;
        Assert.True(expectedLabelCol >= 0,
            $"Input '{inputName}' must not be at Grid.Column=0; expected " +
            $"adjacent label slot at column {expectedLabelCol}.");

        var sibling = parent.Elements(avNs + "TextBlock")
            .FirstOrDefault(e =>
                ParseGridIndex(e, "Grid.Row") == inputRow &&
                ParseGridIndex(e, "Grid.Column") == expectedLabelCol);
        Assert.True(sibling != null,
            $"No TextBlock at row={inputRow}, col={expectedLabelCol} adjacent " +
            $"to '{inputName}' (expected WF label '{expectedLabel}').");

        string actualLabel = (string?)sibling.Attribute("Text") ?? "";
        Assert.True(actualLabel.Contains(expectedLabel),
            $"Adjacent label for '{inputName}' should contain '{expectedLabel}'. " +
            $"Got: '{actualLabel}'");
    }

    static int ParseGridIndex(XElement element, string attributeName)
    {
        var attr = element.Attribute(attributeName);
        if (attr == null) return 0;
        return int.TryParse(attr.Value, out int v) ? v : 0;
    }

    // -----------------------------------------------------------------
    // Code-behind / write-handler assertions.
    // -----------------------------------------------------------------

    /// <summary>
    /// Write handler MUST wrap the VM write call in
    /// `_undoService.Begin / Commit / Rollback` so the ROM mutation is
    /// undoable atomically.
    /// </summary>
    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string code = File.ReadAllText(CodeBehindPath());
        Assert.Contains("_undoService", code);
        Assert.Matches(new Regex(
            @"_undoService\.Begin\([^)]*\)[\s\S]*?WriteMapSetting\(\)[\s\S]*?_undoService\.(Commit|Rollback)\(\)",
            RegexOptions.Singleline), code);
    }

    /// <summary>
    /// CP / Pointer (D0) is the only TextBox in the editor that accepts a
    /// raw hex literal — the rest are NumericUpDown widgets that constrain
    /// input to numbers. A typo in the CP TextBox must be rejected (with a
    /// user-visible error) so the editor cannot silently write 0 to the ROM.
    /// (PR #604 Copilot CLI review blocker #3.)
    /// </summary>
    [Fact]
    public void View_WriteHandler_RejectsInvalidCpPointer()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // ReadUIToVM must use TryParseHexText (not ParseHexText) for D0 and
        // show an error + return false on parse failure.
        Assert.Contains("TryParseHexText(ND0.Text", code);
        Assert.Contains("ShowError", code);
    }

    /// <summary>
    /// After a successful write, the editor reloads from ROM. The reload
    /// must run inside an `IsLoading=true` guard so `ViewModelBase.SetField`
    /// does not re-dirty the VM right after `MarkClean()`, leaving the UI
    /// stuck in a "modified" state. (PR #604 Copilot bot inline review #1.)
    /// </summary>
    [Fact]
    public void View_WriteHandler_ReloadWrappedInIsLoadingGuard()
    {
        string code = File.ReadAllText(CodeBehindPath());
        // The reload between Commit and MarkClean must set IsLoading=true.
        Assert.Matches(new Regex(
            @"_undoService\.Commit\(\)[\s\S]*?_vm\.IsLoading\s*=\s*true[\s\S]*?_vm\.LoadEntry\([\s\S]*?_vm\.IsLoading\s*=\s*false[\s\S]*?_vm\.MarkClean\(\)",
            RegexOptions.Singleline), code);
    }

    // -----------------------------------------------------------------
    // ViewModel behavior tests (synthetic FE6 ROM).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadEntry_ReadsAllBytes_68ByteVariant()
    {
        var (rom, mapBase) = MakeFE6Rom(dataSize: 68);
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapSettingFE6ViewModel();
            vm.LoadEntry(mapBase);

            Assert.Equal((uint)68, vm.DataSize);
            Assert.Equal((uint)0x08123456, vm.CpPointer);
            Assert.Equal((uint)0x1234, vm.ObjectTypePLIST);
            Assert.Equal((uint)0x06, vm.PalettePLIST);
            Assert.Equal((uint)0x07, vm.ChipsetConfigPLIST);
            Assert.Equal((uint)0x08, vm.MapPointerPLIST);
            Assert.Equal((uint)0x09, vm.TileAnimation1PLIST);
            Assert.Equal((uint)0x0A, vm.TileAnimation2PLIST);
            Assert.Equal((uint)0x0B, vm.MapChangePLIST);
            Assert.Equal((uint)0x0C, vm.FogLevel);
            Assert.Equal((uint)0x0D, vm.BattlePreparation);
            Assert.Equal((uint)0x0E, vm.ChapterTitleImage);
            Assert.Equal((uint)0x0F, vm.UnknownB15);
            Assert.Equal((uint)0x10, vm.UnknownB16);
            Assert.Equal((uint)0x11, vm.UnknownB17);
            Assert.Equal((uint)0x12, vm.Weather);
            Assert.Equal((uint)0x13, vm.BattleBGLookup);
            Assert.Equal((uint)0x14, vm.PlayerPhaseBGM);
            Assert.Equal((uint)0x15, vm.EnemyPhaseBGM);
            Assert.Equal((uint)0x16, vm.NpcPhaseBGM);
            Assert.Equal((uint)0x17, vm.HardBoost);
            Assert.Equal((uint)0x18, vm.UnknownB24);
            Assert.Equal((uint)0x19, vm.BreakableWallHP);
            Assert.Equal((uint)0x1A, vm.UnknownB26);
            Assert.Equal((uint)0x1B, vm.UnknownB27);
            Assert.Equal((uint)0x1C, vm.UnknownB28);
            Assert.Equal((uint)0x1D, vm.UnknownB29);
            Assert.Equal((uint)0x1E, vm.UnknownB30);
            Assert.Equal((uint)0x1F, vm.UnknownB31);
            // Little-endian: bytes[N]=lo, bytes[N+1]=hi → u16 = (hi<<8)|lo
            Assert.Equal((uint)0x2120, vm.PlayerPhaseBGMW);
            Assert.Equal((uint)0x2322, vm.EnemyPhaseBGMW);
            Assert.Equal((uint)0x2524, vm.NpcPhaseBGMW);
            Assert.Equal((uint)0x2726, vm.UnknownW38);
            Assert.Equal((uint)0x2928, vm.UnknownW40);
            Assert.Equal((uint)0x2B2A, vm.UnknownW42);
            Assert.Equal((uint)0x2D2C, vm.UnknownW44);
            Assert.Equal((uint)0x2F2E, vm.UnknownW46);
            Assert.Equal((uint)0x3130, vm.ClearConditionText);
            Assert.Equal((uint)0x3332, vm.UpperArmyText);
            Assert.Equal((uint)0x3534, vm.LowerArmyText);
            Assert.Equal((uint)0x3736, vm.EnemyBannerFlag);
            Assert.Equal((uint)0x3938, vm.ChapterTitleText);
            Assert.Equal((uint)0x3A, vm.EventIdPLIST);
            Assert.Equal((uint)0x3B, vm.WorldMapAutoEvent);
            Assert.Equal((uint)0x3D3C, vm.WorldMapPlaceName);
            Assert.Equal((uint)0x3E, vm.ChapterNumber);
            Assert.Equal((uint)0x3F, vm.WorldMapX);
            Assert.Equal((uint)0x40, vm.WorldMapY);
            Assert.Equal((uint)0x41, vm.WorldMapPointX);
            Assert.Equal((uint)0x42, vm.WorldMapPointY);
            Assert.Equal((uint)0x43, vm.VictoryBGMEnemyCount);
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_WriteMapSetting_RoundTrips_AllBytes_68ByteVariant()
    {
        var (rom, mapBase) = MakeFE6Rom(dataSize: 68);
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapSettingFE6ViewModel();
            vm.LoadEntry(mapBase);

            // Wipe and rewrite every field with distinctive sentinels.
            vm.CpPointer = 0x08AABBCC;
            vm.ObjectTypePLIST = 0xBEEF;
            vm.PalettePLIST = 0xA1;
            vm.ChipsetConfigPLIST = 0xA2;
            vm.MapPointerPLIST = 0xA3;
            vm.TileAnimation1PLIST = 0xA4;
            vm.TileAnimation2PLIST = 0xA5;
            vm.MapChangePLIST = 0xA6;
            vm.FogLevel = 0xA7;
            vm.BattlePreparation = 0xA8;
            vm.ChapterTitleImage = 0xA9;
            vm.UnknownB15 = 0xAA;
            vm.UnknownB16 = 0xAB;
            vm.UnknownB17 = 0xAC;
            vm.Weather = 0xAD;
            vm.BattleBGLookup = 0xAE;
            vm.PlayerPhaseBGM = 0xAF;
            vm.EnemyPhaseBGM = 0xB0;
            vm.NpcPhaseBGM = 0xB1;
            vm.HardBoost = 0xB2;
            vm.UnknownB24 = 0xB3;
            vm.BreakableWallHP = 0xB4;
            vm.UnknownB26 = 0xB5;
            vm.UnknownB27 = 0xB6;
            vm.UnknownB28 = 0xB7;
            vm.UnknownB29 = 0xB8;
            vm.UnknownB30 = 0xB9;
            vm.UnknownB31 = 0xBA;
            vm.PlayerPhaseBGMW = 0xCAFE;
            vm.EnemyPhaseBGMW = 0xDEAD;
            vm.NpcPhaseBGMW = 0xFACE;
            vm.UnknownW38 = 0xF00D;
            vm.UnknownW40 = 0xC0DE;
            vm.UnknownW42 = 0xB00B;
            vm.UnknownW44 = 0xBABE;
            vm.UnknownW46 = 0xFEED;
            vm.ClearConditionText = 0x1111;
            vm.UpperArmyText = 0x2222;
            vm.LowerArmyText = 0x3333;
            vm.EnemyBannerFlag = 0x4444;
            vm.ChapterTitleText = 0x5555;
            vm.EventIdPLIST = 0xC1;
            vm.WorldMapAutoEvent = 0xC2;
            vm.WorldMapPlaceName = 0x9999;
            vm.ChapterNumber = 0xC3;
            vm.WorldMapX = 0xC4;
            vm.WorldMapY = 0xC5;
            vm.WorldMapPointX = 0xC6;
            vm.WorldMapPointY = 0xC7;
            vm.VictoryBGMEnemyCount = 0xC8;

            vm.WriteMapSetting();

            // Verify every byte at the documented offset.
            Assert.Equal((uint)0x08AABBCC, rom.u32(mapBase + 0));
            Assert.Equal((uint)0xBEEF, rom.u16(mapBase + 4));
            Assert.Equal((uint)0xA1, rom.u8(mapBase + 6));
            Assert.Equal((uint)0xA2, rom.u8(mapBase + 7));
            Assert.Equal((uint)0xA3, rom.u8(mapBase + 8));
            Assert.Equal((uint)0xA4, rom.u8(mapBase + 9));
            Assert.Equal((uint)0xA5, rom.u8(mapBase + 10));
            Assert.Equal((uint)0xA6, rom.u8(mapBase + 11));
            Assert.Equal((uint)0xA7, rom.u8(mapBase + 12));
            Assert.Equal((uint)0xA8, rom.u8(mapBase + 13));
            Assert.Equal((uint)0xA9, rom.u8(mapBase + 14));
            Assert.Equal((uint)0xAA, rom.u8(mapBase + 15));
            Assert.Equal((uint)0xAB, rom.u8(mapBase + 16));
            Assert.Equal((uint)0xAC, rom.u8(mapBase + 17));
            Assert.Equal((uint)0xAD, rom.u8(mapBase + 18));
            Assert.Equal((uint)0xAE, rom.u8(mapBase + 19));
            Assert.Equal((uint)0xAF, rom.u8(mapBase + 20));
            Assert.Equal((uint)0xB0, rom.u8(mapBase + 21));
            Assert.Equal((uint)0xB1, rom.u8(mapBase + 22));
            Assert.Equal((uint)0xB2, rom.u8(mapBase + 23));
            Assert.Equal((uint)0xB3, rom.u8(mapBase + 24));
            Assert.Equal((uint)0xB4, rom.u8(mapBase + 25));
            Assert.Equal((uint)0xB5, rom.u8(mapBase + 26));
            Assert.Equal((uint)0xB6, rom.u8(mapBase + 27));
            Assert.Equal((uint)0xB7, rom.u8(mapBase + 28));
            Assert.Equal((uint)0xB8, rom.u8(mapBase + 29));
            Assert.Equal((uint)0xB9, rom.u8(mapBase + 30));
            Assert.Equal((uint)0xBA, rom.u8(mapBase + 31));
            Assert.Equal((uint)0xCAFE, rom.u16(mapBase + 32));
            Assert.Equal((uint)0xDEAD, rom.u16(mapBase + 34));
            Assert.Equal((uint)0xFACE, rom.u16(mapBase + 36));
            Assert.Equal((uint)0xF00D, rom.u16(mapBase + 38));
            Assert.Equal((uint)0xC0DE, rom.u16(mapBase + 40));
            Assert.Equal((uint)0xB00B, rom.u16(mapBase + 42));
            Assert.Equal((uint)0xBABE, rom.u16(mapBase + 44));
            Assert.Equal((uint)0xFEED, rom.u16(mapBase + 46));
            Assert.Equal((uint)0x1111, rom.u16(mapBase + 48));
            Assert.Equal((uint)0x2222, rom.u16(mapBase + 50));
            Assert.Equal((uint)0x3333, rom.u16(mapBase + 52));
            Assert.Equal((uint)0x4444, rom.u16(mapBase + 54));
            Assert.Equal((uint)0x5555, rom.u16(mapBase + 56));
            Assert.Equal((uint)0xC1, rom.u8(mapBase + 58));
            Assert.Equal((uint)0xC2, rom.u8(mapBase + 59));
            Assert.Equal((uint)0x9999, rom.u16(mapBase + 60));
            Assert.Equal((uint)0xC3, rom.u8(mapBase + 62));
            Assert.Equal((uint)0xC4, rom.u8(mapBase + 63));
            Assert.Equal((uint)0xC5, rom.u8(mapBase + 64));
            Assert.Equal((uint)0xC6, rom.u8(mapBase + 65));
            Assert.Equal((uint)0xC7, rom.u8(mapBase + 66));
            Assert.Equal((uint)0xC8, rom.u8(mapBase + 67));
        }
        finally { CoreState.ROM = prev; }
    }

    [Fact]
    public void ViewModel_WriteMapSetting_RoundTrips_AllBytes_72ByteVariant()
    {
        var (rom, mapBase) = MakeFE6Rom(dataSize: 72);
        var prev = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapSettingFE6ViewModel();
            vm.LoadEntry(mapBase);

            Assert.Equal((uint)72, vm.DataSize);

            // Mutate one field of each type and confirm offsets are correct.
            vm.FogLevel = 0x55;
            vm.PlayerPhaseBGMW = 0x1234;
            vm.VictoryBGMEnemyCount = 0xEE;
            vm.WriteMapSetting();

            Assert.Equal((uint)0x55, rom.u8(mapBase + 12));
            Assert.Equal((uint)0x1234, rom.u16(mapBase + 32));
            Assert.Equal((uint)0xEE, rom.u8(mapBase + 67));
        }
        finally { CoreState.ROM = prev; }
    }

    // -----------------------------------------------------------------
    // Undo de-duplication — when an ambient scope is active (View has
    // opened an UndoService), the VM must NOT push a redundant range-only
    // UndoData entry.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteMapSetting_SkipsLocalUndoPush_WhenAmbientScopeActive()
    {
        var (rom, mapBase) = MakeFE6Rom(dataSize: 68);
        var prev = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            int baselineUndoCount = CoreState.Undo.UndoBuffer.Count;

            // Simulate a View-level UndoService scope: open an ambient undo
            // capture, call WriteMapSetting, and confirm the VM does NOT
            // push its own redundant UndoData onto the buffer.
            var ambient = CoreState.Undo.NewUndoData("Ambient");
            using (ROM.BeginUndoScope(ambient))
            {
                var vm = new MapSettingFE6ViewModel();
                vm.LoadEntry(mapBase);
                vm.WriteMapSetting();
            }

            int afterCount = CoreState.Undo.UndoBuffer.Count;
            Assert.Equal(baselineUndoCount, afterCount);
        }
        finally
        {
            CoreState.ROM = prev;
            CoreState.Undo = prevUndo;
        }
    }

    [Fact]
    public void ViewModel_WriteMapSetting_PushesLocalUndo_WhenNoAmbientScope()
    {
        var (rom, mapBase) = MakeFE6Rom(dataSize: 68);
        var prev = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            int baselineUndoCount = CoreState.Undo.UndoBuffer.Count;

            var vm = new MapSettingFE6ViewModel();
            vm.LoadEntry(mapBase);
            vm.WriteMapSetting();

            // No ambient scope active — the VM must push its own UndoData
            // so the change remains undoable (legacy behavior preserved).
            Assert.True(CoreState.Undo.UndoBuffer.Count > baselineUndoCount,
                $"Expected UndoBuffer.Count to grow when no ambient scope is active. " +
                $"Was {baselineUndoCount}, became {CoreState.Undo.UndoBuffer.Count}.");
        }
        finally
        {
            CoreState.ROM = prev;
            CoreState.Undo = prevUndo;
        }
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE6 (AFEJ01) ROM with a single map-setting entry
    /// at a known address. The map_setting_pointer slot at 0x2BB20 is
    /// patched to resolve to the synthetic table base. <paramref name="dataSize"/>
    /// must be 68 or 72; the dataSize=72 marker (rom.u16(0x2BB12) == 0x2048)
    /// is set accordingly BEFORE the RomInfo recompute that <see cref="ROM.LoadLow"/>
    /// runs.
    /// </summary>
    static (ROM rom, uint mapBase) MakeFE6Rom(uint dataSize)
    {
        if (dataSize != 68 && dataSize != 72)
            throw new ArgumentException("dataSize must be 68 or 72.", nameof(dataSize));

        var rom = new ROM();
        byte[] data = new byte[0x800000];
        // Mark the 72-byte variant signature before loading. LoadLow runs
        // RomInfo construction which reads 0x2BB12.
        if (dataSize == 72)
        {
            data[0x2BB12] = 0x48;
            data[0x2BB13] = 0x20;
        }
        rom.LoadLow("synth.gba", data, "AFEJ01");

        uint mapBase = 0x200000;
        int mapBaseInt = checked((int)mapBase);
        // Patch the map_setting_pointer slot at 0x2BB20.
        WriteU32(rom.Data, 0x2BB20, 0x08000000u | mapBase);

        // Seed the entry with sentinel bytes at every offset. u32 at 0
        // must be a valid GBA pointer so IsMapSettingValid accepts it.
        WriteU32(rom.Data, mapBaseInt + 0, 0x08123456);
        for (int i = 4; i < (int)dataSize; i++)
            rom.Data[mapBaseInt + i] = (byte)i;
        // Clear weather byte 12 — IsMapSettingValid checks weather < 0xE.
        rom.Data[mapBaseInt + 12] = 0x0C;
        // PLISTs at 4 must be readable.
        // u16 at 4..5 → ObjectTypePLIST = 0x1234 for the round-trip read
        // test; set explicitly (little-endian: low byte at lower offset).
        rom.Data[mapBaseInt + 4] = 0x34;
        rom.Data[mapBaseInt + 5] = 0x12;
        return (rom, mapBase);
    }

    /// <summary>
    /// Write a little-endian u32 at the given byte-array offset. Uses
    /// <c>int</c> for the offset so the array indexer is unambiguously
    /// integer-indexed.
    /// </summary>
    static void WriteU32(byte[] data, int addr, uint value)
    {
        data[addr + 0] = (byte)(value >> 0);
        data[addr + 1] = (byte)(value >> 8);
        data[addr + 2] = (byte)(value >> 16);
        data[addr + 3] = (byte)(value >> 24);
    }

    static void WriteU32(byte[] data, uint addr, uint value)
        => WriteU32(data, checked((int)addr), value);

    static string AxamlPath() => Path.Combine(AvaloniaDir, "Views", "MapSettingFE6View.axaml");
    static string CodeBehindPath() => Path.Combine(AvaloniaDir, "Views", "MapSettingFE6View.axaml.cs");

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
