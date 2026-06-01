// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5 gap-sweep regression tests for ItemEditorView. (#409)
//
// Closes the 74 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `ItemForm`:
//   - density verdict Medium 128/88 = -31.3 % (target: AV >= 96)
//   - 45 WF-only labels (most mapped to existing AV English labels — Japanese
//     equivalents of stat names; the few unmapped get explicit out-of-scope
//     KnownGap markers per Copilot v1 review)
//   - 26 untranslated literals (already covered in ja/zh — ko intentionally
//     unmapped, matching #411 / #412 / #413 convention)
//   - 3 missing jump manifest entries (now `Match`):
//       JumpToWeaponEffect   -> ItemWeaponEffectViewerView
//       JumpToHardcoding     -> PatchManagerView
//       JumpToWeaponDebuffs  -> PatchManagerView
//
// Mirrors the parity-test pattern from PR #561 (EDForm), PR #558 (SongTrack),
// and PR #549 (OPClassDemoFE7).
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
/// Tests proving the ItemForm parity raise (#409) is permanent.
/// </summary>
[Collection("SharedState")]
public class ItemEditorParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach within 25 % of WF.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 128 control instantiations (per the
    /// 2026-05-26 density-sweep manifest). To stay inside the MEDIUM
    /// verdict we need AV >= ceil(128 * 0.75) = 96. The pre-#409 baseline
    /// was 88 (-31.3 %); the gap-sweep AXAML additions bring it back
    /// inside the threshold.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 128;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 96
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} " +
            $"(75 % of WF={WfControlCount}) to stay inside the MEDIUM verdict.");
    }

    // -----------------------------------------------------------------
    // Phase 5 - control surface assertions (Roslyn-static AXAML read).
    // The AutomationId vocabulary mirrors the new WF parity items.
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasHardCodingWarning_Link()
    {
        // WF `HardCodingWarningLabel` — clickable label that opens the
        // Patch Manager filtered on the item's HARDCODING_ITEM= row.
        // Copilot bot review on PR #569: assert IsVisible="False" on the
        // SPECIFIC HardCoding link node, not anywhere in the file (other
        // elements such as ListPreviewBorder are also hidden by default,
        // so a global `Contains("IsVisible=\"False\"")` would pass even if
        // HardCoding accidentally started visible).
        //
        // The Avalonia AXAML attribute is `AutomationProperties.AutomationId`
        // (xmlns-qualified). XDocument exposes the attribute's LocalName as
        // just `AutomationId` but the LINQ extension is more readable if we
        // match the full prefixed string in the raw text instead. We use the
        // raw text scan to locate the line, then re-parse only that element
        // through XElement.Parse so the visibility assertion is scoped.
        string axaml = ReadAxaml();
        int startIdx = axaml.IndexOf(
            "AutomationProperties.AutomationId=\"ItemEditor_HardCodingWarning_Link\"",
            StringComparison.Ordinal);
        Assert.True(startIdx >= 0,
            "HardCoding link element with AutomationId is missing from AXAML.");

        // Walk backwards to find the element open tag `<TextBlock ...`.
        int openIdx = axaml.LastIndexOf("<TextBlock", startIdx, StringComparison.Ordinal);
        Assert.True(openIdx >= 0, "Could not find <TextBlock for HardCoding link.");
        // Walk forwards to the element's self-closing `/>` or matching `</TextBlock>`.
        int closeIdx = axaml.IndexOf("/>", startIdx, StringComparison.Ordinal);
        int explicitClose = axaml.IndexOf("</TextBlock>", startIdx, StringComparison.Ordinal);
        int endIdx;
        if (closeIdx > 0 && (explicitClose < 0 || closeIdx < explicitClose))
            endIdx = closeIdx + 2;
        else
            endIdx = explicitClose + "</TextBlock>".Length;
        Assert.True(endIdx > openIdx, "Could not find element end tag.");

        string elementText = axaml.Substring(openIdx, endIdx - openIdx);
        // The default visibility on the HardCoding link must be False so the
        // warning only appears once OnItemSelected sets it from
        // IAsmMapCache.IsHardCodeItem.
        Assert.Contains("IsVisible=\"False\"", elementText);
    }

    [Fact]
    public void View_HasWeaponEffect_JumpButton()
    {
        // WF `JumpToITEMEFFECT` ("間接エフェクト Jump") — opens
        // ItemWeaponEffectViewerView at the row whose B0 equals this item id.
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationId=\"ItemEditor_JumpToWeaponEffect_Button\"",
            axaml);
        Assert.Contains("Click=\"JumpToWeaponEffect_Click\"", axaml);
    }

    [Fact]
    public void View_HasWeaponDebuffs_Link()
    {
        // WF `J_33_Click` — when SkillSystem patch is present this label
        // becomes a hyperlink that opens Patch Manager filtered on
        // `defWeaponDebuffsTable`.
        string axaml = ReadAxaml();
        Assert.Contains(
            "AutomationId=\"ItemEditor_WeaponDebuffs_Link\"",
            axaml);
        Assert.Contains("PointerPressed=\"OnWeaponDebuffsLink_Click\"", axaml);
    }

    [Fact]
    public void View_HasUnk33Label_ForPatchAwareRename()
    {
        // The B33 label is a named TextBlock so the code-behind can rename
        // it to "Debuff (B33):" when SkillSystem is detected (mirrors WF
        // `J_33.Text = "Debuff"`).
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"Unk33Label\"", axaml);
        Assert.Contains("AutomationId=\"ItemEditor_Unk33_Label\"", axaml);
    }

    [Fact]
    public void View_HasWiredNewallocButtons()
    {
        // #831: the Newalloc buttons (WF L_12_NEWALLOC_ITEMSTATBOOSTER /
        // L_16_NEWALLOC_EFFECTIVENESS) are now WIRED via ItemAllocCore + the
        // wired CoreState.AppendBinaryData seam (#796). The AXAML must carry
        // both buttons (AutomationId + Click handler), and the stale "not
        // wired / deferred" deferral marker must be gone.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"ItemEditor_AllocStatBonuses_Button\"", axaml);
        Assert.Contains("AutomationId=\"ItemEditor_AllocEffectiveness_Button\"", axaml);
        Assert.Contains("Click=\"AllocStatBonuses_Click\"", axaml);
        Assert.Contains("Click=\"AllocEffectiveness_Click\"", axaml);
        // The stale "does not yet wire CoreState.AppendBinaryData" deferral
        // marker must no longer be present (the buttons are wired now).
        Assert.DoesNotContain("does not yet wire", axaml);
    }

    // -----------------------------------------------------------------
    // Phase 5 - code-behind structural assertions.
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        // Pre-existing pattern - Write_Click must open/commit/rollback an
        // undo scope. This regression-pins the pattern in place.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    [Fact]
    public void View_WriteHandler_RoundTripsThroughViewModel()
    {
        // No direct ROM writes — all mutation must go through _vm so the
        // EditorFormRef codec is reused.
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
    public void View_HardCodingLink_RoutesToPatchManager()
    {
        // The HardCoding click handler must call Navigate<PatchManagerView>
        // and use the WF `HARDCODING_ITEM=` filter prefix exactly. The WF
        // call site `f.JumpTo("HARDCODING_ITEM=" + U.ToHexString2(idx), 0)`
        // requires the hex-2-digit format so the Patch Manager filter
        // matches the patch definitions in `config/patch2/`.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("OnHardCodingLink_Click", source);
        Assert.Contains("Navigate<PatchManagerView>", source);
        Assert.Contains("HARDCODING_ITEM=", source);
    }

    [Fact]
    public void View_WeaponEffectJump_SearchesByItemId_NotIndexMath()
    {
        // Copilot v1 review C3: the WF receiver iterates the table looking
        // for `B0 == itemId`. The AV handler MUST mirror that scan; using
        // `base + itemId * 16` would land on the wrong row when items are
        // not contiguous in the indirect-effect table. Roslyn-static
        // assertion ensures the search helper exists.
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("FindWeaponEffectAddrForItem", source);
        // Search semantics: `if (rom.u8(addr) == itemId) return addr;`
        Assert.Contains("u8(addr) == itemId", source);
    }

    [Fact]
    public void View_WeaponDebuffs_RoutesToPatchManager()
    {
        // The Debuff click handler must call Navigate<PatchManagerView>
        // and use the WF `defWeaponDebuffsTable` filter exactly. (J_33
        // click mirror.)
        string source = File.ReadAllText(ViewCodeBehindPath());
        Assert.Contains("OnWeaponDebuffsLink_Click", source);
        Assert.Contains("defWeaponDebuffsTable", source);
    }

    // -----------------------------------------------------------------
    // Navigation manifest (Phase 4) — exactly 3 new wired jumps.
    // Per Copilot v1 review C4: assert the SPECIFIC three new commands
    // and their targets, NOT a misleading total. The pre-#409 manifest
    // had 8 rows; this PR adds exactly 3, total 11.
    // -----------------------------------------------------------------

    [Fact]
    public void NavigationManifest_AddsExactlyThreeNewItemFormJumps()
    {
        var vm = new ItemEditorViewModel();
        var targets = vm.GetNavigationTargets();

        // The three #409 additions.
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToWeaponEffect" &&
            t.TargetViewType == typeof(ItemWeaponEffectViewerView) &&
            t.IssueRef == null);
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToHardcoding" &&
            t.TargetViewType == typeof(PatchManagerView) &&
            t.IssueRef == null);
        Assert.Contains(targets, t =>
            t.CommandName == "JumpToWeaponDebuffs" &&
            t.TargetViewType == typeof(PatchManagerView) &&
            t.IssueRef == null);
    }

    [Fact]
    public void NavigationManifest_TotalRowCountMatchesExpected()
    {
        // 5 pre-existing entries (3 text jumps + 3 stat-bonus variants +
        // 2 effectiveness variants = 8) + 3 #409 additions = 11.
        var vm = new ItemEditorViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Equal(11, targets.Count);
    }

    [Fact]
    public void NavigationManifest_AllRowsAreMatch_NoKnownGaps()
    {
        // After #409 lands, every ItemEditorViewModel manifest row is
        // wired (IssueRef == null). The Effectiveness rows that were
        // KnownGap=#362 / #363 dropped that tag in #456 and PR #461.
        var vm = new ItemEditorViewModel();
        foreach (var t in vm.GetNavigationTargets())
            Assert.Null(t.IssueRef);
    }

    // -----------------------------------------------------------------
    // Localisation (Phase 6) — verify the new English literals introduced
    // by this PR exist in ja/zh. ko intentionally unmapped per project
    // convention (matches #411 / #412 / #413 PRs).
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("[HardCoding]")]
    [InlineData("Indirect Weapon Effect")]
    [InlineData("Debuff Table")]
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
    // Core seam (Phase 5) — IAsmMapCache.IsHardCodeItem must be on the
    // interface so the Avalonia head can call it through CoreState.
    // -----------------------------------------------------------------

    [Fact]
    public void Core_IAsmMapCache_DeclaresIsHardCodeItem()
    {
        // Reflection-check the interface so a future refactor that
        // accidentally removes the method fails this test rather than
        // crashing the AV view at runtime when the warning label tries
        // to evaluate the patch state.
        var method = typeof(IAsmMapCache).GetMethod("IsHardCodeItem");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method!.ReturnType);
        var prms = method.GetParameters();
        Assert.Single(prms);
        Assert.Equal(typeof(uint), prms[0].ParameterType);
    }

    // -----------------------------------------------------------------
    // FindWeaponEffectAddrForItem (Phase 5) — Copilot v1 review C3 says
    // the search must walk by B0 == itemId on the indirect-effect table.
    // -----------------------------------------------------------------

    [Fact]
    public void FindWeaponEffectAddrForItem_ReturnsZeroWhenRomNotLoaded()
    {
        // Without a ROM the search must return 0 (no entry found) and not
        // throw — the click handler navigates to address 0 in that case.
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = null;
            uint addr = ItemEditorView.FindWeaponEffectAddrForItem(1);
            Assert.Equal(0u, addr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void FindWeaponEffectAddrForItem_LocatesMatchingRow_OnSyntheticRom()
    {
        var rom = MakeRomWithItemEffectTable(
            out uint tableAddr,
            entries: new uint[] { 0x01, 0x05, 0x0A, 0x14 });
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint addrForItem5 = ItemEditorView.FindWeaponEffectAddrForItem(5);
            // The synthetic table places item id 5 at row index 1, so the
            // expected addr is tableAddr + 16.
            Assert.Equal(tableAddr + 16, addrForItem5);

            uint addrForItem0xA = ItemEditorView.FindWeaponEffectAddrForItem(0x0A);
            Assert.Equal(tableAddr + 32, addrForItem0xA);

            uint addrForMissing = ItemEditorView.FindWeaponEffectAddrForItem(0xFF);
            Assert.Equal(0u, addrForMissing);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM with an indirect-weapon-effect table whose
    /// rows hold the supplied <paramref name="entries"/>. Each row is 16 bytes
    /// wide; B0 == the corresponding item id. The pointer slot at
    /// <c>ROMFE8U.item_effect_pointer</c> (0x58014) is patched to point at
    /// the synthetic table so <c>rom.p32(item_effect_pointer)</c> resolves
    /// to our table base.
    /// </summary>
    static ROM MakeRomWithItemEffectTable(out uint tableAddr, uint[] entries)
    {
        var rom = new ROM();
        // FE8U needs 16 MB to register as BE8E01.
        rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

        tableAddr = 0x200000;
        // FE8U item_effect_pointer slot is at 0x58014 — patch it so the
        // dereference lands on our synthetic table.
        WriteU32(rom.Data, 0x58014, 0x08000000u | tableAddr);

        // Plant each entry: B0 = entries[i], B1..B15 arbitrary non-zero so
        // the WF parser's four-dword-zero terminator does not fire on a
        // real row.
        for (int i = 0; i < entries.Length; i++)
        {
            uint addr = tableAddr + (uint)i * 16;
            rom.Data[addr + 0] = (byte)entries[i];
            for (int b = 1; b < 16; b++)
                rom.Data[addr + b] = (byte)(0x10 + b);
        }

        // Terminator: 0xFFFF at addr+0..+1 of the row after the last entry
        // forces an early break (avoids scanning into uninitialised bytes
        // that happen to look like real rows).
        uint termAddr = tableAddr + (uint)entries.Length * 16;
        rom.Data[termAddr + 0] = 0xFF;
        rom.Data[termAddr + 1] = 0xFF;
        return rom;
    }

    static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset + 0] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ItemEditorView.axaml");
    }

    static string ViewCodeBehindPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "ItemEditorView.axaml.cs");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            throw new InvalidOperationException(
                "Could not find FEBuilderGBA.sln from test base directory");
        return dir;
    }
}
