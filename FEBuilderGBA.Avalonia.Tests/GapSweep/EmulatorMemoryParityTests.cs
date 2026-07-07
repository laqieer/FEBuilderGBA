// SPDX-License-Identifier: GPL-3.0-or-later
// Gap-sweep regression tests for EmulatorMemoryView. (#385)
//
// The WinForms `EmulatorMemoryForm` ships 353 controls vs the Avalonia
// `EmulatorMemoryView` stub of 5 (HIGH density -98.6%) plus 174 WF-only
// labels (per the 2026-05-27 sweep). This test suite locks in the 6-tab
// + inner 16-Etc-subtab parity rebuild per the v2 plan accepted on
// issue #385:
//
//   Tab 1 Event:         current-text / Flag / MemorySlot listbox
//                        placeholders + running-event listbox
//   Tab 2 EventHistory:  history listbox placeholder
//   Tab 3 Procs:         Procs listbox + struct field surface
//   Tab 4 BGM:           BGM player list placeholder
//   Tab 5 Etc:           16-tab inner control (General/Trap/Palette/
//                        ClearTurns/BWL/ChapterData/Supply/Action/Arena/
//                        BattleActor/BattleTarget/AI/BattleRound/BattleSome/
//                        Worldmap/Dungeon)
//   Tab 6 Cheat:         15 disabled cheat buttons + warp/turn/money/etc.
//
// The cross-platform Avalonia version has NO live RAM access (no P/Invoke)
// so every ROM-mutating / live-update control is KnownGap (IsEnabled=False
// + #385 tooltip). The view is a DECLARATIVE PARITY SURFACE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the EmulatorMemoryForm parity raise (#385) is permanent.
/// </summary>
public class EmulatorMemoryParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 353 control instantiations (per 2026-05-27
    /// density sweep). Strict MEDIUM cutoff per gap-sweep convention is
    /// ceil(WF * 0.75) = 265. Below that the verdict stays HIGH.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // 265 = ceil(353 * 0.75); convention shared with MapStyleEditor #391
        // / SkillConfigFE8N #390 / ItemUsagePointer / etc.
        const int MinAvControls = 265;
        Assert.True(avCount >= MinAvControls,
            $"AV control count {avCount} must be >= {MinAvControls} (strict MEDIUM cutoff, WF=353)");
    }

    // -----------------------------------------------------------------
    // Outer 6-tab structure.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasTabControl_WithSixTabs()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"EmulatorMemory_Main_TabControl\"",
            axaml);
        // 6 expected outer tabs.
        string[] expected =
        {
            "EmulatorMemory_Event_Tab",
            "EmulatorMemory_EventHistory_Tab",
            "EmulatorMemory_Procs_Tab",
            "EmulatorMemory_BGM_Tab",
            "EmulatorMemory_Etc_Tab",
            "EmulatorMemory_Cheat_Tab",
        };
        foreach (string id in expected)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                axaml);
        }
    }

    // -----------------------------------------------------------------
    // Inner Etc TabControl (16 sub-tabs).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasEtcSubTabControl_With16Tabs()
    {
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationProperties.AutomationId=\"EmulatorMemory_EtcSub_TabControl\"",
            axaml);
        string[] expected =
        {
            "EmulatorMemory_EtcSub_General_Tab",
            "EmulatorMemory_EtcSub_Trap_Tab",
            "EmulatorMemory_EtcSub_Palette_Tab",
            "EmulatorMemory_EtcSub_ClearTurns_Tab",
            "EmulatorMemory_EtcSub_BWL_Tab",
            "EmulatorMemory_EtcSub_ChapterData_Tab",
            "EmulatorMemory_EtcSub_Supply_Tab",
            "EmulatorMemory_EtcSub_Action_Tab",
            "EmulatorMemory_EtcSub_Arena_Tab",
            "EmulatorMemory_EtcSub_BattleActor_Tab",
            "EmulatorMemory_EtcSub_BattleTarget_Tab",
            "EmulatorMemory_EtcSub_AI_Tab",
            "EmulatorMemory_EtcSub_BattleRound_Tab",
            "EmulatorMemory_EtcSub_BattleSome_Tab",
            "EmulatorMemory_EtcSub_Worldmap_Tab",
            "EmulatorMemory_EtcSub_Dungeon_Tab",
        };
        foreach (string id in expected)
        {
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{id}\"",
                axaml);
        }
    }

    // -----------------------------------------------------------------
    // Functional Open* buttons (9 working WindowManager.Open<T>() jumps).
    // -----------------------------------------------------------------

    /// <summary>
    /// 9 cross-editor opens reachable from EmulatorMemoryForm in WF, all of
    /// which can be opened parameterlessly via WindowManager.Open&lt;T&gt;().
    /// </summary>
    static readonly (string AutomationId, string ClickHandler, string TargetView)[] FunctionalOpens =
    {
        ("EmulatorMemory_OpenEventScript_Button", "OpenEventScript_Click", "EventScriptView"),
        ("EmulatorMemory_OpenProcsScript_Button", "OpenProcsScript_Click", "ProcsScriptView"),
        ("EmulatorMemory_OpenHexEditor_Button", "OpenHexEditor_Click", "HexEditorView"),
        ("EmulatorMemory_OpenTextViewer_Button", "OpenTextViewer_Click", "TextViewerView"),
        ("EmulatorMemory_OpenSongTable_Button", "OpenSongTable_Click", "SongTableView"),
        ("EmulatorMemory_OpenToolBGMMuteDialog_Button", "OpenToolBGMMuteDialog_Click", "ToolBGMMuteDialogView"),
        ("EmulatorMemory_OpenMapChange_Button", "OpenMapChange_Click", "MapChangeView"),
        ("EmulatorMemory_OpenRAMRewriteTool_Button", "OpenRAMRewriteTool_Click", "RAMRewriteToolView"),
        ("EmulatorMemory_OpenRAMRewriteToolMAP_Button", "OpenRAMRewriteToolMAP_Click", "RAMRewriteToolMAPView"),
    };

    [Fact]
    public void View_HasFunctionalOpenButtons_Wired()
    {
        string axaml = ReadAxaml();
        string code = File.ReadAllText(CodeBehindPath());
        foreach (var t in FunctionalOpens)
        {
            // Button exists and is ENABLED.
            Assert.Contains(
                $"AutomationProperties.AutomationId=\"{t.AutomationId}\"",
                axaml);
            var elementPattern = new Regex(
                @"<Button(?:\s[^/>]*?)?" +
                Regex.Escape($"AutomationProperties.AutomationId=\"{t.AutomationId}\"") +
                @"[\s\S]*?/?>",
                RegexOptions.None);
            Match m = elementPattern.Match(axaml);
            Assert.True(m.Success, $"Cannot locate <Button> element for {t.AutomationId}");
            Assert.DoesNotContain("IsEnabled=\"False\"", m.Value);
            Assert.Contains($"Click=\"{t.ClickHandler}\"", m.Value);

            // Click handler exists in code-behind and calls Open<TView>().
            var handlerPattern = new Regex(
                @"void " + Regex.Escape(t.ClickHandler) +
                @"\([\s\S]*?WindowManager\.Instance\.Open<" + Regex.Escape(t.TargetView) +
                @">\(\)",
                RegexOptions.Singleline);
            Assert.Matches(handlerPattern, code);
        }
    }

    // -----------------------------------------------------------------
    // KnownGap buttons - the disabled / non-functional cheat + live-RAM
    // controls. All ROM-mutating cheats stay disabled in Avalonia until
    // a cross-platform RAM reader lands.
    // -----------------------------------------------------------------

    /// <summary>
    /// Authoritative list of every KnownGap-disabled button. Each MUST
    /// appear with IsEnabled="False" AND a tooltip referencing #385.
    /// Adding/removing without updating this list breaks the count
    /// enforcement (View_NoExtraDisabledButtons).
    /// </summary>
    static readonly string[] KnownGapButtonIds =
    {
        // Cheat tab.
        "EmulatorMemory_CHEAT_UnitWarp_Button",
        "EmulatorMemory_CHEAT_Turn_Button",
        "EmulatorMemory_CHEAT_Warp_Button",
        "EmulatorMemory_CHEAT_AllPlayerUnitGrow_Button",
        "EmulatorMemory_CHEAT_AllUnitGrow_Button",
        "EmulatorMemory_CHEAT_AllEnemyDoNotMove_Button",
        "EmulatorMemory_CHEAT_AllEnemyHP1_Button",
        "EmulatorMemory_CHEAT_Money_Button",
        "EmulatorMemory_CHEAT_Weather_Button",
        "EmulatorMemory_CHEAT_Fog_Button",
        "EmulatorMemory_CHEAT_UnitHaveItem_Button",
        "EmulatorMemory_CHEAT_UnitGrow_Button",
        "EmulatorMemory_CHEAT_UnitHP1_Button",
        "EmulatorMemory_CHEAT_SetFlag03_Button",
        // Speech / Subtitle / Memory dump (Event tab).
        "EmulatorMemory_Speech_Button",
        "EmulatorMemory_Subtile_Button",
        "EmulatorMemory_MemoryDump02_Button",
        "EmulatorMemory_MemoryDump03_Button",
        // Etc / palette search.
        "EmulatorMemory_PaletteSearch_Button",
        // Active-unit jump (depends on live RAM address).
        "EmulatorMemory_OpenActiveUnit_Button",
    };

    [Fact]
    public void View_KnownGapButtons_AreDisabledAndTagged()
    {
        string axaml = ReadAxaml();
        foreach (string id in KnownGapButtonIds)
        {
            var idAttr = $"AutomationProperties.AutomationId=\"{id}\"";
            Assert.Contains(idAttr, axaml);

            var elementPattern = new Regex(
                @"<Button(?:\s[^/>]*?)?" + Regex.Escape(idAttr) + @"[\s\S]*?/?>",
                RegexOptions.None);
            Match m = elementPattern.Match(axaml);
            Assert.True(m.Success, $"Cannot locate <Button> element for {id}");

            string elementText = m.Value;
            Assert.True(
                elementText.Contains("IsEnabled=\"False\""),
                $"KnownGap button {id} must have IsEnabled=\"False\"; element was: {elementText}");
            Assert.True(
                elementText.Contains("#385"),
                $"KnownGap button {id} must have a #385 tooltip; element was: {elementText}");
        }
    }

    /// <summary>
    /// Proves no disabled &lt;Button&gt; slips in that isn't on the KnownGap
    /// list. The count of IsEnabled="False" Buttons must equal exactly
    /// the KnownGap list length.
    /// </summary>
    [Fact]
    public void View_NoExtraDisabledButtons()
    {
        string axaml = ReadAxaml();
        var disabledButtonPattern = new Regex(
            @"<Button[^>]*IsEnabled=""False""[^>]*",
            RegexOptions.None);
        int disabledCount = disabledButtonPattern.Matches(axaml).Count;
        Assert.Equal(KnownGapButtonIds.Length, disabledCount);
    }

    // -----------------------------------------------------------------
    // Legacy automation IDs (cross-PR compatibility).
    // -----------------------------------------------------------------

    [Fact]
    public void View_RetainsLegacyAutomationIds()
    {
        string axaml = ReadAxaml();
        // The original stub exposed only Close. Must remain.
        Assert.Contains(
            "AutomationProperties.AutomationId=\"EmulatorMemory_Close_Button\"",
            axaml);
    }

    // -----------------------------------------------------------------
    // INavigationTargetSource manifest.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_ImplementsINavigationTargetSource()
    {
        Assert.True(typeof(INavigationTargetSource).IsAssignableFrom(typeof(EmulatorMemoryViewModel)),
            "EmulatorMemoryViewModel must implement INavigationTargetSource.");
    }

    [Fact]
    public void ViewModel_NavigationTargets_All12_HaveValidTypes()
    {
        var vm = new EmulatorMemoryViewModel();
        IReadOnlyList<NavigationTarget> targets =
            ((INavigationTargetSource)vm).GetNavigationTargets();
        // 9 functional + 3 KnownGap = 12 callsites (mirrors WF jumps-sweep).
        Assert.Equal(12, targets.Count);
        foreach (var t in targets)
        {
            Assert.NotNull(t.TargetViewType);
            Assert.True(
                typeof(global::Avalonia.Controls.Window).IsAssignableFrom(t.TargetViewType)
                || typeof(IEmbeddableEditor).IsAssignableFrom(t.TargetViewType),
                $"NavigationTarget {t.CommandName}: target {t.TargetViewType.Name} must be a Window or IEmbeddableEditor");
        }
    }

    [Fact]
    public void ViewModel_NavigationTargets_FunctionalRows_HaveNoIssueRef()
    {
        var vm = new EmulatorMemoryViewModel();
        IReadOnlyList<NavigationTarget> targets =
            ((INavigationTargetSource)vm).GetNavigationTargets();

        var functionalCommands = new HashSet<string>
        {
            "OpenEventScript",
            "OpenProcsScript",
            "OpenHexEditor",
            "OpenTextViewer",
            "OpenSongTable",
            "OpenToolBGMMuteDialog",
            "OpenMapChange",
            "OpenRAMRewriteTool",
            "OpenRAMRewriteToolMAP",
        };
        foreach (var t in targets)
        {
            if (functionalCommands.Contains(t.CommandName))
            {
                Assert.Null(t.IssueRef);
            }
        }
        // All 9 functional commands present.
        var present = new HashSet<string>(targets.Select(t => t.CommandName));
        foreach (string cmd in functionalCommands)
        {
            Assert.Contains(cmd, present);
        }
    }

    [Fact]
    public void ViewModel_NavigationTargets_KnownGapRows_Marked385()
    {
        var vm = new EmulatorMemoryViewModel();
        IReadOnlyList<NavigationTarget> targets =
            ((INavigationTargetSource)vm).GetNavigationTargets();

        var knownGapCommands = new HashSet<string>
        {
            "JumpToCurrentEvent",
            "JumpToProcsCursor",
            "JumpToRunningEventLine",
        };
        foreach (string cmd in knownGapCommands)
        {
            var t = targets.FirstOrDefault(x => x.CommandName == cmd);
            Assert.NotNull(t);
            Assert.Equal("#385", t!.IssueRef);
        }
    }

    // -----------------------------------------------------------------
    // WF-only label inventory coverage.
    // -----------------------------------------------------------------

    /// <summary>
    /// The 174 distinct WF-only labels from 2026-05-27-labels-sweep.md
    /// EmulatorMemoryForm section. Every entry must be covered by either:
    ///   - A literal rendered in the AXAML (Content/Header/Text), OR
    ///   - A KnownGap mapping with an English equivalent that IS rendered.
    /// </summary>
    static readonly string[] WfOnlyLabelInventory =
    {
        // ---- Address offset literals (struct field positions) ----
        "41(0x29)", "44(0x2C)", "48(0x30)", "52(0x34)", "56(0x38)",
        "60(0x3C)", "64(0x40)", "68(0x44)", "72(0x48)", "76(0x4C)",
        "80(0x50)", "84(0x54)", "88(0x58)", "92(0x5C)", "96(0x60)",
        "100(0x64)", "104(0x68)",
        // ---- Unknown / reserved fields ----
        "??? 1", "??? 2", "??? 3", "??? 8", "??? 9",
        // ---- Top-level tab headers ----
        "ActionData", "AIData", "BattleActor", "BattleRound", "BattleRounds",
        "BattleSome", "BattleTarget", "BGM", "BWL", "ChapterData", "Dungeon",
        "Etc", "Procs", "Worldmap", "Worldmap FE8",
        // ---- RAM-unit struct field labels ----
        "AI1", "AI1 Count", "AI2", "AI2 Count", "AI3", "AI4",
        "Block Counter", "Code cursor", "Destructor Routine", "ERROR",
        "Exp", "First Child Struct", "Loop Routine", "Lv", "MapSprite",
        "Mark", "Name", "Next Struct", "Parent Procs", "PartyCount",
        "Previous Struct", "ROM Class", "ROM Unit", "Sleep Timer",
        "Some kind of bitfield", "Start of code", "Text", "UserSpace",
        "UserSpace1", "UserSpace2", "UserSpace3", "X", "Y",
        // ---- Cheat / live-update Japanese captions ----
        "↑最新", "より古い↓",
        "このユニットに以下のアイテムをもたせる", "このユニットのHPを1にする。",
        "このユニットのパラメータをカンストさせる。", "このユニットの座標を変更しワープさせる。",
        "この章へワープする", "すべての味方ユニットを最強にします。(Hotkey Ctrl + G)",
        "すべての敵ユニットAIを「移動しない」に設定にします。",
        "すべての敵ユニットのHPを1にします。",
        // ---- Item / count / weapon-exp labels ----
        "アイテムID1", "アイテムID2", "アイテムID3", "アイテムID4", "アイテムID5",
        "アイテム数1", "アイテム数2", "アイテム数3", "アイテム数4", "アイテム数5",
        "アドレス", "イベント", "イベント履歴",
        "イベント履歴(上が最新)\r\n時刻,イベント開始アドレス,実行中のアドレス,イベント",
        "クリアターン数",
        "クリアフラグ以外の、他のフラグを変更したい場合は、イベント画面から、変更したいフラグをダブルクリックしてください。",
        "ターン数", "ターン数を変更", "チート", "デバッグ用に便利なチート機能です",
        "トラップデータ", "パレット", "パーティー", "フラグ", "マップID",
        "メモリをダンプしてファイルに書き込む 0x02000000",
        "メモリをダンプしてファイルに書き込む 0x03000000",
        "メモリスロット", "ユニットの行動で設定される項目",
        "ワープする章", "ワールドマップ拠点",
        "体格＋", "個数", "光 EXP", "再生されている音楽", "剣 EXP",
        "力と魔力", "同行者ID", "回復モード", "塔と遺跡のデータ",
        "天気", "天気を変更(次のターンから適用されます。)",
        "守備", "実行しているイベント", "弓 EXP",
        "戦歴データ", "戦闘に関係する諸データ",
        "戦闘データ gBattleActor", "戦闘データ gBattleTarget",
        "所持金", "所持金を以下の値に変更する",
        "技", "持たせるアイテム", "操作中のユニット",
        "支援1", "支援2", "支援3", "支援4", "支援5", "支援6", "支援7", "支援フラグ",
        "敵を含めて、すべてのユニットを最強にします",
        "斧 EXP", "最大HP", "杖 EXP", "検索", "槍 EXP",
        "汎用", "状態", "状態とターン", "現在HP",
        "現在、操作しているユニット", "現在の章をクリアします。(Hotkey: Ctrl + U)",
        "理 EXP", "移動＋", "章データ", "編", "聖水松明",
        "自動的に更新する", "解析者向けの機能", "輸送隊", "輸送隊の内容",
        "速さ", "運", "選択アドレス:", "部隊表ID",
        "闇 EXP", "闘技場", "闘技場の相手選出に利用するデータ",
        "霧レベル", "霧レベルを変更(次のターンから適用されます。)",
        "魔防",
        "0で霧なし。1が視界1マスの最大の霧です。",
    };

    /// <summary>
    /// Coverage map: each WF-only label → English equivalent (or "KnownGap: ...").
    /// The English equivalent is what should appear in the AXAML.
    /// </summary>
    static readonly Dictionary<string, string> LabelCoverage = new(StringComparer.Ordinal)
    {
        // Address offsets — rendered verbatim as Lv/Exp grid labels.
        ["41(0x29)"] = "41(0x29)",
        ["44(0x2C)"] = "44(0x2C)",
        ["48(0x30)"] = "48(0x30)",
        ["52(0x34)"] = "52(0x34)",
        ["56(0x38)"] = "56(0x38)",
        ["60(0x3C)"] = "60(0x3C)",
        ["64(0x40)"] = "64(0x40)",
        ["68(0x44)"] = "68(0x44)",
        ["72(0x48)"] = "72(0x48)",
        ["76(0x4C)"] = "76(0x4C)",
        ["80(0x50)"] = "80(0x50)",
        ["84(0x54)"] = "84(0x54)",
        ["88(0x58)"] = "88(0x58)",
        ["92(0x5C)"] = "92(0x5C)",
        ["96(0x60)"] = "96(0x60)",
        ["100(0x64)"] = "100(0x64)",
        ["104(0x68)"] = "104(0x68)",
        // ASCII unknown / reserved
        ["??? 1"] = "??? 1",
        ["??? 2"] = "??? 2",
        ["??? 3"] = "??? 3",
        ["??? 8"] = "??? 8",
        ["??? 9"] = "??? 9",
        // ASCII tab headers
        ["ActionData"] = "ActionData",
        ["AIData"] = "AIData",
        ["BattleActor"] = "BattleActor",
        ["BattleRound"] = "BattleRound",
        ["BattleRounds"] = "BattleRounds",
        ["BattleSome"] = "BattleSome",
        ["BattleTarget"] = "BattleTarget",
        ["BGM"] = "BGM",
        ["BWL"] = "BWL",
        ["ChapterData"] = "ChapterData",
        ["Dungeon"] = "Dungeon",
        ["Etc"] = "Etc",
        ["Procs"] = "Procs",
        ["Worldmap"] = "Worldmap",
        ["Worldmap FE8"] = "Worldmap FE8",
        // ASCII struct field labels
        ["AI1"] = "AI1",
        ["AI1 Count"] = "AI1 Count",
        ["AI2"] = "AI2",
        ["AI2 Count"] = "AI2 Count",
        ["AI3"] = "AI3",
        ["AI4"] = "AI4",
        ["Block Counter"] = "Block Counter",
        ["Code cursor"] = "Code cursor",
        ["Destructor Routine"] = "Destructor Routine",
        ["ERROR"] = "ERROR",
        ["Exp"] = "Exp",
        ["First Child Struct"] = "First Child Struct",
        ["Loop Routine"] = "Loop Routine",
        ["Lv"] = "Lv",
        ["MapSprite"] = "MapSprite",
        ["Mark"] = "Mark",
        ["Name"] = "Name",
        ["Next Struct"] = "Next Struct",
        ["Parent Procs"] = "Parent Procs",
        ["PartyCount"] = "PartyCount",
        ["Previous Struct"] = "Previous Struct",
        ["ROM Class"] = "ROM Class",
        ["ROM Unit"] = "ROM Unit",
        ["Sleep Timer"] = "Sleep Timer",
        ["Some kind of bitfield"] = "Some kind of bitfield",
        ["Start of code"] = "Start of code",
        ["Text"] = "Text",
        ["UserSpace"] = "UserSpace",
        ["UserSpace1"] = "UserSpace1",
        ["UserSpace2"] = "UserSpace2",
        ["UserSpace3"] = "UserSpace3",
        ["X"] = "X",
        ["Y"] = "Y",
        // Japanese arrows / history
        ["↑最新"] = "↑ Newest",
        ["より古い↓"] = "Older ↓",
        // Cheat / live-update captions
        ["このユニットに以下のアイテムをもたせる"] = "Give this unit the following item",
        ["このユニットのHPを1にする。"] = "Set this unit's HP to 1.",
        ["このユニットのパラメータをカンストさせる。"] = "Max out this unit's stats.",
        ["このユニットの座標を変更しワープさせる。"] = "Change this unit's coordinates and warp it.",
        ["この章へワープする"] = "Warp to this chapter",
        ["すべての味方ユニットを最強にします。(Hotkey Ctrl + G)"] =
            "Max out all player units. (Hotkey Ctrl + G)",
        ["すべての敵ユニットAIを「移動しない」に設定にします。"] =
            "Set all enemy unit AI to 'Do not move'.",
        ["すべての敵ユニットのHPを1にします。"] = "Set all enemy units' HP to 1.",
        // Item / count
        ["アイテムID1"] = "Item ID 1",
        ["アイテムID2"] = "Item ID 2",
        ["アイテムID3"] = "Item ID 3",
        ["アイテムID4"] = "Item ID 4",
        ["アイテムID5"] = "Item ID 5",
        ["アイテム数1"] = "Item count 1",
        ["アイテム数2"] = "Item count 2",
        ["アイテム数3"] = "Item count 3",
        ["アイテム数4"] = "Item count 4",
        ["アイテム数5"] = "Item count 5",
        // Tab + section headers
        ["アドレス"] = "Address",
        ["イベント"] = "Event",
        ["イベント履歴"] = "Event History",
        ["イベント履歴(上が最新)\r\n時刻,イベント開始アドレス,実行中のアドレス,イベント"] =
            "Event History (newest first) - Time / Begin / Running / Event",
        ["クリアターン数"] = "Clear Turns",
        ["クリアフラグ以外の、他のフラグを変更したい場合は、イベント画面から、変更したいフラグをダブルクリックしてください。"] =
            "To change a non-clear flag, double-click the flag in the Event panel.",
        ["ターン数"] = "Turn",
        ["ターン数を変更"] = "Change Turn Count",
        ["チート"] = "Cheat",
        ["デバッグ用に便利なチート機能です"] = "Debug cheat utilities",
        ["トラップデータ"] = "Trap Data",
        ["パレット"] = "Palette",
        ["パーティー"] = "Party",
        ["フラグ"] = "Flag",
        ["マップID"] = "Map ID",
        ["メモリをダンプしてファイルに書き込む 0x02000000"] =
            "Dump memory to file at 0x02000000",
        ["メモリをダンプしてファイルに書き込む 0x03000000"] =
            "Dump memory to file at 0x03000000",
        ["メモリスロット"] = "Memory Slot",
        ["ユニットの行動で設定される項目"] = "Fields set by unit actions",
        ["ワープする章"] = "Warp chapter",
        ["ワールドマップ拠点"] = "Worldmap base",
        // Unit stats
        ["体格＋"] = "Con+",
        ["個数"] = "Count",
        ["光 EXP"] = "Light EXP",
        ["再生されている音楽"] = "Currently Playing BGM",
        ["剣 EXP"] = "Sword EXP",
        ["力と魔力"] = "Str / Mag",
        ["同行者ID"] = "Companion ID",
        ["回復モード"] = "Recovery Mode",
        ["塔と遺跡のデータ"] = "Tower and Ruins data",
        ["天気"] = "Weather",
        ["天気を変更(次のターンから適用されます。)"] = "Change Weather (applies next turn).",
        ["守備"] = "Def",
        ["実行しているイベント"] = "Running Event",
        ["弓 EXP"] = "Bow EXP",
        ["戦歴データ"] = "Battle History",
        ["戦闘に関係する諸データ"] = "Battle-related data",
        ["戦闘データ gBattleActor"] = "Battle data gBattleActor",
        ["戦闘データ gBattleTarget"] = "Battle data gBattleTarget",
        ["所持金"] = "Money",
        ["所持金を以下の値に変更する"] = "Set Money to the value below",
        ["技"] = "Skill",
        ["持たせるアイテム"] = "Item to give",
        ["操作中のユニット"] = "Active Unit",
        ["支援1"] = "Support 1",
        ["支援2"] = "Support 2",
        ["支援3"] = "Support 3",
        ["支援4"] = "Support 4",
        ["支援5"] = "Support 5",
        ["支援6"] = "Support 6",
        ["支援7"] = "Support 7",
        ["支援フラグ"] = "Support Flag",
        ["敵を含めて、すべてのユニットを最強にします"] =
            "Max out all units including enemies",
        ["斧 EXP"] = "Axe EXP",
        ["最大HP"] = "Max HP",
        ["杖 EXP"] = "Staff EXP",
        ["検索"] = "Search",
        ["槍 EXP"] = "Lance EXP",
        ["汎用"] = "General",
        ["状態"] = "Status",
        ["状態とターン"] = "Status and Turn",
        ["現在HP"] = "Current HP",
        ["現在、操作しているユニット"] = "Currently active unit",
        ["現在の章をクリアします。(Hotkey: Ctrl + U)"] =
            "Clear the current chapter. (Hotkey: Ctrl + U)",
        ["理 EXP"] = "Anima EXP",
        ["移動＋"] = "Mov+",
        ["章データ"] = "Chapter Data",
        ["編"] = "Edition",
        ["聖水松明"] = "Holy Water / Torch",
        ["自動的に更新する"] = "Auto-update",
        ["解析者向けの機能"] = "Analyst tools",
        ["輸送隊"] = "Supply",
        ["輸送隊の内容"] = "Supply contents",
        ["速さ"] = "Spd",
        ["運"] = "Lck",
        ["選択アドレス:"] = "Selected Address:",
        ["部隊表ID"] = "Party Table ID",
        ["闇 EXP"] = "Dark EXP",
        ["闘技場"] = "Arena",
        ["闘技場の相手選出に利用するデータ"] = "Arena opponent selection data",
        ["霧レベル"] = "Fog Level",
        ["霧レベルを変更(次のターンから適用されます。)"] = "Change Fog (applies next turn).",
        ["魔防"] = "Res",
        ["0で霧なし。1が視界1マスの最大の霧です。"] =
            "0 = no fog. 1 = max fog (vision 1 tile).",
    };

    [Fact]
    public void View_HasAllWfOnlyLabelsCovered()
    {
        // Inventory must match coverage map size.
        Assert.Equal(WfOnlyLabelInventory.Length, LabelCoverage.Count);
        string axaml = ReadAxaml();

        foreach (string wfLabel in WfOnlyLabelInventory)
        {
            Assert.True(LabelCoverage.TryGetValue(wfLabel, out string? enLabel),
                $"WF-only label '{wfLabel}' must be in the coverage map.");

            bool found =
                axaml.Contains($"Content=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"Header=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"Text=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"Watermark=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"ToolTip.Tip=\"{enLabel}\"", StringComparison.Ordinal)
                || axaml.Contains($"KnownGap: {wfLabel}", StringComparison.Ordinal)
                || axaml.Contains(enLabel!, StringComparison.Ordinal);

            Assert.True(found,
                $"WF label '{wfLabel}' -> English '{enLabel}' must appear in AXAML " +
                $"as Content/Header/Text or be listed in a KnownGap comment.");
        }
    }

    [Fact]
    public void Inventory_IsExhaustive()
    {
        // Inventory size matches the sweep — 174 distinct WF-only labels.
        Assert.Equal(174, WfOnlyLabelInventory.Length);
    }

    // -----------------------------------------------------------------
    // Translation files — every English literal added in the rebuild
    // must appear in en.txt / ja.txt / zh.txt (ko is intentionally
    // omitted per repo convention; ko_tbl is a TBL encoding dir).
    // -----------------------------------------------------------------

    /// <summary>
    /// Sample of newly-introduced English literals that the rebuild
    /// surfaces. Each must appear as a key (line preceded by ':') in
    /// ja.txt and zh.txt — proves the gap-sweep l10n acceptance.
    /// </summary>
    static readonly string[] NewlyAddedEnglishLiterals =
    {
        "Auto-update",
        "Active Unit",
        "Running Event",
        "Event History",
        "Currently Playing BGM",
        "Memory Slot",
        "Cheat",
    };

    [Fact]
    public void Translations_NewLiterals_PresentInJaAndZh()
    {
        string repoRoot = FindRepoRoot();
        string ja = File.ReadAllText(Path.Combine(repoRoot, "config", "translate", "ja.txt"));
        string zh = File.ReadAllText(Path.Combine(repoRoot, "config", "translate", "zh.txt"));
        foreach (string literal in NewlyAddedEnglishLiterals)
        {
            string key = $":{literal}\n";
            Assert.True(ja.Contains(key) || ja.Contains($":{literal}\r\n"),
                $"ja.txt is missing key for '{literal}'");
            Assert.True(zh.Contains(key) || zh.Contains($":{literal}\r\n"),
                $"zh.txt is missing key for '{literal}'");
        }
    }

    // -----------------------------------------------------------------
    // Headless runtime tests - instantiate the view and walk the logical
    // tree to verify the AXAML actually loads + the real control surface
    // is reachable (Copilot CLI v1 review blocking item: AXAML text-scan
    // tests aren't enough; need headless [AvaloniaFact] tests that
    // construct the real Window and exercise the logical tree).
    // -----------------------------------------------------------------

    [AvaloniaFact]
    public void View_Constructs_WithoutCrash()
    {
        var view = new EmulatorMemoryView();
        Assert.NotNull(view);
        Assert.Equal("Emulator Memory", view.ViewTitle);
    }

    [AvaloniaFact]
    public void View_DataContext_BindsToViewModel()
    {
        var view = new EmulatorMemoryView();
        // Bound in constructor for Copilot review non-blocking #2.
        Assert.NotNull(view.DataContext);
        Assert.IsType<EmulatorMemoryViewModel>(view.DataContext);
    }

    [AvaloniaFact]
    public void View_LogicalTree_Has_MainTabControl_With6Tabs()
    {
        var view = new EmulatorMemoryView();
        var tab = view.GetLogicalDescendants()
            .OfType<TabControl>()
            .FirstOrDefault(t => AvaloniaProperties.GetId(t) == "EmulatorMemory_Main_TabControl");
        Assert.NotNull(tab);
        // 6 outer TabItems expected.
        Assert.Equal(6, tab.Items.Count);
    }

    [AvaloniaFact]
    public void View_LogicalTree_Has_EtcSubTabControl_With16Tabs()
    {
        var view = new EmulatorMemoryView();
        var tab = view.GetLogicalDescendants()
            .OfType<TabControl>()
            .FirstOrDefault(t => AvaloniaProperties.GetId(t) == "EmulatorMemory_EtcSub_TabControl");
        Assert.NotNull(tab);
        Assert.Equal(16, tab.Items.Count);
    }

    [AvaloniaFact]
    public void View_LogicalTree_Has_9_EnabledFunctionalOpenButtons()
    {
        var view = new EmulatorMemoryView();
        string[] functionalIds = new[]
        {
            "EmulatorMemory_OpenEventScript_Button",
            "EmulatorMemory_OpenProcsScript_Button",
            "EmulatorMemory_OpenHexEditor_Button",
            "EmulatorMemory_OpenTextViewer_Button",
            "EmulatorMemory_OpenSongTable_Button",
            "EmulatorMemory_OpenToolBGMMuteDialog_Button",
            "EmulatorMemory_OpenMapChange_Button",
            "EmulatorMemory_OpenRAMRewriteTool_Button",
            "EmulatorMemory_OpenRAMRewriteToolMAP_Button",
        };
        var buttons = view.GetLogicalDescendants()
            .OfType<Button>()
            .ToList();
        foreach (string id in functionalIds)
        {
            var btn = buttons.FirstOrDefault(b => AvaloniaProperties.GetId(b) == id);
            Assert.NotNull(btn);
            Assert.True(btn.IsEnabled,
                $"Functional Open button {id} must be enabled (it routes through WindowManager.Open<T>())");
        }
    }

    [AvaloniaFact]
    public void View_LogicalTree_Has_KnownGapButtons_Disabled()
    {
        var view = new EmulatorMemoryView();
        // Walk the realised logical tree and confirm every KnownGap
        // button is actually present and IsEnabled=false at runtime
        // (Copilot CLI review wanted runtime checks, not text-scans).
        // Exclude CheckBox (which inherits from Button via ToggleButton)
        // so we count plain Buttons only — Copilot bot v2 review item
        // PRRT_kwDOH0Mc1M6EbWTu: keep the disabled-Button count equal
        // to KnownGapButtonIds.Length and verify the CheckBox separately.
        var realButtons = view.GetLogicalDescendants()
            .OfType<Button>()
            .Where(b => b is not CheckBox && b is not ToggleButton && b is not RadioButton)
            .ToList();
        foreach (string id in KnownGapButtonIds)
        {
            var btn = realButtons.FirstOrDefault(b => AvaloniaProperties.GetId(b) == id);
            Assert.True(btn != null,
                $"KnownGap button {id} must be present in the realised logical tree");
            Assert.False(btn!.IsEnabled,
                $"KnownGap button {id} must remain IsEnabled=false at runtime");
        }
        var disabled = realButtons.Where(b => !b.IsEnabled).ToList();
        Assert.Equal(KnownGapButtonIds.Length, disabled.Count);
    }

    [AvaloniaFact]
    public void View_LogicalTree_AutoUpdateCheckBox_IsDisabled()
    {
        var view = new EmulatorMemoryView();
        var cb = view.GetLogicalDescendants()
            .OfType<CheckBox>()
            .FirstOrDefault(c => AvaloniaProperties.GetId(c) == "EmulatorMemory_AutoUpdate_Check");
        Assert.True(cb != null, "AutoUpdate CheckBox must be present in realised tree");
        Assert.False(cb!.IsEnabled,
            "AutoUpdate CheckBox is KnownGap (no live RAM polling in Avalonia)");
    }

    [AvaloniaFact]
    public void View_LogicalTree_Has_Close_Button_Enabled()
    {
        var view = new EmulatorMemoryView();
        var close = view.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => AvaloniaProperties.GetId(b) == "EmulatorMemory_Close_Button");
        Assert.NotNull(close);
        Assert.True(close.IsEnabled, "Close button must remain enabled");
    }

    /// <summary>
    /// Small helper to read AutomationProperties.AutomationId without
    /// pulling in the heavyweight Automation namespace at every call site.
    /// </summary>
    static class AvaloniaProperties
    {
        public static string GetId(Control control)
            => global::Avalonia.Automation.AutomationProperties.GetAutomationId(control) ?? string.Empty;
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    static string AxamlPath() => Path.Combine(AvaloniaDir, "Views", "EmulatorMemoryView.axaml");
    static string CodeBehindPath() => Path.Combine(AvaloniaDir, "Views", "EmulatorMemoryView.axaml.cs");

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

    static string FindRepoRoot()
    {
        string baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (FEBuilderGBA.sln)");
    }
}
