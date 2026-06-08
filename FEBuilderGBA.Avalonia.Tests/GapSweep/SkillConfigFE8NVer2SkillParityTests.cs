// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5/6 gap-sweep regression tests for SkillConfigFE8NVer2SkillView. (#396)
//
// Closes the 150 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `SkillConfigFE8NVer2SkillForm` (HIGH density 17/136 == -87.5%, 31 WF-only
// labels, 0 common labels). Each assertion maps to a concrete acceptance-
// criterion bullet in the issue body and to a Copilot CLI plan-review finding.
//
// Mirrors the exact pattern PR #525 established for `SkillConfigSkillSystem` and
// PR #516 for `SkillConfigFE8UCSkillSys09x`. FE8N v2 specifics differ:
//   - Pattern scan key: rom.u32(0x89268+4) for iconExPointer, then byte-pattern
//     scan for [0x50,0x93,0x08,0x08,0x48,0x93,0x08,0x08] in 0xE00000..0.
//   - Variable row stride (ICON_LIST_SIZE detected at runtime, 16..40).
//   - Icon storage uses rom.p32(RomInfo.icon_pointer) + 128 * (0x100+id) — NOT
//     the SkillSystem striped-table model.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the SkillConfigFE8NVer2 parity raise (#396) is permanent.
///
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class SkillConfigFE8NVer2SkillParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 136 control instantiations. To leave the HIGH
    /// verdict we need AV >= ceil(136 * 0.5) + 1 = 69 so the actual delta
    /// is (69-136)/136 = -49.26% which is below the HIGH boundary at -50.0%.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // Strict MEDIUM boundary: 68 gives exactly -50.0% (HIGH); 69 gives
        // -49.26% (MEDIUM). Use 69 as the minimum gate.
        const int MinAvControls = 69;
        Assert.True(avCount >= MinAvControls,
            $"AV control count {avCount} must be >= {MinAvControls} (strict MEDIUM cutoff, WF=136)");
    }

    // -----------------------------------------------------------------
    // Pointer-location helpers - planted patterns resolve correctly.
    // -----------------------------------------------------------------

    [Fact]
    public void Helper_FindSkillFE8NVer2SkillPointerLocation_PlantsScanned()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Diagnostic: prove the byte-pattern grep can find the planted
            // pattern at 0xE10000 (this is the first step of the scan).
            byte[] iconPattern = new byte[] { 0x50, 0x93, 0x08, 0x08, 0x48, 0x93, 0x08, 0x08 };
            uint hit = U.Grep(rom.Data, iconPattern, 0xE00000, 0, 4);
            Assert.NotEqual(U.NOT_FOUND, hit);
            Assert.Equal(0xE10000u, hit);

            // Diagnostic: iconExPointer at 0x8926C must be a safe pointer.
            uint iconExPointer = rom.u32(0x89268 + 4);
            Assert.True(U.isSafetyPointer(iconExPointer, rom),
                $"iconExPointer 0x{iconExPointer:X} must be a safe pointer");

            uint loc = PreviewIconHelper.FindSkillFE8NVer2SkillPointerLocation();
            // The planted FE8N v2 byte pattern in the scan window must resolve
            // to a non-zero pointer location (the WF `g_SkillBaseAddress`).
            Assert.NotEqual(0u, loc);
            Assert.NotEqual(U.NOT_FOUND, loc);
            Assert.True(U.isSafetyOffset(loc + 3, rom));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Helper_FindSkillFE8NVer2AnimeBaseAddress_Resolves()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Force a fresh scan (the test fixture may have populated the
            // cache from a previous test in the same xUnit process).
            PreviewIconHelper.ResetFE8NVer2Cache();

            uint loc = PreviewIconHelper.FindSkillFE8NVer2SkillPointerLocation();
            Assert.NotEqual(U.NOT_FOUND, loc);
            uint animeBase = PreviewIconHelper.FindSkillFE8NVer2AnimeBaseAddress();
            // Synthetic ROM plants the anime base at 0xE30000 via slot[8].
            Assert.Equal(0xE30000u, animeBase);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Helper_GetFE8NVer2IconListSize_DetectsStrideFromRom()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Drive the scan first so the size cache is populated.
            uint loc = PreviewIconHelper.FindSkillFE8NVer2SkillPointerLocation();
            Assert.NotEqual(U.NOT_FOUND, loc);
            uint size = PreviewIconHelper.GetFE8NVer2IconListSize();
            // Synthetic ROM plants the default sizeof-16 layout.
            Assert.Equal(16u, size);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Naming consolidation - canonical VM name matches the View suffix-strip.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_HasCanonicalName_AndOldNameDeleted()
    {
        var avAsm = typeof(SkillConfigFE8NVer2SkillView).Assembly;
        var canonical = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillConfigFE8NVer2SkillViewModel");
        var legacy = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillConfigFE8NVer2SkillViewViewModel");
        Assert.NotNull(canonical);
        Assert.Null(legacy);
    }

    [Fact]
    public void ViewModel_ImplementsNavigationTargetSourceInterface()
    {
        var t = typeof(SkillConfigFE8NVer2SkillViewModel);
        Assert.Contains(typeof(INavigationTargetSource), t.GetInterfaces());
    }

    [Fact]
    public void ViewModel_DeriveViewName_MatchesCanonicalViewName()
    {
        string derived = JumpParityScanner.DeriveViewNameFromVmName(
            typeof(SkillConfigFE8NVer2SkillViewModel).Name);
        Assert.Equal("SkillConfigFE8NVer2SkillView", derived);
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) - Manifest declares the WF callsite as KnownGap.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresJumpToAnimationCreator()
    {
        var vm = new SkillConfigFE8NVer2SkillViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(ToolAnimationCreatorView));
    }

    /// <summary>
    /// JumpToAnimationCreator must carry a non-null IssueRef so the gap-sweep
    /// scanner reports it as `KnownGap` until the ToolAnimationCreatorView
    /// Init flow lands (#500).
    /// </summary>
    [Fact]
    public void ViewModel_JumpToAnimationCreator_IsMarkedAsKnownGap()
    {
        var vm = new SkillConfigFE8NVer2SkillViewModel();
        var target = vm.GetNavigationTargets()
            .FirstOrDefault(t => t.TargetViewType == typeof(ToolAnimationCreatorView));
        Assert.NotNull(target);
        Assert.False(string.IsNullOrEmpty(target!.IssueRef),
            "JumpToAnimationCreator must carry a non-null IssueRef until ToolAnimationCreatorView.Init lands (#500)");
    }

    [Fact]
    public void JumpParityScanner_ToCreator_IsKnownGap()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "SkillConfigFE8NVer2SkillForm",
                TargetForm: "ToolAnimationCreatorForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "SkillConfigFE8NVer2SkillViewModel",
                SourceView: "SkillConfigFE8NVer2SkillView",
                Command: "JumpToAnimationCreator",
                TargetView: "ToolAnimationCreatorView",
                IssueRef: "#500"),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "SkillConfigFE8NVer2SkillForm" &&
            r.TargetWfType == "ToolAnimationCreatorForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.KnownGap, match!.Status);
    }

    [Fact]
    public void ListParityHelper_DeclaresWfAvFormPair()
    {
        var extras = ListParityHelper.GetExtraCrossViewMappings();
        Assert.True(extras.ContainsKey("SkillConfigFE8NVer2SkillView"),
            "ListParityHelper.KnownExtraCrossViewMappings must declare SkillConfigFE8NVer2SkillView");
        Assert.Equal("SkillConfigFE8NVer2SkillForm", extras["SkillConfigFE8NVer2SkillView"]);
    }

    // -----------------------------------------------------------------
    // ViewModel state - Phase 1 new fields populated.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_PopulatesFromSyntheticRom()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer2SkillViewModel();
            var items = vm.LoadList();
            // Synthetic ROM plants rows 0-4 (5 rows total). LoadList must
            // produce a sizeable count to prove iteration didn't truncate.
            Assert.True(items.Count >= 4,
                $"LoadList must populate multiple rows - got {items.Count}");
            Assert.True(vm.ReadStartAddress > 0, "LoadList must populate ReadStartAddress");
        }
        finally
        {
            CoreState.ROM = prevRom;
            // Race-condition guard for parallel tests (Copilot bot review on PR #525).
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    [Fact]
    public void ViewModel_LoadList_DetectsIconListSize16_DefaultLayout()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer2SkillViewModel();
            vm.LoadList();
            // Default synthetic ROM ships ICON_LIST_SIZE = 16 (no Item2).
            Assert.Equal(16u, vm.IconListSize);
            Assert.False(vm.HasItem2, "ICON_LIST_SIZE=16 must NOT enable Item2 row");
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    /// <summary>
    /// Copilot CLI PR review (PR #598) finding 3: stride-20+ Item2 path must
    /// be covered. Build a synthetic ROM with `ICON_LIST_SIZE = 20`, populate
    /// the Item2 (P16) pointer for row 1, assert that:
    ///   - `IconListSize` reports 20 and `HasItem2` is true.
    ///   - `LoadEntry` reads the P16 pointer offset.
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_DetectsIconListSize20_EnablesItem2()
    {
        ROM rom = MakeStride20FE8NVer2Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer2SkillViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);
            Assert.Equal(20u, vm.IconListSize);
            Assert.True(vm.HasItem2, "ICON_LIST_SIZE=20 must enable the Item2 row");

            // Row 1 has Item2 pointer = 0x08F00300 -> offset 0x00F00300.
            uint addr = items[1].addr;
            vm.LoadEntry(addr);
            Assert.Equal(0x00F00300u, vm.Item2SkillPointer);
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    /// <summary>
    /// Stride-20 Item2 pointer round-trips through Write. Mutate Item2 pointer,
    /// call Write, assert raw u32 = offset | 0x08000000 AND p32(slot) = offset.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PersistsItem2Pointer_OnStride20()
    {
        ROM rom = MakeStride20FE8NVer2Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer2SkillViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);
            Assert.True(vm.HasItem2);

            uint addr = items[1].addr;
            vm.LoadEntry(addr);

            uint newItem2Off = 0x00F00800u;
            vm.Item2SkillPointer = newItem2Off;
            vm.Write();

            // Item2 slot is at addr + 16 in stride-20 layout. WF parity: raw
            // u32 = offset | 0x08000000, p32 = offset.
            Assert.Equal(newItem2Off | 0x08000000u, rom.u32(addr + 16));
            Assert.Equal(newItem2Off, rom.p32(addr + 16));
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesAllFields()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer2SkillViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            // Row 1 has all fields set in the synthetic ROM.
            uint addr = items[1].addr;
            vm.LoadEntry(addr);

            Assert.Equal(addr, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
            // Synthetic ROM puts textId=0x00AB, palette=0x0001 at row 1.
            Assert.Equal(0x00ABu, vm.TextDetail);
            Assert.Equal(0x0001u, vm.Palette);
            // The sub-pointer fields are stored as ROM OFFSETS (p32 strips
            // the GBA high bit). Synthetic ROM plants
            //   unit = 0x08F00000 -> offset 0x00F00000
            //   class = 0x08F00100 -> offset 0x00F00100
            //   item = 0x08F00200 -> offset 0x00F00200
            Assert.Equal(0x00F00000u, vm.UnitSkillPointer);
            Assert.Equal(0x00F00100u, vm.ClassSkillPointer);
            Assert.Equal(0x00F00200u, vm.ItemSkillPointer);
            // Animation pointer at row 1 = 0x08100000 -> offset 0x00100000.
            Assert.Equal(0x00100000u, vm.AnimationPointer);
            Assert.True(vm.IsAnimationValid,
                "Row 1 in synthetic ROM has a valid animation pointer");
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    [Fact]
    public void ViewModel_LoadEntry_AnimationValidFlagMirrorsWf()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer2SkillViewModel();
            var items = vm.LoadList();

            // Row 2 has a NULL animation pointer in the synthetic ROM.
            uint addr = items[2].addr;
            vm.LoadEntry(addr);

            Assert.Equal(0u, vm.AnimationPointer);
            Assert.False(vm.IsAnimationValid,
                "Row 2 with null animation pointer must report IsAnimationValid = false");
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    /// <summary>
    /// Write parity must cover textId + palette + 3 sub-pointers + animation
    /// pointer in one undo scope (the scope is owned by the View). This test
    /// mutates all six editable fields and asserts they round-trip through ROM.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PersistsAllFields()
    {
        ROM rom = MakeMinimalFE8NVer2Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer2SkillViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            uint addr = items[1].addr;
            uint id = items[1].tag;
            vm.LoadEntry(addr);

            // Mutate all six editable fields.
            uint newTextId = 0x00CDu;
            uint newPalette = 0x0002u;
            // Pointer fields are ROM OFFSETS — pick distinct values within
            // ROM bounds (16 MB).
            uint newUnitOff = 0x00F00400u;
            uint newClassOff = 0x00F00500u;
            uint newItemOff = 0x00F00600u;
            uint newAnimOff = 0x00400000u;

            vm.TextDetail = newTextId;
            vm.Palette = newPalette;
            vm.UnitSkillPointer = newUnitOff;
            vm.ClassSkillPointer = newClassOff;
            vm.ItemSkillPointer = newItemOff;
            vm.AnimationPointer = newAnimOff;

            vm.Write();

            // (a) text id + palette persist at addr+0 / addr+2.
            Assert.Equal(newTextId, (uint)rom.u16(addr + 0));
            Assert.Equal(newPalette, (uint)rom.u16(addr + 2));

            // (b) Sub-pointers persist at addr+4/+8/+12. WF parity contract:
            // editor value is a ROM OFFSET, serialized via write_p32 (OR with
            // 0x08000000) so:
            //   - Raw u32 at slot MUST be the GBA pointer form.
            //   - p32(slot) MUST return the original offset.
            Assert.Equal(newUnitOff | 0x08000000u, rom.u32(addr + 4));
            Assert.Equal(newUnitOff, rom.p32(addr + 4));
            Assert.Equal(newClassOff | 0x08000000u, rom.u32(addr + 8));
            Assert.Equal(newClassOff, rom.p32(addr + 8));
            Assert.Equal(newItemOff | 0x08000000u, rom.u32(addr + 12));
            Assert.Equal(newItemOff, rom.p32(addr + 12));

            // (c) Animation pointer at animeBase + 4*id.
            uint animeBase = PreviewIconHelper.FindSkillFE8NVer2AnimeBaseAddress();
            uint animeSlot = animeBase + 4 * id;
            uint rawReadBack = rom.u32(animeSlot);
            uint offsetReadBack = rom.p32(animeSlot);
            Assert.Equal(newAnimOff | 0x08000000u, rawReadBack);
            Assert.Equal(newAnimOff, offsetReadBack);
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    // -----------------------------------------------------------------
    // View - control surface assertions (Roslyn-static).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasReloadButton_Wired()
    {
        // #743: top bar migrated to EditorTopBarWithInputs — see
        // SkillConfigSkillSystemParityTests for the migration pattern.
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer2Skill_ReloadList_Button\"", axaml);
        Assert.Contains("ReloadRequested=\"OnTopBarReloadRequested\"", axaml);
    }

    [Fact]
    public void View_HasAnimationPanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"AnimationPanel\"", axaml);
    }

    [Fact]
    public void View_HasImageImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer2Skill_ImageImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasImageExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer2Skill_ImageExport_Button\"", axaml);
    }

    [Fact]
    public void View_HasAnimationImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer2Skill_AnimationImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasAnimationExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer2Skill_AnimationExport_Button\"", axaml);
    }

    [Fact]
    public void View_HasJumpToEditorButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer2Skill_JumpToEditor_Button\"", axaml);
    }

    /// <summary>
    /// #997: the List Expand button is intentionally DISABLED with an honest
    /// "not yet implemented" tooltip (functional table expansion is a documented
    /// follow-up — the skill-config tables are multi-table / multi-pointer per
    /// patch variant). Asserts the disabled state, the removed no-op Click
    /// handler, and that the stale #500 placeholder tooltip is gone.
    /// </summary>
    [Fact]
    public void View_ListExpandButton_IsDisabled()
    {
        string axaml = ReadAxaml();

        // Isolate the self-closing button element (`[^>]` spans newlines, so the
        // match captures the whole element regardless of attribute wrapping).
        var match = System.Text.RegularExpressions.Regex.Match(
            axaml, "<Button[^>]*SkillConfigFE8NVer2Skill_ListExpand_Button[^>]*?/>");
        Assert.True(match.Success, "List Expand button element not found in AXAML.");
        string button = match.Value;

        Assert.Contains("AutomationId=\"SkillConfigFE8NVer2Skill_ListExpand_Button\"", button);
        Assert.Contains("IsEnabled=\"False\"", button);
        Assert.DoesNotContain("Click=", button);

        // The stale #500 / "Pending Core extraction" placeholder must be gone from
        // the List Expand button element (other unrelated sibling buttons in this
        // view keep their own #500 placeholders — out of scope for #997).
        Assert.DoesNotContain("#500", button);
        Assert.DoesNotContain("Pending Core extraction", button);

        // The honest "not yet implemented" tooltip must live on the enabled wrapper.
        Assert.Contains("ToolTip.Tip=", axaml);
        Assert.Contains("List expansion is not yet implemented for the skill-config tables", axaml);
    }

    /// <summary>
    /// Write handler in the View must wrap ROM mutation in
    /// `_undoService.Begin/Commit/Rollback`. Roslyn-static read of the
    /// code-behind source - no Avalonia head needed.
    /// </summary>
    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillConfigFE8NVer2SkillView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillConfigFE8NVer2SkillView.axaml");
    }

    static string ReadAxaml() => File.ReadAllText(AxamlPath());

    static void EnsureSystemTextEncoder(ROM rom)
    {
        if (CoreState.SystemTextEncoder == null)
        {
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom);
        }
    }

    /// <summary>
    /// Build a synthetic FE8U ROM that mimics the FE8N v2 skill layout:
    ///   - 0x89268+4 holds a "good" iconExPointer (any safe GBA pointer).
    ///   - 0x70B96 (u16) = 0 so the size-detection branch runs.
    ///   - At 0xE10000 plant the byte pattern [0x50,0x93,0x08,0x08, 0x48,0x93,0x08,0x08]
    ///     so the WF grep finds it. The icon-pointer array starts at
    ///     `patternPos - 4*5 = 0xE10000 - 20 = 0xE0FFEC`. Each slot is a u32
    ///     GBA pointer; the WF iteration ACCEPTS slots whose dereferenced
    ///     offset is >= 0xE00000 (excludes API pointers) and only the 5th
    ///     accepted slot is `g_SkillBaseAddress`. The pattern bytes themselves
    ///     form slot[5] (0x08089350) and slot[6] (0x08089348), both decoded
    ///     to 0x00089350 / 0x00089348 — BELOW 0xE00000 — so they're correctly
    ///     skipped by the WF "pp < 0xE00000 continue" check.
    ///   - Plant 5+ accepted slots BEFORE the pattern (in slot[0..4]). slot[4]
    ///     is the skill base pointer; its value points to the skill table at
    ///     offset 0xE20000 (>= 0xE00000 so it passes the accept check). The
    ///     skill table at 0xE20000 stores the actual sizeof-16 row entries.
    ///   - slot[3] is "nazo_pointer": WF reads it, calls toOffset, then
    ///     appends it to `pointer`. After the iteration the appended value
    ///     ends up at `pointer[5]`, and WF calls p32 on that offset to get
    ///     the anime base. So slot[3]'s value should point to a u32 holding
    ///     a GBA pointer to the anime table. Use 0xE10100 (after pattern) as
    ///     that intermediate location, and 0xE30000 as the anime table base.
    ///   - slot[11] = ICON_LIST_SIZE = 16 (default sizeof-16).
    /// </summary>
    static ROM MakeMinimalFE8NVer2Rom()
    {
        var bytes = new byte[0x1000000];

        // -----------------------------------------------------------------
        // 0. Plant iconExPointer at 0x8926C. WF only needs isSafetyPointer
        //    (which requires a >= 0x08000200), so any GBA pointer beyond
        //    the 0x200 header floor works.
        // -----------------------------------------------------------------
        WriteU32(bytes, 0x8926C, 0x08001000u);

        // -----------------------------------------------------------------
        // 1. 0x70B96 (u16) = 0 so size-detect path runs.
        // -----------------------------------------------------------------
        WriteU16(bytes, 0x70B96, 0);

        // -----------------------------------------------------------------
        // 2. Plant the icon-pointer array.
        //
        //    iconPointersBase = patternPos - 20 = 0xE10000 - 20 = 0xE0FFEC.
        //
        //    The pattern bytes 50 93 08 08 48 93 08 08 occupy bytes
        //    0xE10000..0xE10007. These are slot[5] (0x08089350) and slot[6]
        //    (0x08089348) when read as little-endian u32 — their toOffset
        //    forms 0x00089350 / 0x00089348 are < 0xE00000, so the WF
        //    `pp < 0xE00000 continue` branch correctly skips both. They do
        //    NOT contribute to the `pointer` list.
        //
        //    So we need slot[0..4] (= bytes 0xE0FFEC..0xE0FFFF) to all hold
        //    valid GBA pointers whose toOffset >= 0xE00000. slot[4] = the
        //    skill base pointer.
        //
        //    Slot[3] (= bytes 0xE0FFF8..0xE0FFFB) holds the "nazo_pointer"
        //    in WF sizeof-16 path. WF does `pointer.Add(U.toOffset(nazo_pointer))`
        //    so slot[3]'s value's toOffset must be >= 0xE00000 (else the WF
        //    accept-loop's `continue` skips it BEFORE the nazo branch). But
        //    the nazo branch happens AFTER the accept-loop, so slot[3] is
        //    already in `pointer[3]` slot of the result array. WF code:
        //
        //        pointer.Add(U.toOffset(nazo_pointer));  // appended AT END
        //        if (pointer.Count > 5)
        //            g_AnimeBaseAddress = Program.ROM.p32(pointer[5]);
        //
        //    So we need at least 5 entries in `pointer` BEFORE the nazo
        //    append (slot[0..4] all accepted), so the appended entry ends
        //    up at index 5. That means we plant 5 valid slots (slot[0..4]).
        //
        //    Iteration STOPS on the first non-safe-pointer u32 encountered.
        //    Plant slot[5..7] as safe pointers too (the pattern bytes themselves
        //    decode to safe pointers — high bit 0x08 set). Then plant slot[8]
        //    onwards to terminate the loop OR fall through to find slot[11].
        //
        //    Actually, the iteration loop must REACH slot[11] (for size
        //    detection). So the loop must NOT terminate before slot[11].
        //    Plant slot[5..11] as safe pointers, then slot[12] as 0 (null
        //    terminator).
        // -----------------------------------------------------------------
        const uint iconPointersBase = 0xE0FFEC;

        const uint skillTableBase = 0x00E20000;
        const uint animeTableBase = 0x00E30000;

        // slot[0..4] all hold valid GBA pointers >= 0xE00000.
        WriteU32(bytes, iconPointersBase + 4 * 0, 0x08E00000u);
        WriteU32(bytes, iconPointersBase + 4 * 1, 0x08E00100u);
        WriteU32(bytes, iconPointersBase + 4 * 2, 0x08E00200u);
        WriteU32(bytes, iconPointersBase + 4 * 3, 0x08E00300u);
        // slot[4] = g_SkillBaseAddress — points to skill table at 0xE20000.
        WriteU32(bytes, iconPointersBase + 4 * 4, skillTableBase | 0x08000000u);
        // slot[5] and slot[6] are the pattern bytes — toOffset < 0xE00000 so
        // the WF accept loop skips them. (Pattern planted at iconPointersBase+20 below.)
        // slot[7+] = filler valid pointers so iteration reaches slot[11].
        WriteU32(bytes, iconPointersBase + 4 * 7, 0x08E00500u);
        // slot[8] = the anime pointer location for sizeof-16+ path. WF reads
        // p32 from `U.toOffset(slot[8] address)` = the offset of the slot[8]
        // location itself. That offset, when p32'd, must yield the anime
        // table base. So plant the anime-table GBA pointer AT slot[8].
        WriteU32(bytes, iconPointersBase + 4 * 8, animeTableBase | 0x08000000u);
        WriteU32(bytes, iconPointersBase + 4 * 9, 0x08E00700u);
        WriteU32(bytes, iconPointersBase + 4 * 10, 0x08E00800u);
        // slot[11] = ICON_LIST_SIZE = 16. Not a safe pointer, so the
        // iteration loop terminates when it hits this slot (which is fine
        // since WF reads slot[11] outside the iteration).
        WriteU32(bytes, iconPointersBase + 4 * 11, 16);

        // Plant the WF grep pattern at 0xE10000 (= iconPointersBase + 20).
        byte[] iconPattern = new byte[] { 0x50, 0x93, 0x08, 0x08, 0x48, 0x93, 0x08, 0x08 };
        Array.Copy(iconPattern, 0, bytes, (int)(iconPointersBase + 20), iconPattern.Length);

        // -----------------------------------------------------------------
        // 3. Plant the skill-info table at skillTableBase (0xE20000).
        //    sizeof-16 layout. Row 5 has u8=0xFF to terminate iteration.
        // -----------------------------------------------------------------
        WriteU16(bytes, skillTableBase + 0 * 16 + 0, 0x0001);
        WriteU16(bytes, skillTableBase + 0 * 16 + 2, 0x0000);

        WriteU16(bytes, skillTableBase + 1 * 16 + 0, 0x00AB);
        WriteU16(bytes, skillTableBase + 1 * 16 + 2, 0x0001);
        WriteU32(bytes, skillTableBase + 1 * 16 + 4, 0x00F00000u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * 16 + 8, 0x00F00100u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * 16 + 12, 0x00F00200u | 0x08000000u);

        WriteU16(bytes, skillTableBase + 2 * 16 + 0, 0x00CD);

        WriteU16(bytes, skillTableBase + 3 * 16 + 0, 0x00EF);
        WriteU16(bytes, skillTableBase + 4 * 16 + 0, 0x0101);

        // Row 5 starts with 0xFF -> WF iteration terminates here.
        bytes[(int)skillTableBase + 5 * 16] = 0xFF;

        // -----------------------------------------------------------------
        // 4. Plant the anime-pointer table at animeTableBase (0xE30000).
        // -----------------------------------------------------------------
        WriteU32(bytes, animeTableBase + 0 * 4, 0);
        WriteU32(bytes, animeTableBase + 1 * 4, 0x00100000u | 0x08000000u);
        WriteU32(bytes, animeTableBase + 2 * 4, 0);
        WriteU32(bytes, animeTableBase + 3 * 4, 0x00200000u | 0x08000000u);
        WriteU32(bytes, animeTableBase + 4 * 4, 0x00300000u | 0x08000000u);

        // Race-condition guard.
        bytes[0x6E0] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8nver2.gba", bytes, "BE8E01");
        return rom;
    }

    /// <summary>
    /// Variant of <see cref="MakeMinimalFE8NVer2Rom"/> that plants
    /// `ICON_LIST_SIZE = 20` at <c>iconPointers + 4*11</c>. The skill-info
    /// table at <c>0xE20000</c> uses a 20-byte stride: u16 textId @ +0,
    /// u16 palette @ +2, u32 unit-pointer @ +4, u32 class-pointer @ +8,
    /// u32 item-pointer @ +12, u32 item2-pointer @ +16. Row 1 plants a
    /// valid Item2 pointer (0x08F00300 -> offset 0x00F00300) so the
    /// stride-20 path can be exercised end-to-end.
    /// </summary>
    static ROM MakeStride20FE8NVer2Rom()
    {
        var bytes = new byte[0x1000000];

        // 0. iconExPointer.
        WriteU32(bytes, 0x8926C, 0x08001000u);

        // 1. 0x70B96 = 0 (size-detect path runs).
        WriteU16(bytes, 0x70B96, 0);

        // 2. Icon-pointer array.
        const uint iconPointersBase = 0xE0FFEC;
        const uint skillTableBase = 0x00E20000;
        const uint animeTableBase = 0x00E30000;
        const uint stride = 20;

        WriteU32(bytes, iconPointersBase + 4 * 0, 0x08E00000u);
        WriteU32(bytes, iconPointersBase + 4 * 1, 0x08E00100u);
        WriteU32(bytes, iconPointersBase + 4 * 2, 0x08E00200u);
        WriteU32(bytes, iconPointersBase + 4 * 3, 0x08E00300u);
        WriteU32(bytes, iconPointersBase + 4 * 4, skillTableBase | 0x08000000u);
        WriteU32(bytes, iconPointersBase + 4 * 7, 0x08E00500u);
        // slot[8] holds the anime base pointer (sizeof-20+ path).
        WriteU32(bytes, iconPointersBase + 4 * 8, animeTableBase | 0x08000000u);
        WriteU32(bytes, iconPointersBase + 4 * 9, 0x08E00700u);
        WriteU32(bytes, iconPointersBase + 4 * 10, 0x08E00800u);
        // slot[11] = ICON_LIST_SIZE = 20.
        WriteU32(bytes, iconPointersBase + 4 * 11, 20);

        // Plant the WF grep pattern.
        byte[] iconPattern = new byte[] { 0x50, 0x93, 0x08, 0x08, 0x48, 0x93, 0x08, 0x08 };
        Array.Copy(iconPattern, 0, bytes, (int)(iconPointersBase + 20), iconPattern.Length);

        // 3. Skill-info table — stride 20 bytes.
        WriteU16(bytes, skillTableBase + 0 * stride + 0, 0x0001);

        WriteU16(bytes, skillTableBase + 1 * stride + 0, 0x00AB);
        WriteU16(bytes, skillTableBase + 1 * stride + 2, 0x0001);
        WriteU32(bytes, skillTableBase + 1 * stride + 4, 0x00F00000u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * stride + 8, 0x00F00100u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * stride + 12, 0x00F00200u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * stride + 16, 0x00F00300u | 0x08000000u);

        WriteU16(bytes, skillTableBase + 2 * stride + 0, 0x00CD);
        WriteU16(bytes, skillTableBase + 3 * stride + 0, 0x00EF);
        WriteU16(bytes, skillTableBase + 4 * stride + 0, 0x0101);

        // Row 5 starts with 0xFF -> iteration terminates.
        bytes[(int)skillTableBase + 5 * stride] = 0xFF;

        // 4. Anime-pointer table.
        WriteU32(bytes, animeTableBase + 0 * 4, 0);
        WriteU32(bytes, animeTableBase + 1 * 4, 0x00100000u | 0x08000000u);
        WriteU32(bytes, animeTableBase + 2 * 4, 0);
        WriteU32(bytes, animeTableBase + 3 * 4, 0x00200000u | 0x08000000u);

        bytes[0x6E0] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8nver2-stride20.gba", bytes, "BE8E01");
        return rom;
    }

    static void WriteU32(byte[] bytes, uint offset, uint value)
    {
        int o = checked((int)offset);
        bytes[o] = (byte)(value & 0xFF);
        bytes[o + 1] = (byte)((value >> 8) & 0xFF);
        bytes[o + 2] = (byte)((value >> 16) & 0xFF);
        bytes[o + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void WriteU16(byte[] bytes, uint offset, ushort value)
    {
        int o = checked((int)offset);
        bytes[o] = (byte)(value & 0xFF);
        bytes[o + 1] = (byte)((value >> 8) & 0xFF);
    }

    /// <summary>
    /// Walk parent directories from the test bin/ folder until we find
    /// the repo root (identified by FEBuilderGBA.sln).
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
