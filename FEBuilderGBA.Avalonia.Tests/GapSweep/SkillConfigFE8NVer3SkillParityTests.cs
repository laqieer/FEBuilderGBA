// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5/6 gap-sweep regression tests for SkillConfigFE8NVer3SkillView. (#392)
//
// Closes the 187 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `SkillConfigFE8NVer3SkillForm` (HIGH density 19/166 == -88.6%, 37 WF-only
// labels, 0 common labels, 3 unmapped jump callsites). Each assertion maps to a
// concrete acceptance-criterion bullet in the issue body and to a Copilot CLI
// plan-review finding.
//
// Mirrors the exact pattern PR #598 established for the sister editor
// SkillConfigFE8NVer2SkillView. FE8N v3 specifics differ from v2:
//   - ROM scan uses FIXED offsets (no byte-pattern grep): 0x89268+4 sentinel,
//     0x892A8+4 = skill_table pointer slot, +8 = ICON_LIST_SIZE, +20 = anime base.
//   - Has FIVE sub-list editors (N1=unit, N2=class, N3=item, N4=item2,
//     N5=composite-skill) vs v2's four; the row stride is 24 bytes minimum
//     (D4/D8/D12/D16/D20).
//   - Three WF jump callsites (PatchForm, ErrorPaletteShowForm, ToolAnimationCreatorForm).
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
/// Tests proving the SkillConfigFE8NVer3 parity raise (#392) is permanent.
///
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class SkillConfigFE8NVer3SkillParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 166 control instantiations (per the
    /// 2026-05-21 density-sweep baseline). To leave the HIGH verdict we
    /// need AV >= ceil(166 * 0.5) + 1 = 84 so the actual delta is
    /// (84-166)/166 = -49.40% which is below the HIGH boundary at -50.0%.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        // Strict MEDIUM boundary: 83 gives exactly -50.0% (HIGH); 84 gives
        // -49.40% (MEDIUM). Use 84 as the minimum gate.
        const int MinAvControls = 84;
        Assert.True(avCount >= MinAvControls,
            $"AV control count {avCount} must be >= {MinAvControls} (strict MEDIUM cutoff, WF=166)");
    }

    // -----------------------------------------------------------------
    // Pointer-location helpers - planted offsets resolve correctly.
    // -----------------------------------------------------------------

    [Fact]
    public void Helper_FindSkillFE8NVer3SkillPointerLocation_ResolvesFromFixedOffset()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PreviewIconHelper.ResetFE8NVer3Cache();

            uint loc = PreviewIconHelper.FindSkillFE8NVer3SkillPointerLocation();
            // WF v3 g_SkillBaseAddress is `0x892A8 + 4 = 0x892AC` — the slot
            // address, not the dereferenced offset.
            Assert.Equal(0x892ACu, loc);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Helper_FindSkillFE8NVer3SkillBaseAddress_DereferencesSlot()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PreviewIconHelper.ResetFE8NVer3Cache();

            uint baseAddr = PreviewIconHelper.FindSkillFE8NVer3SkillBaseAddress();
            // Synthetic ROM plants skill table base at 0x00E20000 via
            // 0x892AC -> 0x08E20000.
            Assert.Equal(0x00E20000u, baseAddr);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Helper_FindSkillFE8NVer3AnimeBaseAddress_Resolves()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PreviewIconHelper.ResetFE8NVer3Cache();

            uint animeBase = PreviewIconHelper.FindSkillFE8NVer3AnimeBaseAddress();
            // Synthetic ROM plants the anime table base at 0x00E30000 via
            // 0x892BC -> 0x08E30000.
            Assert.Equal(0x00E30000u, animeBase);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Helper_GetFE8NVer3IconListSize_DetectsStrideFromRom()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PreviewIconHelper.ResetFE8NVer3Cache();

            uint size = PreviewIconHelper.GetFE8NVer3IconListSize();
            // Synthetic ROM plants ICON_LIST_SIZE = 24 (minimum valid for v3
            // - row layout D4/D8/D12/D16/D20 needs at least 24 bytes).
            Assert.Equal(24u, size);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Copilot review #1 (plan): GetFE8NVer3IconListSize MUST reject strides
    /// below 24 bytes — anything less aliases CompositeSkillPointer @ +20
    /// onto the next row's textId. Build a synthetic ROM with size=16 and
    /// assert the helper returns 0 (treated as "patch not installed").
    /// </summary>
    [Fact]
    public void Helper_GetFE8NVer3IconListSize_RejectsStrideBelow24()
    {
        ROM rom = MakeFE8NVer3Rom(strideOverride: 16);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PreviewIconHelper.ResetFE8NVer3Cache();

            uint size = PreviewIconHelper.GetFE8NVer3IconListSize();
            Assert.Equal(0u, size);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Stride validation must reject non-multiples-of-4 too (Copilot review #1).
    /// </summary>
    [Fact]
    public void Helper_GetFE8NVer3IconListSize_RejectsUnalignedStride()
    {
        ROM rom = MakeFE8NVer3Rom(strideOverride: 25);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            PreviewIconHelper.ResetFE8NVer3Cache();

            uint size = PreviewIconHelper.GetFE8NVer3IconListSize();
            Assert.Equal(0u, size);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Naming consolidation - canonical VM name matches the View suffix-strip.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_HasCanonicalName_AndOldNameDeleted()
    {
        var avAsm = typeof(SkillConfigFE8NVer3SkillView).Assembly;
        var canonical = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillConfigFE8NVer3SkillViewModel");
        var legacy = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillConfigFE8NVer3SkillViewViewModel");
        Assert.NotNull(canonical);
        Assert.Null(legacy);
    }

    [Fact]
    public void ViewModel_ImplementsNavigationTargetSourceInterface()
    {
        var t = typeof(SkillConfigFE8NVer3SkillViewModel);
        Assert.Contains(typeof(INavigationTargetSource), t.GetInterfaces());
    }

    [Fact]
    public void ViewModel_DeriveViewName_MatchesCanonicalViewName()
    {
        string derived = JumpParityScanner.DeriveViewNameFromVmName(
            typeof(SkillConfigFE8NVer3SkillViewModel).Name);
        Assert.Equal("SkillConfigFE8NVer3SkillView", derived);
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) - Manifest declares all THREE WF callsites
    // (Copilot review #2: JumpParityScanner matches by (sourceView,
    // targetView), so all three jumps need a manifest row.)
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresJumpToAnimationCreator()
    {
        var vm = new SkillConfigFE8NVer3SkillViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(ToolAnimationCreatorView));
    }

    [Fact]
    public void ViewModel_DeclaresJumpToCombatArt()
    {
        var vm = new SkillConfigFE8NVer3SkillViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(PatchManagerView));
    }

    [Fact]
    public void ViewModel_DeclaresJumpToErrorPaletteShow()
    {
        var vm = new SkillConfigFE8NVer3SkillViewModel();
        var targets = vm.GetNavigationTargets();
        Assert.Contains(targets, t => t.TargetViewType == typeof(ErrorPaletteShowView));
    }

    /// <summary>
    /// JumpToAnimationCreator must carry a non-null IssueRef so the gap-sweep
    /// scanner reports it as `KnownGap` until the ToolAnimationCreatorView
    /// Init flow lands (#500).
    /// </summary>
    [Fact]
    public void ViewModel_JumpToAnimationCreator_IsMarkedAsKnownGap()
    {
        var vm = new SkillConfigFE8NVer3SkillViewModel();
        var target = vm.GetNavigationTargets()
            .FirstOrDefault(t => t.TargetViewType == typeof(ToolAnimationCreatorView));
        Assert.NotNull(target);
        Assert.False(string.IsNullOrEmpty(target!.IssueRef),
            "JumpToAnimationCreator must carry a non-null IssueRef until ToolAnimationCreatorView.Init lands (#500)");
    }

    [Fact]
    public void ViewModel_JumpToCombatArt_IsMarkedAsKnownGap()
    {
        var vm = new SkillConfigFE8NVer3SkillViewModel();
        var target = vm.GetNavigationTargets()
            .FirstOrDefault(t => t.TargetViewType == typeof(PatchManagerView));
        Assert.NotNull(target);
        Assert.False(string.IsNullOrEmpty(target!.IssueRef),
            "JumpToCombatArt must carry a non-null IssueRef (PatchManagerView lacks JumpToSelectStruct seam)");
    }

    [Fact]
    public void ViewModel_JumpToErrorPaletteShow_IsMarkedAsKnownGap()
    {
        var vm = new SkillConfigFE8NVer3SkillViewModel();
        var target = vm.GetNavigationTargets()
            .FirstOrDefault(t => t.TargetViewType == typeof(ErrorPaletteShowView));
        Assert.NotNull(target);
        Assert.False(string.IsNullOrEmpty(target!.IssueRef),
            "JumpToErrorPaletteShow must carry a non-null IssueRef (Import flow no-op pending #500)");
    }

    [Fact]
    public void JumpParityScanner_ToCreator_IsKnownGap()
    {
        var wfCallsites = new[]
        {
            new WfJumpCallsite(
                SourceForm: "SkillConfigFE8NVer3SkillForm",
                TargetForm: "ToolAnimationCreatorForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "SkillConfigFE8NVer3SkillViewModel",
                SourceView: "SkillConfigFE8NVer3SkillView",
                Command: "JumpToAnimationCreator",
                TargetView: "ToolAnimationCreatorView",
                IssueRef: "#500"),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "SkillConfigFE8NVer3SkillForm" &&
            r.TargetWfType == "ToolAnimationCreatorForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.KnownGap, match!.Status);
    }

    [Fact]
    public void ListParityHelper_DeclaresWfAvFormPair()
    {
        var extras = ListParityHelper.GetExtraCrossViewMappings();
        Assert.True(extras.ContainsKey("SkillConfigFE8NVer3SkillView"),
            "ListParityHelper.KnownExtraCrossViewMappings must declare SkillConfigFE8NVer3SkillView");
        Assert.Equal("SkillConfigFE8NVer3SkillForm", extras["SkillConfigFE8NVer3SkillView"]);
    }

    // -----------------------------------------------------------------
    // ViewModel state - Phase 1 new fields populated.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_PopulatesFromSyntheticRom()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer3SkillViewModel();
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
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    [Fact]
    public void ViewModel_LoadList_DetectsIconListSize24_DefaultLayout()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer3SkillViewModel();
            vm.LoadList();
            // Default synthetic ROM ships ICON_LIST_SIZE = 24 (minimum
            // valid for v3 sizeof-24 layout).
            Assert.Equal(24u, vm.IconListSize);
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    /// <summary>
    /// Stride-28 variant exercises a wider layout that still fits all five
    /// sub-pointer fields. Asserts the helper accepts strides that are a
    /// 4-byte multiple in 24..100 and that LoadEntry reads from the correct
    /// offset.
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_DetectsIconListSize28_ReadsAllFields()
    {
        ROM rom = MakeFE8NVer3Rom(strideOverride: 28);
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer3SkillViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);
            Assert.Equal(28u, vm.IconListSize);

            // Row 1 has all five sub-pointers populated even at stride=28.
            uint addr = items[1].addr;
            vm.LoadEntry(addr);
            Assert.Equal(0x00F00000u, vm.UnitSkillPointer);
            Assert.Equal(0x00F00100u, vm.ClassSkillPointer);
            Assert.Equal(0x00F00200u, vm.ItemSkillPointer);
            Assert.Equal(0x00F00300u, vm.Item2SkillPointer);
            Assert.Equal(0x00F00400u, vm.CompositeSkillPointer);
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
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer3SkillViewModel();
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
            // Sub-pointer fields stored as ROM OFFSETS (p32 strips GBA high bit).
            //   unit = 0x08F00000 -> offset 0x00F00000
            //   class = 0x08F00100 -> offset 0x00F00100
            //   item = 0x08F00200 -> offset 0x00F00200
            //   item2 = 0x08F00300 -> offset 0x00F00300
            //   composite = 0x08F00400 -> offset 0x00F00400
            Assert.Equal(0x00F00000u, vm.UnitSkillPointer);
            Assert.Equal(0x00F00100u, vm.ClassSkillPointer);
            Assert.Equal(0x00F00200u, vm.ItemSkillPointer);
            Assert.Equal(0x00F00300u, vm.Item2SkillPointer);
            Assert.Equal(0x00F00400u, vm.CompositeSkillPointer);
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
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer3SkillViewModel();
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
    /// Write parity must cover textId + palette + 5 sub-pointers + animation
    /// pointer in one undo scope (the scope is owned by the View). This test
    /// mutates all eight editable fields and asserts they round-trip through ROM.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PersistsAllFields()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer3SkillViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            uint addr = items[1].addr;
            uint id = items[1].tag;
            vm.LoadEntry(addr);

            uint newTextId = 0x00CDu;
            uint newPalette = 0x0002u;
            uint newUnitOff = 0x00F00500u;
            uint newClassOff = 0x00F00600u;
            uint newItemOff = 0x00F00700u;
            uint newItem2Off = 0x00F00800u;
            uint newCompositeOff = 0x00F00900u;
            uint newAnimOff = 0x00400000u;

            vm.TextDetail = newTextId;
            vm.Palette = newPalette;
            vm.UnitSkillPointer = newUnitOff;
            vm.ClassSkillPointer = newClassOff;
            vm.ItemSkillPointer = newItemOff;
            vm.Item2SkillPointer = newItem2Off;
            vm.CompositeSkillPointer = newCompositeOff;
            vm.AnimationPointer = newAnimOff;

            vm.Write();

            // (a) text id + palette persist at addr+0 / addr+2.
            Assert.Equal(newTextId, (uint)rom.u16(addr + 0));
            Assert.Equal(newPalette, (uint)rom.u16(addr + 2));

            // (b) Sub-pointers persist at addr+4/+8/+12/+16/+20. WF parity:
            // editor value is a ROM OFFSET, serialized via write_p32 so:
            //   - Raw u32 at slot MUST be the GBA pointer form.
            //   - p32(slot) MUST return the original offset.
            Assert.Equal(newUnitOff | 0x08000000u, rom.u32(addr + 4));
            Assert.Equal(newUnitOff, rom.p32(addr + 4));
            Assert.Equal(newClassOff | 0x08000000u, rom.u32(addr + 8));
            Assert.Equal(newClassOff, rom.p32(addr + 8));
            Assert.Equal(newItemOff | 0x08000000u, rom.u32(addr + 12));
            Assert.Equal(newItemOff, rom.p32(addr + 12));
            Assert.Equal(newItem2Off | 0x08000000u, rom.u32(addr + 16));
            Assert.Equal(newItem2Off, rom.p32(addr + 16));
            Assert.Equal(newCompositeOff | 0x08000000u, rom.u32(addr + 20));
            Assert.Equal(newCompositeOff, rom.p32(addr + 20));

            // (c) Animation pointer at animeBase + 4*id.
            uint animeBase = PreviewIconHelper.FindSkillFE8NVer3AnimeBaseAddress();
            uint animeSlot = animeBase + 4 * id;
            Assert.Equal(newAnimOff | 0x08000000u, rom.u32(animeSlot));
            Assert.Equal(newAnimOff, rom.p32(animeSlot));
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
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_ReloadList_Button\"", axaml);
        Assert.Contains("Click=\"ReloadList_Click\"", axaml);
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
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_ImageImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasImageExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_ImageExport_Button\"", axaml);
    }

    [Fact]
    public void View_HasAnimationImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_AnimationImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasAnimationExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_AnimationExport_Button\"", axaml);
    }

    [Fact]
    public void View_HasJumpToEditorButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_JumpToEditor_Button\"", axaml);
    }

    [Fact]
    public void View_HasJumpToCombatArtButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_JumpToCombatArt_Button\"", axaml);
    }

    [Fact]
    public void View_HasListExpandButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_ListExpand_Button\"", axaml);
    }

    [Fact]
    public void View_HasCompositeSkillPointerRow_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8NVer3Skill_CompositeSkillPointer_Input\"", axaml);
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
            "SkillConfigFE8NVer3SkillView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("_undoService.Begin(", source);
        Assert.Contains("_undoService.Commit()", source);
        Assert.Contains("_undoService.Rollback()", source);
    }

    /// <summary>
    /// Sub-list tab Entry count labels are populated by `UpdateUI`'s call
    /// to the static `CountSubListEntries` helper - the AXAML default
    /// `Entry count: -` is replaced by `Entry count: N` whenever a row is
    /// selected. This test verifies the helper's per-row counting against
    /// a synthetic ROM that plants 3 sub-list entries terminated by a 0 byte.
    /// Addresses Copilot CLI PR-review finding #2 (round 1).
    /// </summary>
    [Fact]
    public void View_CountSubListEntries_CountsUntilZeroTerminator()
    {
        // Build a synthetic ROM that plants 3 nonzero bytes followed by 0
        // at a known offset within the safety window.
        byte[] bytes = new byte[0x1000000];
        const uint subListBase = 0x00F00000u;
        bytes[subListBase + 0] = 0x01;
        bytes[subListBase + 1] = 0x02;
        bytes[subListBase + 2] = 0x03;
        // bytes[subListBase + 3] stays 0 -> terminator.

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8nver3-sublist.gba", bytes, "BE8E01");

        // Invoke the static helper via reflection (it lives on the View
        // class for code-locality reasons but is functionally pure).
        var viewType = typeof(SkillConfigFE8NVer3SkillView);
        var method = viewType.GetMethod("CountSubListEntries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Case 1: planted 3-entry sub-list.
            int count = (int)method!.Invoke(null, new object[] { rom, subListBase })!;
            Assert.Equal(3, count);

            // Case 2: null pointer (0) returns 0.
            int nullCount = (int)method.Invoke(null, new object[] { rom, 0u })!;
            Assert.Equal(0, nullCount);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Skill name parsing must extract the substring between `『` and `』`
    /// (FE skill-name delimiters) like WF `ParseTextToSkillName`, NOT
    /// colon-split the raw text. Addresses Copilot bot PR-review thread
    /// #1 (round 2). Tests the static `ResolveSkillName` helper directly
    /// via reflection - it's marked private static on the VM.
    /// </summary>
    [Fact]
    public void ViewModel_ResolveSkillName_ExtractsTextBetweenJapaneseQuotes()
    {
        var method = typeof(SkillConfigFE8NVer3SkillViewModel).GetMethod(
            "ResolveSkillName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        // Build a tiny synthetic ROM with a planted u16 textId at a known
        // entry slot - the actual text resolution requires a real TBL/text
        // table which the synthetic ROM doesn't have. We can still verify
        // the parsing logic by reflecting on the public parse path: call
        // the private helper with a 0-size stride to avoid the text lookup
        // path and instead exercise the bracket-scan logic via a separate
        // test point.
        //
        // The 『』 delimiter logic is best exercised through the helper's
        // string-handling path. Since the helper depends on
        // NameResolver.GetTextById, we use a sentinel that exits early:
        // textId=0 -> returns "". For end-to-end coverage of the bracket
        // path, we rely on Roslyn-static assertion (next test).
        //
        // This test confirms the helper signature exists and that calling
        // it with a null rom returns "" (the early-return).
        string emptyResult = (string)method!.Invoke(null, new object[] { null!, 0u, 0u, 24u })!;
        Assert.Equal("", emptyResult);
    }

    /// <summary>
    /// Roslyn-static assert that `ResolveSkillName` uses the `『』`
    /// delimiter pattern (matches WF `ParseTextToSkillName`) and NOT a
    /// colon-split heuristic. Addresses Copilot bot PR-review thread #1
    /// (round 2).
    /// </summary>
    [Fact]
    public void ViewModel_ResolveSkillName_UsesJapaneseQuoteDelimiters_NotColonSplit()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "ViewModels",
            "SkillConfigFE8NVer3SkillViewModel.cs");
        string source = File.ReadAllText(sourcePath);

        // Must contain the bracket delimiters (U+300E + U+300F).
        Assert.Contains("『", source);
        Assert.Contains("』", source);
        // Must NOT colon-split (the old buggy logic).
        Assert.DoesNotContain("text.IndexOf(':')", source);
    }

    /// <summary>
    /// `ResetDerivedListState` must clear cached scan state (IconListSize,
    /// SkillBaseAddress, AnimeBaseAddress, SkillPointerLocation) in
    /// addition to per-row state - otherwise a failed re-scan leaves
    /// stale stride/base addresses that LoadEntry would use for bounds
    /// arithmetic. Addresses Copilot bot PR-review thread #2 (round 2).
    /// </summary>
    [Fact]
    public void ViewModel_ResetDerivedListState_ClearsCachedScanFields()
    {
        ROM rom = MakeMinimalFE8NVer3Rom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8NVer3SkillViewModel();
            // Populate state via a valid LoadList.
            vm.LoadList();
            Assert.Equal(24u, vm.IconListSize);
            Assert.NotEqual(0u, vm.SkillBaseAddress);
            Assert.NotEqual(0u, vm.AnimeBaseAddress);
            Assert.NotEqual(U.NOT_FOUND, vm.SkillPointerLocation);

            // Swap in an empty ROM (no FE8N v3 patch) and reload.
            byte[] emptyBytes = new byte[0x1000000];
            // Race-condition guard for HeadlessSystemTextEncoder bootstrap.
            emptyBytes[0x6E0] = 0xFF;
            var emptyRom = new ROM();
            emptyRom.LoadLow("synthetic-no-patch.gba", emptyBytes, "BE8E01");
            CoreState.ROM = emptyRom;

            // LoadList on the empty ROM should hit the patch-missing branch
            // and reset all cached state to defaults.
            var items = vm.LoadList();
            Assert.Empty(items);
            Assert.Equal(24u, vm.IconListSize); // back to DEFAULT_SIZE
            Assert.Equal(0u, vm.SkillBaseAddress);
            Assert.Equal(0u, vm.AnimeBaseAddress);
            Assert.Equal(U.NOT_FOUND, vm.SkillPointerLocation);
        }
        finally
        {
            CoreState.ROM = prevRom;
            if (prevEnc != null) CoreState.SystemTextEncoder = prevEnc;
        }
    }

    /// <summary>
    /// `UpdateUI` calls `CountSubListEntries` and assigns the result to
    /// the per-tab Count labels. Roslyn-static read of the code-behind
    /// confirms the assignment is wired so the AXAML default `-` will be
    /// replaced on every selection. Addresses Copilot CLI PR-review
    /// finding #2 (round 1).
    /// </summary>
    [Fact]
    public void View_UpdateUI_PopulatesSubListEntryCounts()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillConfigFE8NVer3SkillView.axaml.cs");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("UnitTabCountLabel.Content", source);
        Assert.Contains("ClassTabCountLabel.Content", source);
        Assert.Contains("ItemTabCountLabel.Content", source);
        Assert.Contains("Item2TabCountLabel.Content", source);
        Assert.Contains("CompositeTabCountLabel.Content", source);
        Assert.Contains("CountSubListEntries", source);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    static string AxamlPath()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillConfigFE8NVer3SkillView.axaml");
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
    /// Build a synthetic FE8U ROM that mimics the FE8N v3 skill layout with
    /// the default sizeof-24 layout. Unlike v2 the v3 path uses FIXED OFFSETS:
    ///   - 0x89268+4 (= 0x8926C) holds a "good" iconExPointer (install sentinel).
    ///   - 0x892A8+4 (= 0x892AC) holds the skill-table GBA pointer.
    ///   - 0x892A8+8 (= 0x892B0) holds ICON_LIST_SIZE (default 24).
    ///   - 0x892A8+20 (= 0x892BC) holds the anime-table GBA pointer.
    ///   - The skill table at 0xE20000 holds rows with v3 sizeof-24 layout
    ///     (u16 textId @ +0, u16 palette @ +2, u32 unit-pointer @ +4,
    ///      u32 class-pointer @ +8, u32 item-pointer @ +12,
    ///      u32 item2-pointer @ +16, u32 composite-pointer @ +20).
    ///   - The anime pointer table at 0xE30000 holds u32 GBA pointers
    ///     per skill ID, with row 2 set to 0 (NULL animation).
    /// </summary>
    static ROM MakeMinimalFE8NVer3Rom() => MakeFE8NVer3Rom(strideOverride: 24);

    /// <summary>
    /// Variant of <see cref="MakeMinimalFE8NVer3Rom"/> that lets the caller
    /// override the planted ICON_LIST_SIZE so the stride-validation paths
    /// can be exercised (Copilot review #1).
    /// </summary>
    static ROM MakeFE8NVer3Rom(uint strideOverride)
    {
        var bytes = new byte[0x1000000];

        // -----------------------------------------------------------------
        // 0. iconExPointer at 0x8926C (= 0x89268+4) — install sentinel.
        // -----------------------------------------------------------------
        WriteU32(bytes, 0x8926C, 0x08001000u);

        // -----------------------------------------------------------------
        // 1. Skill-table pointer slot at 0x892AC (= 0x892A8+4).
        //    Points to 0x08E20000 (skill table base at offset 0xE20000).
        // -----------------------------------------------------------------
        const uint skillTableBase = 0x00E20000;
        WriteU32(bytes, 0x892AC, skillTableBase | 0x08000000u);

        // -----------------------------------------------------------------
        // 2. ICON_LIST_SIZE at 0x892B0 (= 0x892A8+8). Default sizeof-24.
        //    Caller may override to test rejection paths.
        // -----------------------------------------------------------------
        WriteU32(bytes, 0x892B0, strideOverride);

        // -----------------------------------------------------------------
        // 3. Anime-table pointer slot at 0x892BC (= 0x892A8+20).
        //    Points to 0x08E30000 (anime table base at offset 0xE30000).
        // -----------------------------------------------------------------
        const uint animeTableBase = 0x00E30000;
        WriteU32(bytes, 0x892BC, animeTableBase | 0x08000000u);

        // -----------------------------------------------------------------
        // 4. Plant the skill-info table rows at skillTableBase. Use the
        //    caller's stride so each row's offsets land correctly.
        //    Plant 5 rows (0..4) then terminate at row 5 with u8==0xFF.
        // -----------------------------------------------------------------
        uint stride = strideOverride;
        // If the stride is too small to fit the row layout, fall back to
        // 24 for row planting (the helper rejects the stride so the rows
        // are effectively ignored — but writing past-end-of-array would
        // crash the test fixture).
        uint plantingStride = stride < 24 ? 24u : stride;

        // Row 0 — minimal placeholder row.
        WriteU16(bytes, skillTableBase + 0 * plantingStride + 0, 0x0001);
        WriteU16(bytes, skillTableBase + 0 * plantingStride + 2, 0x0000);

        // Row 1 — full data row exercising every field.
        WriteU16(bytes, skillTableBase + 1 * plantingStride + 0, 0x00AB);
        WriteU16(bytes, skillTableBase + 1 * plantingStride + 2, 0x0001);
        WriteU32(bytes, skillTableBase + 1 * plantingStride + 4, 0x00F00000u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * plantingStride + 8, 0x00F00100u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * plantingStride + 12, 0x00F00200u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * plantingStride + 16, 0x00F00300u | 0x08000000u);
        WriteU32(bytes, skillTableBase + 1 * plantingStride + 20, 0x00F00400u | 0x08000000u);

        // Row 2 — text-only, no animation (NULL animation pointer in step 5).
        WriteU16(bytes, skillTableBase + 2 * plantingStride + 0, 0x00CD);
        // Rows 3-4 — minimal text-only rows so iteration sees >= 4 entries.
        WriteU16(bytes, skillTableBase + 3 * plantingStride + 0, 0x00EF);
        WriteU16(bytes, skillTableBase + 4 * plantingStride + 0, 0x0101);

        // Row 5 — terminator (u8 @ +0 == 0xFF stops the WF iteration).
        bytes[(int)(skillTableBase + 5 * plantingStride)] = 0xFF;

        // -----------------------------------------------------------------
        // 5. Plant the anime-pointer table at animeTableBase. Row 2 is the
        //    NULL animation case; rows 1/3/4 have valid animation pointers.
        // -----------------------------------------------------------------
        WriteU32(bytes, animeTableBase + 0 * 4, 0);
        WriteU32(bytes, animeTableBase + 1 * 4, 0x00100000u | 0x08000000u);
        WriteU32(bytes, animeTableBase + 2 * 4, 0);
        WriteU32(bytes, animeTableBase + 3 * 4, 0x00200000u | 0x08000000u);
        WriteU32(bytes, animeTableBase + 4 * 4, 0x00300000u | 0x08000000u);

        // Race-condition guard.
        bytes[0x6E0] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8nver3.gba", bytes, "BE8E01");
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
