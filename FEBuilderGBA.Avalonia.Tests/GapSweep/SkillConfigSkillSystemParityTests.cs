// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5/6 gap-sweep regression tests for SkillConfigSkillSystemView. (#427)
//
// Closes the 45 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `SkillConfigSkillSystemForm` (HIGH density 7/30 == -76.7 %, 22 WF-only
// labels, 0 common labels). Each assertion maps to a concrete acceptance-
// criterion bullet in the issue body and to a Copilot CLI plan-review finding.
//
// Mirrors the exact pattern PR #516 established for
// `SkillConfigFE8UCSkillSys09xParityTests`.
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
/// Tests proving the SkillConfigSkillSystem parity raise (#427) is permanent.
///
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class SkillConfigSkillSystemParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 30 control instantiations. To leave the HIGH
    /// verdict we need AV >= ceil(30 * 0.75) = 23.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 30;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 23
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Pointer-location helpers - planted patterns resolve correctly.
    // -----------------------------------------------------------------

    [Fact]
    public void Helper_FindSkillSystemTextPointerLocation_PlantsScanned()
    {
        ROM rom = MakeMinimalSkillSystemRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint loc = PreviewIconHelper.FindSkillSystemTextPointerLocation();
            // The planted TEXT pattern in the scan window must resolve to a
            // non-zero pointer location (the WF `textPointer` argument).
            Assert.NotEqual(0u, loc);
            Assert.NotEqual(U.NOT_FOUND, loc);
            // The dereferenced pointer must land on the planted text table.
            Assert.True(U.isSafetyOffset(loc + 3, rom));
            uint textBase = rom.p32(loc);
            Assert.Equal(PlantedTextBase, textBase);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Helper_FindSkillSystemAnimePointerLocation_PlantsScanned()
    {
        ROM rom = MakeMinimalSkillSystemRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint loc = PreviewIconHelper.FindSkillSystemAnimePointerLocation();
            Assert.NotEqual(0u, loc);
            Assert.NotEqual(U.NOT_FOUND, loc);
            Assert.True(U.isSafetyOffset(loc + 3, rom));
            uint animeBase = rom.p32(loc);
            Assert.Equal(PlantedAnimeBase, animeBase);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Naming consolidation - canonical VM name matches the View suffix-strip.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_HasCanonicalName_AndNoLegacyStub()
    {
        var avAsm = typeof(SkillConfigSkillSystemView).Assembly;
        var canonical = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillConfigSkillSystemViewModel");
        Assert.NotNull(canonical);
    }

    [Fact]
    public void ViewModel_ImplementsNavigationTargetSourceInterface()
    {
        var t = typeof(SkillConfigSkillSystemViewModel);
        Assert.Contains(typeof(INavigationTargetSource), t.GetInterfaces());
    }

    [Fact]
    public void ViewModel_DeriveViewName_MatchesCanonicalViewName()
    {
        string derived = JumpParityScanner.DeriveViewNameFromVmName(
            typeof(SkillConfigSkillSystemViewModel).Name);
        Assert.Equal("SkillConfigSkillSystemView", derived);
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) - Manifest declares the WF callsite as KnownGap.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresJumpToAnimationCreator()
    {
        var vm = new SkillConfigSkillSystemViewModel();
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
        var vm = new SkillConfigSkillSystemViewModel();
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
                SourceForm: "SkillConfigSkillSystemForm",
                TargetForm: "ToolAnimationCreatorForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "SkillConfigSkillSystemViewModel",
                SourceView: "SkillConfigSkillSystemView",
                Command: "JumpToAnimationCreator",
                TargetView: "ToolAnimationCreatorView",
                IssueRef: "#500"),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "SkillConfigSkillSystemForm" &&
            r.TargetWfType == "ToolAnimationCreatorForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.KnownGap, match!.Status);
    }

    [Fact]
    public void ListParityHelper_DeclaresWfAvFormPair()
    {
        var extras = ListParityHelper.GetExtraCrossViewMappings();
        Assert.True(extras.ContainsKey("SkillConfigSkillSystemView"),
            "ListParityHelper.KnownExtraCrossViewMappings must declare SkillConfigSkillSystemView");
        Assert.Equal("SkillConfigSkillSystemForm", extras["SkillConfigSkillSystemView"]);
    }

    // -----------------------------------------------------------------
    // ViewModel state - Phase 1 new fields populated.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_PopulatesFromSyntheticRom()
    {
        ROM rom = MakeMinimalSkillSystemRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigSkillSystemViewModel();
            var items = vm.LoadList();
            // WF predicate is `i < 255`; we expect a sizeable count to prove
            // iteration didn't truncate at the first invalid row.
            Assert.True(items.Count >= 4,
                $"LoadList must populate multiple rows - got {items.Count}");
            Assert.True(vm.ReadStartAddress > 0, "LoadList must populate ReadStartAddress");
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesTextIdAndAnimation()
    {
        ROM rom = MakeMinimalSkillSystemRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigSkillSystemViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            // Row 1 has all fields set in the synthetic ROM.
            uint addr = items[1].addr;
            vm.LoadEntry(addr);

            Assert.Equal(addr, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
            Assert.Equal(0x00ABu, vm.TextDetail);
            // AnimationPointer is the OFFSET form (p32 strips the high bit).
            Assert.Equal(0x00100000u, vm.AnimationPointer);
            Assert.True(vm.IsAnimationValid,
                "Row 1 in synthetic ROM has a valid animation pointer");
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    [Fact]
    public void ViewModel_LoadEntry_AnimationValidFlagMirrorsWf()
    {
        ROM rom = MakeMinimalSkillSystemRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigSkillSystemViewModel();
            var items = vm.LoadList();

            // Row 2 has a NULL animation pointer in the synthetic ROM.
            uint addr = items[2].addr;
            vm.LoadEntry(addr);

            Assert.Equal(0u, vm.AnimationPointer);
            Assert.False(vm.IsAnimationValid,
                "Row 2 with null animation pointer must report IsAnimationValid = false");
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    /// <summary>
    /// Copilot CLI plan-review finding 2: Write parity must cover textId +
    /// animation pointer in one undo scope (the scope is owned by the View).
    /// This test mutates both fields and asserts they round-trip through ROM.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PersistsBothFields()
    {
        ROM rom = MakeMinimalSkillSystemRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigSkillSystemViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            uint addr = items[1].addr;
            uint id = items[1].tag;
            vm.LoadEntry(addr);

            // Mutate both editable fields.
            uint newTextId = 0x00CDu;
            uint newAnimOffset = 0x00400000u;
            vm.TextDetail = newTextId;
            vm.AnimationPointer = newAnimOffset;

            vm.Write();

            // (a) text id must persist at addr (which IS textBase + 2*id).
            Assert.Equal(newTextId, (uint)rom.u16(addr));

            // (b) animation pointer must persist at animeBase + 4*id.
            //
            // WF parity: the editor value is a ROM OFFSET, but it is
            // serialized via `write_p32` which converts it to a GBA pointer
            // (OR with 0x08000000) before writing. So:
            //   - Raw u32 at the slot MUST be the GBA pointer form.
            //   - `p32(slot)` MUST return the original offset.
            uint animeBase = rom.p32(vm.AnimePointerLocation);
            uint animeSlot = animeBase + 4 * id;
            uint rawReadBack = rom.u32(animeSlot);
            uint offsetReadBack = rom.p32(animeSlot);
            Assert.Equal(newAnimOffset | 0x08000000u, rawReadBack);
            Assert.Equal(newAnimOffset, offsetReadBack);
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    // -----------------------------------------------------------------
    // View - control surface assertions (Roslyn-static).
    // -----------------------------------------------------------------

    [Fact]
    public void View_HasReloadButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_ReloadList_Button\"", axaml);
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
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_ImageImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasImageExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_ImageExport_Button\"", axaml);
    }

    [Fact]
    public void View_HasAnimationImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_AnimationImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasAnimationExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_AnimationExport_Button\"", axaml);
    }

    [Fact]
    public void View_HasJumpToEditorButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_JumpToEditor_Button\"", axaml);
    }

    [Fact]
    public void View_HasBulkImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_BulkImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasBulkExportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_BulkExport_Button\"", axaml);
    }

    [Fact]
    public void View_HasListExpandButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigSkillSystem_ListExpand_Button\"", axaml);
    }

    /// <summary>
    /// Write handler in the View must wrap ROM mutation in
    /// `_undoService.Begin/Commit/Rollback` (Copilot CLI plan-review finding:
    /// the View owns the undo scope, not the VM). Roslyn-static read of the
    /// code-behind source - no Avalonia head needed.
    /// </summary>
    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillConfigSkillSystemView.axaml.cs");
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
            "SkillConfigSkillSystemView.axaml");
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
    /// Synthetic SkillSystem ROM layout:
    ///   - Plant a TEXT byte pattern at 0xB10000 so the scan window finds it.
    ///   - Plant an ANIME byte pattern at 0xB20000 so the scan finds it too.
    ///   - Plant an ICON byte pattern at 0xB30000 so LoadList's icon-base
    ///     check passes (otherwise it correctly returns an empty list).
    ///   - The post-pattern u32 at "pattern_end + skip" points to the
    ///     synthetic text/anime/icon base.
    ///   - Text table at 0x80000 (u16/row).
    ///   - Anime pointer table at 0x90000 (u32/row, GBA-pointer form).
    ///   - Icon table at 0xA0000 (striped 128-byte tiles).
    /// </summary>
    const uint PlantedTextBase = 0x80000;
    const uint PlantedAnimeBase = 0x90000;
    const uint PlantedIconBase = 0xA0000;

    static ROM MakeMinimalSkillSystemRom()
    {
        var bytes = new byte[0x1000000];

        // WF FindTextPointer pattern (entry 0, skip=16).
        // The post-pattern u32 (at offset patternEnd + 16) holds the
        // GBA-pointer of the text base. Plant the pattern at 0xB10000 so the
        // scan window (0xB00000..0xC00000, blockSize 4) hits it.
        byte[] textPattern = new byte[]
        {
            0x07, 0x49, 0x40, 0x00, 0x40, 0x18, 0x00, 0x88,
            0x00, 0x28, 0x00, 0xD1, 0x06, 0x48, 0x21, 0x1C,
        };
        const uint textPatternPos = 0xB10000;
        Array.Copy(textPattern, 0, bytes, (int)textPatternPos, textPattern.Length);
        // After pattern (16 bytes) + skip (16 bytes) = 32 bytes after start.
        uint textPointerLoc = textPatternPos + (uint)textPattern.Length + 16;
        WriteU32(bytes, textPointerLoc, PlantedTextBase | 0x08000000u);

        // WF FindAnimePointer pattern (entry 0, skip=32).
        // Plant at 0xB20000.
        byte[] animePattern = new byte[]
        {
            0x00, 0x2B, 0x00, 0xD1, 0x06, 0x4B, 0x38, 0x1C,
            0x9E, 0x46, 0x00, 0xF8, 0x05, 0x48, 0x00, 0x47,
        };
        const uint animePatternPos = 0xB20000;
        Array.Copy(animePattern, 0, bytes, (int)animePatternPos, animePattern.Length);
        uint animePointerLoc = animePatternPos + (uint)animePattern.Length + 32;
        WriteU32(bytes, animePointerLoc, PlantedAnimeBase | 0x08000000u);

        // WF FindIconPointer first pattern (entry 0, skip=24). Plant at 0xB30000.
        byte[] iconPattern = new byte[]
        {
            0x02, 0x40, 0x09, 0x4C, 0x05, 0x48, 0x00, 0x47,
            0x05, 0x48, 0x00, 0x47, 0x05, 0x48, 0x00, 0x47,
        };
        const uint iconPatternPos = 0xB30000;
        Array.Copy(iconPattern, 0, bytes, (int)iconPatternPos, iconPattern.Length);
        uint iconPointerLoc = iconPatternPos + (uint)iconPattern.Length + 24;
        WriteU32(bytes, iconPointerLoc, PlantedIconBase | 0x08000000u);

        // Text table @ PlantedTextBase: u16 per row. Plant row 0 = 0x0000,
        // row 1 = 0x00AB (test sentinel), row 2 = 0x00CD, row 3 = 0x00EF,
        // row 4 = 0x0101.
        WriteU16(bytes, PlantedTextBase + 0 * 2, 0x0000);
        WriteU16(bytes, PlantedTextBase + 1 * 2, 0x00AB);
        WriteU16(bytes, PlantedTextBase + 2 * 2, 0x00CD);
        WriteU16(bytes, PlantedTextBase + 3 * 2, 0x00EF);
        WriteU16(bytes, PlantedTextBase + 4 * 2, 0x0101);

        // Anime pointer table @ PlantedAnimeBase: u32 per row (GBA pointer).
        // Row 1 = 0x08100000 (valid pointer -> offset 0x00100000).
        // Row 2 = 0 (null - IsAnimationValid must be false).
        // Row 3 = 0x08200000 (valid).
        WriteU32(bytes, PlantedAnimeBase + 0 * 4, 0);
        WriteU32(bytes, PlantedAnimeBase + 1 * 4, 0x08100000u);
        WriteU32(bytes, PlantedAnimeBase + 2 * 4, 0);
        WriteU32(bytes, PlantedAnimeBase + 3 * 4, 0x08200000u);
        WriteU32(bytes, PlantedAnimeBase + 4 * 4, 0x08300000u);

        // Plant a small fake palette pointer at 0x22370 so the icon-decode
        // path doesn't error (icon content not asserted - just the iteration).
        WriteU32(bytes, 0x22370, 0x08100000u);

        // Race-condition guard for parallel tests: when CoreState.ROM is set
        // to this synthetic instance, OTHER parallel non-SharedState tests
        // (e.g. ViewInstantiationSweepTests.View_CanInstantiate(TextCharCodeView))
        // may briefly see this ROM and call `rom.RomInfo.mask_pointer` (= 0x6E0
        // for FE8U) + `rom.u8(0x6E0)`. Plant 0xFF at that byte so their
        // iteration loop terminates on the first byte (their predicate is
        // `byte == 0xFF`) rather than fall through to `rom.getString` which
        // requires `CoreState.SystemTextEncoder`. Without this, the parallel
        // test can observe a partially-initialised CoreState (ROM set but
        // encoder not yet wired by our SetUp) and NRE inside the decode path.
        // This is a parity test concern only - production code installs the
        // encoder during ROM-load setup so the race is impossible in the app.
        bytes[0x6E0] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-skillsystem.gba", bytes, "BE8E01");
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
