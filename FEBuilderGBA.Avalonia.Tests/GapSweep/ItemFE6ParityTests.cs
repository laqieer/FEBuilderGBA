// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for ItemFE6View. (#402)
//
// Closes the 112 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `ItemFE6Form`:
//   - density verdict High 121/53 = -56.2% (target: AV >= 91)
//   - 44 WF-only labels (most map to existing AV English equivalents from #569
//     ItemEditor; the few unmapped get explicit KnownGap markers per
//     Copilot v1 review)
//   - reusable ja/zh literals from #569 (`[HardCoding]`, `Indirect Weapon
//     Effect`, `Stat Bonuses Preview`); FE6-only literal `Shingeki Shop:`
//     marked `KnownGap NonEnglish` because the field has no FE7/8 sibling.
//   - 6 new jump manifest entries:
//       JumpToNameText       -> TextViewerView
//       JumpToUseDescText    -> TextViewerView
//       JumpToHardcoding     -> PatchManagerView
//       JumpToWeaponEffect   -> ItemWeaponEffectViewerView
//       JumpToStatBonuses    -> ItemStatBonusesViewerView
//       JumpToEffectiveness  -> ItemEffectivenessViewerView
//
// Mirrors the parity-test pattern from PR #569 (ItemForm),
// PR #435 (ImagePortraitFE6), and PR #571 (AIScript).
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the ItemFE6Form parity raise (#402) is permanent.
/// </summary>
[Collection("SharedState")]
public class ItemFE6ParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach within 25% of WF.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 121 control instantiations (per the
    /// 2026-05-21 density-sweep manifest). To stay inside the MEDIUM
    /// verdict we need AV >= ceil(121 * 0.75) = 91. The pre-#402 baseline
    /// was 53 (-56.2%); the gap-sweep AXAML additions bring it back
    /// inside the threshold.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 121;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 91
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} " +
            $"(75% of WF={WfControlCount}) to stay inside the MEDIUM verdict.");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasHardCodingWarning_Link()
    {
        // WF `HardCodingWarningLabel` - clickable label that opens the
        // Patch Manager filtered on the item's HARDCODING_ITEM= row.
        string axaml = ReadAxaml();
        int startIdx = axaml.IndexOf(
            "AutomationProperties.AutomationId=\"ItemFE6_HardCodingWarning_Link\"",
            StringComparison.Ordinal);
        Assert.True(startIdx >= 0,
            "HardCoding link element with AutomationId is missing from AXAML.");

        int openIdx = axaml.LastIndexOf("<TextBlock", startIdx, StringComparison.Ordinal);
        Assert.True(openIdx >= 0, "Could not find <TextBlock for HardCoding link.");
        int closeIdx = axaml.IndexOf("/>", startIdx, StringComparison.Ordinal);
        int explicitClose = axaml.IndexOf("</TextBlock>", startIdx, StringComparison.Ordinal);
        int endIdx;
        if (closeIdx > 0 && (explicitClose < 0 || closeIdx < explicitClose))
            endIdx = closeIdx + 2;
        else
            endIdx = explicitClose + "</TextBlock>".Length;
        Assert.True(endIdx > openIdx, "Could not find element end tag.");

        string elementText = axaml.Substring(openIdx, endIdx - openIdx);
        Assert.Contains("IsVisible=\"False\"", elementText);
    }

    [Fact]
    public void View_HasWeaponEffect_JumpButton()
    {
        // WF `JumpToITEMEFFECT` ("間接エフェクト Jump") - opens
        // ItemWeaponEffectViewerView at the row whose B0 equals this
        // item's list index.
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationId=\"ItemFE6_JumpToWeaponEffect_Button\"",
            axaml);
        Assert.Contains("Click=\"JumpToWeaponEffect_Click\"", axaml);
    }

    [Fact]
    public void View_HasNewallocKnownGapMarker()
    {
        // Newalloc buttons (WF L_12_NEWALLOC_ITEMSTATBOOSTER /
        // L_16_NEWALLOC_EFFECTIVENESS) are deliberately deferred because the
        // Avalonia head does not yet wire CoreState.AppendBinaryData.
        string axaml = ReadAxaml();
        Assert.Contains("KnownGap", axaml);
        Assert.Contains("NEWALLOC", axaml);
        Assert.Contains("CoreState.AppendBinaryData", axaml);
    }

    [Fact]
    public void View_HasShingekiShopNonEnglishMarker()
    {
        // FE6-only Shingeki Shop label - no English equivalent in FE7/8
        // ItemForm. Must carry a KnownGap NonEnglish marker (plan v1-B3).
        string axaml = ReadAxaml();
        Assert.Contains("KnownGap NonEnglish", axaml);
        Assert.Contains("Shingeki", axaml);
    }

    // -----------------------------------------------------------------
    // Phase 5 - code-behind structural assertions.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        // Write_Click must open/commit/rollback an undo scope.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_WriteHandler_RoundTripsThroughViewModel()
    {
        // No direct ROM writes - all mutation must go through _vm.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.DoesNotContain(".write_u8(", source);
        Assert.DoesNotContain(".write_u16(", source);
        Assert.DoesNotContain(".write_u32(", source);
        Assert.DoesNotContain(".SetU8(", source);
        Assert.DoesNotContain(".SetU16(", source);
        Assert.DoesNotContain(".SetU32(", source);
        Assert.Contains("_vm.WriteItem", source);
    }

    [Fact]
    public void View_HardCodingLink_UsesListIndex_NotItemNumber()
    {
        // Copilot v1-B2: HardCoding handler must read the LIST INDEX
        // (EntryList.SelectedOriginalIndex), NOT the editable B6
        // ItemNumberBox.Value. Mirrors WF
        // `this.AddressList.SelectedIndex`.
        string source = File.ReadAllText(ViewCodeBehindPath());
        int handlerIdx = source.IndexOf("OnHardCodingLink_Click", StringComparison.Ordinal);
        Assert.True(handlerIdx >= 0, "OnHardCodingLink_Click handler missing.");
        int searchEnd = source.IndexOf("\n        }", handlerIdx, StringComparison.Ordinal);
        Assert.True(searchEnd > handlerIdx, "Could not delimit handler scope.");
        string handlerBody = source.Substring(handlerIdx, searchEnd - handlerIdx);
        Assert.Contains("SelectedOriginalIndex", handlerBody);
    }

    [Fact]
    public void View_WeaponEffectJump_UsesListIndex_NotItemNumber()
    {
        // Copilot v1-B2: weapon-effect jump handler must read the LIST
        // INDEX from EntryList.SelectedOriginalIndex, NOT the editable
        // ItemNumberBox.Value.
        string source = File.ReadAllText(ViewCodeBehindPath());
        int handlerIdx = source.IndexOf("JumpToWeaponEffect_Click", StringComparison.Ordinal);
        Assert.True(handlerIdx >= 0, "JumpToWeaponEffect_Click handler missing.");
        int searchEnd = source.IndexOf("\n        }", handlerIdx, StringComparison.Ordinal);
        Assert.True(searchEnd > handlerIdx, "Could not delimit handler scope.");
        string handlerBody = source.Substring(handlerIdx, searchEnd - handlerIdx);
        Assert.Contains("SelectedOriginalIndex", handlerBody);
    }

    [Fact]
    public void View_HardCodingLink_RoutesToPatchManager()
    {
        // The HardCoding click handler must call Navigate<PatchManagerView>
        // and use the WF `HARDCODING_ITEM=` filter prefix exactly.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("OnHardCodingLink_Click", source);
        Assert.Contains("Navigate<PatchManagerView>", source);
        Assert.Contains("HARDCODING_ITEM=", source);
    }

    [Fact]
    public void View_WeaponEffectJump_SearchesByItemId_NotIndexMath()
    {
        // The WF receiver iterates the table looking for `B0 == itemId`.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("FindWeaponEffectAddrForItem", source);
        Assert.Contains("u8(addr) == itemId", source);
    }

    // -----------------------------------------------------------------
    // Navigation manifest (Phase 4) - 7 rows total (3 text + 4 jumps).
    // -----------------------------------------------------------------

    [Fact]
    public void NavigationManifest_AddsTwoNewTextJumps()
    {
        var vm = new ItemFE6ViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToNameText" &&
            t.TargetViewType == typeof(TextViewerView));
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToUseDescText" &&
            t.TargetViewType == typeof(TextViewerView));
    }

    [Fact]
    public void NavigationManifest_AddsExactlyFourNewJumps()
    {
        var vm = new ItemFE6ViewModel();
        var targets = vm.GetNavigationTargets();

        Assert.Contains(targets, t =>
            t.CommandName == "JumpToHardcoding" &&
            t.TargetViewType == typeof(PatchManagerView) &&
            t.IssueRef == null);
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToWeaponEffect" &&
            t.TargetViewType == typeof(ItemWeaponEffectViewerView) &&
            t.IssueRef == null);
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToStatBonuses" &&
            t.TargetViewType == typeof(ItemStatBonusesViewerView) &&
            t.IssueRef == null);
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToEffectiveness" &&
            t.TargetViewType == typeof(ItemEffectivenessViewerView) &&
            t.IssueRef == null);
    }

    [Fact]
    public void NavigationManifest_TotalRowCountMatchesExpected()
    {
        // 3 text jumps (DescText + NameText + UseDescText) + 4 #402
        // additions (Hardcoding/WeaponEffect/StatBonuses/Effectiveness)
        // = 7 rows.
        var vm = new ItemFE6ViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Equal(7, targets.Count);
    }

    [Fact]
    public void NavigationManifest_AllRowsAreMatch_NoKnownGaps()
    {
        var vm = new ItemFE6ViewModel();
        foreach (var t in vm.GetNavigationTargets())
            Assert.Null(t.IssueRef);
    }

    // -----------------------------------------------------------------
    // Localisation (Phase 6).
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("[HardCoding]")]
    [InlineData("Indirect Weapon Effect")]
    [InlineData("Stat Bonuses Preview")]
    public void Localisation_NewLiterals_AreTranslated_InJaAndZh(string literal)
    {
        string repoRoot = FindRepoRoot();
        string jaPath = Path.Combine(repoRoot, "config", "translate", "ja.txt");
        string zhPath = Path.Combine(repoRoot, "config", "translate", "zh.txt");
        Assert.True(File.Exists(jaPath), $"ja.txt not found: {jaPath}");
        Assert.True(File.Exists(zhPath), $"zh.txt not found: {zhPath}");

        string ja = File.ReadAllText(jaPath);
        string zh = File.ReadAllText(zhPath);
        Assert.Contains(literal, ja);
        Assert.Contains(literal, zh);
    }

    // -----------------------------------------------------------------
    // FindWeaponEffectAddrForItem (Phase 5).
    // -----------------------------------------------------------------

    [Fact]
    public void FindWeaponEffectAddrForItem_ReturnsZeroWhenRomNotLoaded()
    {
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            uint addr = ItemFE6View.FindWeaponEffectAddrForItem(1);
            Assert.Equal(0u, addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindWeaponEffectAddrForItem_LocatesMatchingRow_OnSyntheticRom()
    {
        var rom = MakeFE6RomWithItemEffectTable(
            out uint tableAddr,
            entries: new uint[] { 0x01, 0x05, 0x0A, 0x14 });
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint addrForItem5 = ItemFE6View.FindWeaponEffectAddrForItem(5);
            Assert.Equal(tableAddr + 16, addrForItem5);

            uint addrForItem0xA = ItemFE6View.FindWeaponEffectAddrForItem(0x0A);
            Assert.Equal(tableAddr + 32, addrForItem0xA);

            uint addrForMissing = ItemFE6View.FindWeaponEffectAddrForItem(0xFF);
            Assert.Equal(0u, addrForMissing);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE6 (AFEJ01) ROM with an indirect-weapon-effect
    /// table whose rows hold the supplied entries. Each row is 16 bytes
    /// wide; B0 == the corresponding item id. The pointer slot at
    /// <c>ROMFE6JP.item_effect_pointer</c> (0x49db4) is patched to point
    /// at the synthetic table so <c>rom.p32(item_effect_pointer)</c>
    /// resolves to our table base.
    /// </summary>
    static ROM MakeFE6RomWithItemEffectTable(out uint tableAddr, uint[] entries)
    {
        var rom = new ROM();
        // FE6 ROM is 8 MB.
        rom.LoadLow("synth.gba", new byte[0x800000], "AFEJ01");

        tableAddr = 0x200000;
        // FE6 item_effect_pointer slot is at 0x49db4.
        WriteU32(rom.Data, 0x49db4, 0x08000000u | tableAddr);

        for (int i = 0; i < entries.Length; i++)
        {
            uint addr = tableAddr + (uint)i * 16;
            rom.Data[addr + 0] = (byte)entries[i];
            for (int b = 1; b < 16; b++)
                rom.Data[addr + b] = 0xAA;
        }
        return rom;
    }

    static void WriteU32(byte[] data, uint addr, uint value)
    {
        data[addr + 0] = (byte)(value >> 0);
        data[addr + 1] = (byte)(value >> 8);
        data[addr + 2] = (byte)(value >> 16);
        data[addr + 3] = (byte)(value >> 24);
    }

    static string AxamlPath() => Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia",
        "Views", "ItemFE6View.axaml");

    static string ViewCodeBehindPath() => Path.Combine(FindRepoRoot(),
        "FEBuilderGBA.Avalonia", "Views", "ItemFE6View.axaml.cs");

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string FindRepoRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }
}
