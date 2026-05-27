// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep #405 regression tests for ClassOPDemoView.
//
// Closes the 68-control gap + 32 WF-only labels surfaced on the orphan
// ClassOPDemoForm (HIGH density 71 / 3, -95.8 %). The orphan WF form is
// <Compile Remove>'d from the WinForms build, but the gap-sweep tooling
// still parses its Designer.cs. So parity here matches the orphan's
// actual surface, NOT the canonical OPClassDemoForm/OPClassDemoViewerView
// pair PR #544 built.
//
// Key differences from PR #544's OPClassDemoParityTests:
//   - N1 has NO 16-entry cap (orphan validator stops only at 0xFF).
//   - N2 is a SINGLE 6-byte tuple (orphan validator is `i < 1`), not a
//     repeating (Cmd, Arg) command stream.
//   - The 3-choice combo (`01=Normal`, `02=Critical`, `03=Ranged/Magic Sword`)
//     binds to the special-spec byte B2, not the command byte B0.
//   - No patch-aware UI (orphan constructor has no PatchUtil calls).
//   - List-expand button lives on the N1 sub-panel (`N1_AddressListExpandsButton`
//     in WF), not on the main table.
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

[Collection("SharedState")]
public class ClassOPDemoParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ClassOPDemoView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 71;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 54
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Phase 5 — control surface assertions (static AXAML inspection).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasFilterAndReloadBar()
    {
        string axaml = ReadAxaml();
        // #668: NUD-based ReadStart/ReadCount inputs became read-only
        // TextBlock slots inside the unified EditorTopBar control. The
        // *_Input AutomationIds are now *_Label; the ReloadList_Button id
        // is preserved.
        Assert.Contains("AutomationId=\"ClassOPDemo_ReadStart_Label\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_ReadCount_Label\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_ReloadList_Button\"", axaml);
        // The Reload routed event now bubbles via EditorTopBar; the
        // host code-behind wires it through OnTopBarReloadRequested.
        Assert.Contains("ReloadRequested=\"OnTopBarReloadRequested\"", axaml);
    }

    [Fact]
    public void View_HasAddressWriteBar()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ClassOPDemo_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_Addr_Label\"", axaml);
    }

    [Fact]
    public void View_HasMainDetailFields_AllThirteen()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ClassOPDemo_EnglishNamePtr_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_DescTextId_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_JpNamePtr_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_JpNameLen_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_PaletteId_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_DisplayWeapon_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_AllyEnemyColor_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_BattleAnime_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_MagicEffect_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_Unknown18_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_TerrainLeft_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_TerrainRight_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_AnimePtr_Input\"", axaml);
    }

    [Fact]
    public void View_HasN1JpFontSublist()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_List\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_B0_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_WritePtr_Button\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N1_ListExpand_Button\"", axaml);
        Assert.Contains("Click=\"N1_Write_Click\"", axaml);
        Assert.Contains("Click=\"N1_WritePtr_Click\"", axaml);
        Assert.Contains("Click=\"N1_ListExpand_Click\"", axaml);
    }

    [Fact]
    public void View_HasN2AnimeTuple_SixByteSpinners()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_B0_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_B1_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_B2_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_B3_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_B4_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_B5_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_Cmd_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_Addr_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_BlockSize_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_SelectedAddress_Input\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_Write_Button\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_N2_WritePtr_Button\"", axaml);
        Assert.Contains("Click=\"N2_Write_Click\"", axaml);
        Assert.Contains("Click=\"N2_WritePtr_Click\"", axaml);
    }

    /// <summary>
    /// Regression for Copilot CLI re-review on PR #577: every actionable
    /// Button in the view must have either a Click handler OR be marked
    /// IsEnabled="False"/IsVisible="False". Catches the dead-control
    /// failure mode (`N1WritePtrButton` / `N2WritePtrButton` had no
    /// handler in the initial commit and silently no-op'd).
    /// </summary>
    [Fact]
    public void View_AllButtons_AreWiredOrExplicitlyInert()
    {
        string axaml = ReadAxaml();
        // Pick every <Button ... /> declaration. The parsing is line-based
        // to keep the test independent of attribute ordering — a Button
        // tag may span multiple lines.
        var buttonOpenRx = new Regex(@"<Button\b([\s\S]*?)/>|<Button\b([\s\S]*?)>",
            RegexOptions.Compiled);
        var matches = buttonOpenRx.Matches(axaml);
        Assert.True(matches.Count >= 5,
            $"Expected at least 5 Button tags in the AXAML; found {matches.Count}.");
        foreach (Match m in matches)
        {
            string tag = m.Value;
            // Skip Button definitions that are inside a ContextMenu (those
            // come from the AddressListControl template — not our concern).
            // The two patterns are distinguishable: our buttons have an
            // AutomationId starting with "ClassOPDemo_".
            if (!tag.Contains("ClassOPDemo_")) continue;
            bool hasClick = tag.Contains("Click=\"");
            bool inert = tag.Contains("IsEnabled=\"False\"") || tag.Contains("IsVisible=\"False\"");
            Assert.True(hasClick || inert,
                $"Button without Click handler nor IsEnabled/IsVisible=False: {tag}");
        }
    }

    [Fact]
    public void View_HasAnimeSharedList()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ClassOPDemo_AnimeShared_List\"", axaml);
    }

    [Fact]
    public void View_HasInlinePreviewLabels()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ClassOPDemo_EnglishName_Label\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_DescText_Label\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_UnitPalette_Label\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_ClassName_Label\"", axaml);
    }

    [Fact]
    public void View_HasComboBoxesForAllyAndMagic()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ClassOPDemo_AllyEnemyColor_Combo\"", axaml);
        Assert.Contains("AutomationId=\"ClassOPDemo_MagicEffect_Combo\"", axaml);
    }

    // -----------------------------------------------------------------
    // Roslyn AST walk — assert all four handlers use _undoService.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N1WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N1_Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N2WriteHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N2_Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N2_Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N2_Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N1ListExpandHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N1_ListExpand_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_ListExpand_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_ListExpand_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N1ListExpandHandler_CallsExpandN1Block()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N1_ListExpand_Click[\s\S]*?_vm\.ExpandN1Block\(", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N1WritePtrHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N1_WritePtr_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_WritePtr_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N1_WritePtr_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_N2WritePtrHandler_UsesUndoService()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+N2_WritePtr_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N2_WritePtr_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+N2_WritePtr_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_OnAnimeSharedSelected_IsGatedByPopulationFlag()
    {
        // Copilot bot review thread PRRT_kwDOH0Mc1M6EWIzW: AnimeSharedList
        // SetItems' SelectFirst auto-fires SelectedAddressChanged, which
        // would otherwise jump the main list whenever we populate the
        // sibling panel. The view must suppress that auto-jump.
        string source = ReadCodeBehind();
        Assert.Contains("_suppressAnimeSharedJump", source);
        Assert.Matches(new Regex(@"void\s+LoadAnimeSharedFromOffset[\s\S]*?_suppressAnimeSharedJump\s*=\s*true", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+OnAnimeSharedSelected[\s\S]*?if\s*\(\s*_suppressAnimeSharedJump\s*\)\s*return", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_LoadN1Sublist_ResetsSelectedAddressOnEntryChange()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+LoadN1Sublist[\s\S]*?_n1SelectedAddr\s*=\s*0", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_LoadN2Tuple_ResetsBaseAddressOnEntryChange()
    {
        string source = ReadCodeBehind();
        Assert.Matches(new Regex(@"void\s+LoadN2Tuple[\s\S]*?_n2BaseAddr\s*=\s*0", RegexOptions.Compiled), source);
    }

    /// <summary>
    /// Static scan: any direct ROM-write primitive in the code-behind must
    /// live inside a `_undoService.Begin/Commit` window. This is the
    /// bypass-scan Copilot CLI plan review finding #6 asked for.
    /// </summary>
    [Fact]
    public void View_NoRomWriteBypassesUndoScope()
    {
        string source = ReadCodeBehind();
        var writePattern = new Regex(@"\brom\.(?:write_(?:u8|u16|u32|p32|range|fill)|SetU(?:8|16|32))\b",
            RegexOptions.Compiled);
        foreach (Match m in writePattern.Matches(source))
        {
            // Find the surrounding method-body boundaries and assert
            // `_undoService.Begin` appears before this match and
            // `_undoService.Commit` after.
            int matchIdx = m.Index;
            int methodStart = source.LastIndexOf("void ", matchIdx, StringComparison.Ordinal);
            int methodEnd = FindMatchingBrace(source, matchIdx);
            string body = source.Substring(methodStart, methodEnd - methodStart);
            Assert.Contains("_undoService.Begin", body);
            Assert.Contains("_undoService.Commit", body);
        }
    }

    static int FindMatchingBrace(string src, int matchIdx)
    {
        // Look for an opening brace up to 200 chars before the write — but
        // clamp at 0 so a write near the top of the file doesn't throw
        // ArgumentOutOfRangeException (Copilot bot review thread
        // `PRRT_kwDOH0Mc1M6EWIzd`).
        int searchStart = Math.Max(0, matchIdx - 200);
        int braceOpen = src.IndexOf('{', searchStart);
        if (braceOpen < 0 || braceOpen > matchIdx) braceOpen = src.IndexOf('{', matchIdx);
        if (braceOpen < 0) return src.Length;
        int depth = 1;
        for (int i = braceOpen + 1; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}') { depth--; if (depth == 0) return i + 1; }
        }
        return src.Length;
    }

    // -----------------------------------------------------------------
    // KnownGap inventory — every WF-only label must be covered as either
    // an AXAML control/label OR an explicit `KnownGap: <name> reason=...`
    // comment block in the AXAML.
    // -----------------------------------------------------------------

    /// <summary>
    /// The 32 distinct WF-only labels from the
    /// 2026-05-22-labels-sweep.md ClassOPDemoForm section. Mirroring the
    /// labels-sweep verbatim (deduplicated) so the test is auditable.
    /// </summary>
    static readonly string[] WfOnlyLabelInventory =
    {
        "/60 (秒)",
        "00固定",
        "05固定",
        "??",
        "size:",
        "アドレス",
        "アニメの特殊指定",
        "アニメ指定\nポインタ書き込み", // WF Designer used \r\n; we collapse to \n for AXAML matching
        "アニメ指定のポインタ",
        "アニメ指定のポインタ先",
        "アニメ指定共有",
        "ウェイト",
        "パレットID",
        "リストの拡張",
        "使用可能表示武器",
        "先頭アドレス",
        "再取得",
        "名前",
        "戦闘アニメ",
        "敵味方カラー",
        "日本語名\nポインタ書き込み",
        "日本語名の長さ",
        "日本語名ポインタ",
        "日本語名ポインタ先",
        "書き込み",
        "英語ポインタ",
        "表示地形右半分",
        "表示地形左半分",
        "説明文ID",
        "読込数",
        "選択アドレス:",
        "魔法エフェクト",
    };

    /// <summary>
    /// Each WF-only label must be one of:
    /// - present in AXAML as a translated English label (mapped via the
    ///   ja/zh tables in config/translate/),
    /// - present in AXAML directly as a runtime-translated content string, OR
    /// - listed in a structured `KnownGap: <label> reason=<...>` comment
    ///   block in the AXAML with a non-empty `reason=`.
    /// The mapping table below is the explicit audit trail.
    /// </summary>
    [Fact]
    public void View_HasAllWfOnlyLabelsCovered()
    {
        Assert.Equal(32, WfOnlyLabelInventory.Length);

        string axaml = ReadAxaml();

        // Build a label->coverage map. AXAML coverage = either an English
        // translation present in the AXAML Content/Header text, OR a
        // KnownGap entry with a non-empty reason.
        var labelCoverage = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Main detail field labels (13).
            ["英語ポインタ"] = "English Name Pointer:",
            ["説明文ID"] = "Description Text ID:",
            ["日本語名ポインタ"] = "Japanese Name Pointer:",
            ["日本語名の長さ"] = "Japanese Name Length:",
            ["パレットID"] = "Palette ID:",
            ["使用可能表示武器"] = "Display Weapon (Class):",
            ["敵味方カラー"] = "Ally / Enemy Color:",
            ["戦闘アニメ"] = "Battle Anime:",
            ["魔法エフェクト"] = "Magic Effect:",
            ["表示地形左半分"] = "Terrain (Left Half):",
            ["表示地形右半分"] = "Terrain (Right Half):",
            ["アニメ指定のポインタ"] = "Anime Spec Pointer:",
            // Top filter/reload bar
            ["先頭アドレス"] = "Start Address",
            ["読込数"] = "Read Count",
            ["再取得"] = "Reload",
            // Top write bar + main address-list panel
            ["アドレス"] = "Address",
            ["size:"] = "Size:",
            ["選択アドレス:"] = "Selected Address:",
            ["書き込み"] = "Write",
            ["名前"] = "Name",
            // N1 sub-panel
            ["日本語名ポインタ先"] = "Japanese Name Pointer Target",
            ["日本語名\nポインタ書き込み"] = "Write JP Name Pointer",
            ["リストの拡張"] = "List Expand",
            // N2 sub-panel
            ["アニメ指定のポインタ先"] = "Anime Spec Pointer Target",
            ["アニメ指定\nポインタ書き込み"] = "Write Anime Spec Pointer",
            ["アニメ指定共有"] = "Anime Spec Shared",
            // N2 6-byte tuple labels
            ["05固定"] = "Fixed 05:",
            ["ウェイト"] = "Wait Frames:",
            ["/60 (秒)"] = "/60 (sec)",
            ["アニメの特殊指定"] = "Anime Special Spec:",
            ["00固定"] = "Fixed 00:",
            ["??"] = "??:",
        };

        Assert.Equal(WfOnlyLabelInventory.Length, labelCoverage.Count);
        foreach (var wfLabel in WfOnlyLabelInventory)
        {
            Assert.True(labelCoverage.TryGetValue(wfLabel, out var enLabel),
                $"WF-only label '{wfLabel}' must be in the coverage map.");

            // The English label must appear in the AXAML (either as
            // Content=, Header=, Text=, an EditorTopBar slot-label
            // override, or inside a KnownGap reason).
            // #668: Start Address / Read Count / Reload labels migrated
            // into EditorTopBar styled-property overrides. Accept the
            // *Label="<en>" / *Label="<en>:" / ReloadButtonText="<en>"
            // forms; for "Reload" the EditorTopBar default ReloadButtonText
            // is "Reload" so the presence of <controls:EditorTopBar>
            // counts as covering that label.
            bool labelInEditorTopBarDefault =
                (enLabel == "Reload" || enLabel == "Start Address" || enLabel == "Read Count")
                && axaml.Contains("<controls:EditorTopBar", StringComparison.Ordinal);

            bool found =
                axaml.Contains($"Content=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"Header=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"Text=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"StartAddressLabel=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"StartAddressLabel=\"{enLabel}:\"", StringComparison.Ordinal)
                || axaml.Contains($"ReadCountLabel=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"ReadCountLabel=\"{enLabel}:\"", StringComparison.Ordinal)
                || axaml.Contains($"ReloadButtonText=\"{enLabel}\"", StringComparison.Ordinal)
                || labelInEditorTopBarDefault
                || axaml.Contains($"KnownGap: {wfLabel}", StringComparison.Ordinal);

            Assert.True(found,
                $"WF label '{wfLabel}' -> English '{enLabel}' must appear in AXAML " +
                $"as Content/Header/Text or be listed in a KnownGap comment.");
        }
    }

    /// <summary>
    /// The KnownGap comment block must enumerate every deferred WF-only
    /// surface with a non-empty `reason=`. This enforces the acceptance
    /// criterion 1 ("explicitly justified") audit trail Copilot CLI
    /// finding #5 asked for.
    /// </summary>
    [Fact]
    public void View_KnownGapBlock_HasNonEmptyReasons()
    {
        string axaml = ReadAxaml();
        // KnownGap entries live in `<!-- KnownGap: <name> reason=<text> -->` comments.
        // <name> captures everything up to the first whitespace+"reason=", then
        // <reason> captures everything up to the closing `-->`. Hyphens inside
        // the reason are allowed (`WinForms-only`, etc.).
        var rx = new Regex(@"KnownGap:\s*(\S+(?:\s+\S+)*?)\s+reason=(.+?)\s*-->",
            RegexOptions.Compiled);
        var matches = rx.Matches(axaml);
        Assert.True(matches.Count >= 2,
            $"AXAML must contain at least 2 KnownGap markers (battle-anime PNG preview, " +
            $"font glyph PNG preview); found {matches.Count}.");
        foreach (Match m in matches)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Groups[1].Value),
                $"KnownGap entry must name a feature: '{m.Value}'");
            Assert.False(string.IsNullOrWhiteSpace(m.Groups[2].Value),
                $"KnownGap entry must have a reason: '{m.Value}'");
        }
    }

    // -----------------------------------------------------------------
    // ViewModel: N1 (1-byte glyph, 0xFF terminator, NO cap).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadN1FontList_StopsAt0xFF()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            for (int i = 0; i < 5; i++) bytes[baseAddr + i] = (byte)(0x10 + i);
            bytes[baseAddr + 5] = 0xFF;
            bytes[baseAddr + 6] = 0x42; // junk past terminator
            uint slot = 0x00830000u;
            BitConverter.GetBytes(baseAddr | 0x08000000u).CopyTo(bytes, slot);

            var vm = new ClassOPDemoViewModel();
            var rows = vm.LoadN1FontList(slot);
            Assert.Equal(5, rows.Count);
            Assert.Equal(0x10u, rows[0].GlyphId);
            Assert.Equal(0x14u, rows[4].GlyphId);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Orphan ClassOPDemoForm.N1_Init has NO 16-entry cap (unlike canonical
    /// OPClassDemoForm.N1_Init). Plant 32 non-0xFF bytes and prove the
    /// walker reads all 32 instead of stopping at 16.
    /// </summary>
    [Fact]
    public void ViewModel_LoadN1FontList_HasNoSixteenEntryCap()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00820000u;
            // Plant 32 non-FF bytes — walker must NOT stop at 16.
            for (int i = 0; i < 32; i++) bytes[baseAddr + i] = (byte)(0x20 + i);
            bytes[baseAddr + 32] = 0xFF;
            uint slot = 0x00830000u;
            BitConverter.GetBytes(baseAddr | 0x08000000u).CopyTo(bytes, slot);

            var vm = new ClassOPDemoViewModel();
            var rows = vm.LoadN1FontList(slot);
            Assert.Equal(32, rows.Count);
            Assert.Equal(0x20u, rows[0].GlyphId);
            Assert.Equal(0x3Fu, rows[31].GlyphId);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN1FontList_NullRom_ReturnsEmpty()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ClassOPDemoViewModel();
            var rows = vm.LoadN1FontList(0x00830000u);
            Assert.Empty(rows);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteN1Entry_WritesOneByte_RoundTrip()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint addr = 0x00822000u;
            var vm = new ClassOPDemoViewModel();
            vm.WriteN1Entry(addr, 0x42);
            Assert.Equal(0x42u, rom.u8(addr));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel: N2 (single 6-byte tuple, orphan validator `i < 1`).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadN2Tuple_ReadsSixBytes()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            byte[] bytes = rom.Data;
            uint baseAddr = 0x00840000u;
            bytes[baseAddr + 0] = 0x05;
            bytes[baseAddr + 1] = 0x1E; // 30 frames = 0.5 sec
            bytes[baseAddr + 2] = 0x01;
            bytes[baseAddr + 3] = 0x00;
            bytes[baseAddr + 4] = 0xAB;
            bytes[baseAddr + 5] = 0x00;
            uint slot = 0x00850000u;
            BitConverter.GetBytes(baseAddr | 0x08000000u).CopyTo(bytes, slot);

            var vm = new ClassOPDemoViewModel();
            var tuple = vm.LoadN2Tuple(slot);
            Assert.True(tuple.HasValue);
            Assert.Equal(baseAddr, tuple.Value.Addr);
            Assert.Equal(0x05u, tuple.Value.B0);
            Assert.Equal(0x1Eu, tuple.Value.B1);
            Assert.Equal(0x01u, tuple.Value.B2);
            Assert.Equal(0x00u, tuple.Value.B3);
            Assert.Equal(0xABu, tuple.Value.B4);
            Assert.Equal(0x00u, tuple.Value.B5);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN2Tuple_NullRom_ReturnsNull()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            var vm = new ClassOPDemoViewModel();
            var tuple = vm.LoadN2Tuple(0x00850000u);
            Assert.False(tuple.HasValue);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadN2Tuple_InvalidPointer_ReturnsNull()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // No bytes planted at the slot — p32 returns 0 which is unsafe.
            var vm = new ClassOPDemoViewModel();
            var tuple = vm.LoadN2Tuple(0x00860000u);
            Assert.False(tuple.HasValue);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteN2Tuple_WritesSixBytes_RoundTrip()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint baseAddr = 0x00870000u;
            var vm = new ClassOPDemoViewModel();
            vm.WriteN2Tuple(baseAddr, 0x05, 0x3C, 0x02, 0x00, 0x7F, 0x00);

            Assert.Equal(0x05u, rom.u8(baseAddr + 0));
            Assert.Equal(0x3Cu, rom.u8(baseAddr + 1));
            Assert.Equal(0x02u, rom.u8(baseAddr + 2));
            Assert.Equal(0x00u, rom.u8(baseAddr + 3));
            Assert.Equal(0x7Fu, rom.u8(baseAddr + 4));
            Assert.Equal(0x00u, rom.u8(baseAddr + 5));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel: pointer-aware writes (P0/P8/P24 -> 0x08000000 high bit).
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_WriteClassOPDemo_StoresEnglishNamePointer_AsGbaPointer()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ClassOPDemoViewModel();
            uint entryAddr = 0x00800000u;
            vm.CurrentAddr = entryAddr;
            vm.P0 = 0x00400000u;
            vm.WriteClassOPDemo();
            Assert.Equal(0x00400000u | 0x08000000u, rom.u32(entryAddr + 0));
            Assert.Equal(0x00400000u, rom.p32(entryAddr + 0));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteClassOPDemo_StoresJapaneseNamePointer_AsGbaPointer()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ClassOPDemoViewModel();
            uint entryAddr = 0x00800100u;
            vm.CurrentAddr = entryAddr;
            vm.P8 = 0x00200000u;
            vm.WriteClassOPDemo();
            Assert.Equal(0x00200000u | 0x08000000u, rom.u32(entryAddr + 8));
            Assert.Equal(0x00200000u, rom.p32(entryAddr + 8));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_WriteClassOPDemo_StoresAnimePointer_AsGbaPointer()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ClassOPDemoViewModel();
            uint entryAddr = 0x00800200u;
            vm.CurrentAddr = entryAddr;
            vm.P24 = 0x00300000u;
            vm.WriteClassOPDemo();
            Assert.Equal(0x00300000u | 0x08000000u, rom.u32(entryAddr + 24));
            Assert.Equal(0x00300000u, rom.p32(entryAddr + 24));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadClassOPDemo_ReadsPointersAsOffsets()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint entryAddr = 0x00800300u;
            BitConverter.GetBytes(0x08500000u).CopyTo(rom.Data, entryAddr + 0);   // P0
            BitConverter.GetBytes(0x08501000u).CopyTo(rom.Data, entryAddr + 8);   // P8
            BitConverter.GetBytes(0x08502000u).CopyTo(rom.Data, entryAddr + 24);  // P24

            var vm = new ClassOPDemoViewModel();
            vm.LoadClassOPDemo(entryAddr);
            Assert.Equal(0x00500000u, vm.P0);
            Assert.Equal(0x00501000u, vm.P8);
            Assert.Equal(0x00502000u, vm.P24);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadClassOPDemo_ReadsAllThirteenFields()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint entryAddr = 0x00800400u;
            // Plant a fully populated entry (28 bytes).
            BitConverter.GetBytes(0x08500000u).CopyTo(rom.Data, entryAddr + 0);   // P0
            BitConverter.GetBytes(0x12345678u).CopyTo(rom.Data, entryAddr + 4);   // D4
            BitConverter.GetBytes(0x08501000u).CopyTo(rom.Data, entryAddr + 8);   // P8
            rom.Data[entryAddr + 12] = 0x10;  // B12
            rom.Data[entryAddr + 13] = 0x11;  // B13
            rom.Data[entryAddr + 14] = 0x12;  // B14
            rom.Data[entryAddr + 15] = 0x13;  // B15
            rom.Data[entryAddr + 16] = 0x14;  // B16
            rom.Data[entryAddr + 17] = 0x15;  // B17
            BitConverter.GetBytes(0x9ABCDEF0u).CopyTo(rom.Data, entryAddr + 18);  // D18
            rom.Data[entryAddr + 22] = 0x16;  // B22
            rom.Data[entryAddr + 23] = 0x17;  // B23
            BitConverter.GetBytes(0x08502000u).CopyTo(rom.Data, entryAddr + 24);  // P24

            var vm = new ClassOPDemoViewModel();
            vm.LoadClassOPDemo(entryAddr);
            Assert.Equal(0x00500000u, vm.P0);
            Assert.Equal(0x12345678u, vm.D4);
            Assert.Equal(0x00501000u, vm.P8);
            Assert.Equal(0x10u, vm.B12);
            Assert.Equal(0x11u, vm.B13);
            Assert.Equal(0x12u, vm.B14);
            Assert.Equal(0x13u, vm.B15);
            Assert.Equal(0x14u, vm.B16);
            Assert.Equal(0x15u, vm.B17);
            Assert.Equal(0x9ABCDEF0u, vm.D18);
            Assert.Equal(0x16u, vm.B22);
            Assert.Equal(0x17u, vm.B23);
            Assert.Equal(0x00502000u, vm.P24);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel-level write-pointer behaviour (the view's WritePtr_Click
    // handlers do rom.write_p32 directly, so the round-trip can be
    // exercised in isolation against a synthetic ROM).
    // -----------------------------------------------------------------

    [Fact]
    public void RomWritePointer_WritesJpNamePointer_WithGbaHighBit()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint entryAddr = 0x00800400u;
            uint newPtr = 0x00500000u;
            rom.write_p32(entryAddr + 8, newPtr);
            Assert.Equal(newPtr | 0x08000000u, rom.u32(entryAddr + 8));
            Assert.Equal(newPtr, rom.p32(entryAddr + 8));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void RomWritePointer_WritesAnimePointer_WithGbaHighBit()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint entryAddr = 0x00800500u;
            uint newPtr = 0x00600000u;
            rom.write_p32(entryAddr + 24, newPtr);
            Assert.Equal(newPtr | 0x08000000u, rom.u32(entryAddr + 24));
            Assert.Equal(newPtr, rom.p32(entryAddr + 24));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel: anime-shared sibling scan.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadAnimeSharedList_ReturnsSiblingsSharingP24()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Plant a fresh op_class_demo table inside ROM and rewire the
            // pointer so LoadClassOPDemoList picks it up.
            uint tableAddr = 0x00900000u;
            uint sharedP24 = 0x08123456u;
            uint otherP24 = 0x08234567u;

            // 5 entries, 28 bytes each. Entries 0, 2, 4 share `sharedP24`;
            // entries 1, 3 use `otherP24`.
            for (int i = 0; i < 5; i++)
            {
                uint entry = (uint)(tableAddr + i * 28);
                // P0 (must be a valid pointer for the list walker to accept).
                BitConverter.GetBytes(0x08F00000u).CopyTo(rom.Data, entry + 0);
                uint p24 = (i % 2 == 0) ? sharedP24 : otherP24;
                BitConverter.GetBytes(p24).CopyTo(rom.Data, entry + 24);
            }
            // Terminator: entry 5 has invalid P0.
            uint termEntry = tableAddr + 5 * 28;
            BitConverter.GetBytes(0x00000000u).CopyTo(rom.Data, termEntry + 0);

            // Rewire op_class_demo_pointer slot.
            uint ptrSlot = rom.RomInfo.op_class_demo_pointer;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, ptrSlot);

            var vm = new ClassOPDemoViewModel();
            // P24 stored value uses offsets (0x123456) once read through p32.
            uint sharedOffset = sharedP24 & 0x00FFFFFFu;
            var siblings = vm.LoadAnimeSharedList(sharedOffset, currentEntryAddr: tableAddr);
            // Entries 0, 2, 4 share -> excluding self (entry 0) we expect 2, 4.
            Assert.Equal(2, siblings.Count);
            Assert.Equal(tableAddr + 2 * 28, siblings[0].addr);
            Assert.Equal(tableAddr + 4 * 28, siblings[1].addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // ViewModel: ExpandN1Block round-trip.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ExpandN1Block_AddsOneEntryAndRelocates()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Plant a 3-entry N1 block at tableAddr; pointer at slot.
            uint tableAddr = 0x00A00000u;
            rom.Data[tableAddr + 0] = 0x10;
            rom.Data[tableAddr + 1] = 0x11;
            rom.Data[tableAddr + 2] = 0x12;
            rom.Data[tableAddr + 3] = 0xFF;
            uint slot = 0x00A10000u;
            BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(rom.Data, slot);

            var vm = new ClassOPDemoViewModel();
            var result = vm.ExpandN1Block(slot, currentCount: 3);
            Assert.True(result.Success, result.Error ?? "ExpandTable returned success=false");
            // DataExpansionCore.ExpandTable adds exactly one entry.
            Assert.Equal((uint)(3 + 1), result.NewCount);
            // Old slot bytes should have been wiped to 0xFF.
            Assert.Equal(0xFFu, rom.u8(tableAddr + 0));
            Assert.Equal(0xFFu, rom.u8(tableAddr + 1));
            Assert.Equal(0xFFu, rom.u8(tableAddr + 2));
            // Pointer should now point to the new base.
            uint newBase = rom.p32(slot);
            Assert.Equal(result.NewBaseAddress, newBase);
            // The first 3 entries at the new base must match the originals.
            Assert.Equal(0x10u, rom.u8(newBase + 0));
            Assert.Equal(0x11u, rom.u8(newBase + 1));
            Assert.Equal(0x12u, rom.u8(newBase + 2));
            // The freshly-allocated slot is zero-filled.
            Assert.Equal(0x00u, rom.u8(newBase + 3));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static ROM MakeMinimalFe8uRom()
    {
        var rom = new ROM();
        var data = new byte[0x1100000];
        rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
        return rom;
    }

    static string ReadAxaml() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ClassOPDemoView.axaml"));

    static string ReadCodeBehind() => File.ReadAllText(Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ClassOPDemoView.axaml.cs"));

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
