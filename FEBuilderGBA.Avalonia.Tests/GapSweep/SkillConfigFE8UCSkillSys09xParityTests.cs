// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1/2/4/5/6 gap-sweep regression tests for SkillConfigFE8UCSkillSys09xView. (#430)
//
// Closes the 40 Avalonia <-> WinForms gaps the gap-sweep methodology surfaced
// on `SkillConfigCSkillSystem09xForm` (HIGH density 9/29, 20 WF-only labels,
// 0 common labels). Each assertion maps to a concrete acceptance-criterion
// bullet in the issue body and to a Copilot CLI plan-review finding.
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
/// Tests proving the SkillConfigFE8UCSkillSys09x parity raise (#430) is permanent.
///
/// Marked [Collection("SharedState")] because the synthetic-ROM tests mutate
/// CoreState.ROM. Without serialization, xUnit's per-class parallel runner can
/// race a sibling test's ROM swap between LoadList / LoadEntry calls.
/// </summary>
[Collection("SharedState")]
public class SkillConfigFE8UCSkillSys09xParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) - AV control count must reach the MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// WF designer.cs reports 29 control instantiations. To leave the HIGH
    /// verdict we need AV >= ceil(29 * 0.75) = 22.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string axamlPath = AxamlPath();
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 29;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 22
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be >= {mediumThreshold} (75% of WF={WfControlCount})");
    }

    // -----------------------------------------------------------------
    // Naming consolidation - the canonical VM name is the one paired with the View.
    // The unused stub `SkillConfigFE8UCSkillSys09xViewViewModel` is gone.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_HasCanonicalName_AndOldNameDeleted()
    {
        var avAsm = typeof(SkillConfigFE8UCSkillSys09xView).Assembly;
        var canonical = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillConfigFE8UCSkillSys09xViewModel");
        var legacy = avAsm.GetType("FEBuilderGBA.Avalonia.ViewModels.SkillConfigFE8UCSkillSys09xViewViewModel");
        Assert.NotNull(canonical);
        Assert.Null(legacy);
    }

    /// <summary>
    /// The canonical VM implements INavigationTargetSource so JumpParityScanner
    /// picks up the manifest. Suffix-strip of "ViewModel" produces "View" which
    /// matches the real Avalonia view name.
    /// </summary>
    [Fact]
    public void ViewModel_ImplementsNavigationTargetSourceInterface()
    {
        var t = typeof(SkillConfigFE8UCSkillSys09xViewModel);
        Assert.Contains(typeof(INavigationTargetSource), t.GetInterfaces());
    }

    [Fact]
    public void ViewModel_DeriveViewName_MatchesCanonicalViewName()
    {
        string derived = JumpParityScanner.DeriveViewNameFromVmName(
            typeof(SkillConfigFE8UCSkillSys09xViewModel).Name);
        Assert.Equal("SkillConfigFE8UCSkillSys09xView", derived);
    }

    // -----------------------------------------------------------------
    // Jumps (Phase 4) - Manifest declares the WF callsite as KnownGap.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_DeclaresJumpToAnimationCreator()
    {
        var vm = new SkillConfigFE8UCSkillSys09xViewModel();
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
        var vm = new SkillConfigFE8UCSkillSys09xViewModel();
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
                SourceForm: "SkillConfigCSkillSystem09xForm",
                TargetForm: "ToolAnimationCreatorForm",
                HasAddressArgument: false),
        };

        var avManifests = new[]
        {
            new AvManifestEntry(
                SourceVm: "SkillConfigFE8UCSkillSys09xViewModel",
                SourceView: "SkillConfigFE8UCSkillSys09xView",
                Command: "JumpToAnimationCreator",
                TargetView: "ToolAnimationCreatorView",
                IssueRef: "#500"),
        };

        var rows = JumpParityScanner.ComputeJumpRows(wfCallsites, avManifests);
        var match = rows.FirstOrDefault(r =>
            r.SourceForm == "SkillConfigCSkillSystem09xForm" &&
            r.TargetWfType == "ToolAnimationCreatorForm");
        Assert.NotNull(match);
        Assert.Equal(JumpRowStatus.KnownGap, match!.Status);
    }

    // -----------------------------------------------------------------
    // ViewModel state - Phase 1 new fields populated.
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadList_PopulatesFromSyntheticRom()
    {
        ROM rom = MakeMinimalFe8uCSkillSysRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8UCSkillSys09xViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);
            // We expect at least many rows (WF predicate is i < 0x400; we'll
            // accept any sizeable count as proof iteration didn't truncate).
            Assert.True(items.Count >= 4,
                $"LoadList must populate multiple rows from the synthetic table - got {items.Count}");
            Assert.True(vm.ReadStartAddress > 0, "LoadList must populate ReadStartAddress");
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    /// <summary>
    /// Copilot CLI plan-review finding 5: LoadList must NOT terminate on the
    /// first invalid row. The WF predicate is `i &lt; 0x400`. Our synthetic ROM
    /// plants a row with a NULL icon pointer at index 2 (which is fine - WF
    /// keeps iterating); LoadList must return rows past it.
    /// </summary>
    [Fact]
    public void ViewModel_LoadList_DoesNotEarlyExitOnInvalidMidRow()
    {
        ROM rom = MakeMinimalFe8uCSkillSysRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8UCSkillSys09xViewModel();
            var items = vm.LoadList();
            // Synthetic ROM populates 5 distinct rows + 0x3FB "blank" rows
            // after; LoadList must return all 0x400 rows (the WF predicate).
            // Be tolerant to floor at the count actually planted plus blanks,
            // but assert > 4 to prove we didn't early-exit at row 2 (null).
            Assert.True(items.Count > 4,
                $"LoadList must iterate past invalid mid-table rows - got {items.Count}");
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    [Fact]
    public void ViewModel_LoadEntry_PopulatesAllFields()
    {
        ROM rom = MakeMinimalFe8uCSkillSysRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8UCSkillSys09xViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            // Row 1 has all 3 fields set in the synthetic ROM.
            uint addr = items[1].addr;
            vm.LoadEntry(addr);

            Assert.Equal(addr, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
            // Synthetic ROM puts SkillNameMsg=0x0042, DescriptionMsg=0x0043,
            // IconAddr=0x08123456 (GBA-style pointer) at row 1.
            Assert.Equal(0x0042u, vm.SkillNameMsg);
            Assert.Equal(0x0043u, vm.DescriptionMsg);
            // IconAddr is the raw u32 read from entry+0 (GBA-style pointer).
            Assert.Equal(0x08123456u, vm.IconAddr);
        }
        finally { CoreState.ROM = prevRom; CoreState.SystemTextEncoder = prevEnc; }
    }

    /// <summary>
    /// Copilot CLI plan-review finding 2: Write parity must cover W4 + W6 +
    /// animation pointer in one undo scope. This test mutates all three fields
    /// and asserts they round-trip through the ROM after Write.
    /// </summary>
    [Fact]
    public void ViewModel_Write_PersistsAllThreeFields()
    {
        ROM rom = MakeMinimalFe8uCSkillSysRom();
        var prevRom = CoreState.ROM;
        var prevEnc = CoreState.SystemTextEncoder;
        try
        {
            CoreState.ROM = rom;
            EnsureSystemTextEncoder(rom);
            var vm = new SkillConfigFE8UCSkillSys09xViewModel();
            var items = vm.LoadList();
            Assert.NotEmpty(items);

            uint addr = items[1].addr;
            vm.LoadEntry(addr);

            // Mutate all three editable fields.
            vm.SkillNameMsg = 0x00AAu;
            vm.DescriptionMsg = 0x00BBu;
            // AnimationPointer is a ROM OFFSET (not a raw GBA pointer).
            // Pick a value INSIDE the synthetic ROM bounds (16 MB =
            // 0x01000000) but distinct from the value LoadEntry read so
            // the round-trip is observable. 0x00400000 is a clean offset
            // that round-trips cleanly through `write_p32`/`p32`.
            uint newAnimOffset = 0x00400000u;
            vm.AnimationPointer = newAnimOffset;

            vm.Write();

            // (a) W4 / W6 must persist at addr+4 / addr+6.
            Assert.Equal(0x00AAu, (uint)rom.u16(addr + 4));
            Assert.Equal(0x00BBu, (uint)rom.u16(addr + 6));

            // (b) Animation pointer must persist at gpEfxSkillAnims + 4 * id.
            //
            // WF parity: the editor value is a ROM OFFSET, but it is
            // serialized via `write_p32` which converts it to a GBA pointer
            // (OR with 0x08000000) before writing. So:
            //   - Raw u32 at the slot MUST be the GBA pointer form.
            //   - `p32(slot)` MUST return the original offset.
            const uint gpEfxSkillAnims = 0xB2A630;
            uint animBase = rom.p32(gpEfxSkillAnims);
            uint animSlot = animBase + 4 * items[1].tag;
            uint rawReadBack = rom.u32(animSlot);
            uint offsetReadBack = rom.p32(animSlot);
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
        Assert.Contains("AutomationId=\"SkillConfigFE8UCSkillSys09x_ReloadList_Button\"", axaml);
        Assert.Contains("Click=\"ReloadList_Click\"", axaml);
    }

    [Fact]
    public void View_HasAnimationPanel()
    {
        string axaml = ReadAxaml();
        Assert.Contains("Name=\"AnimationPanel\"", axaml);
    }

    [Fact]
    public void View_HasJumpToEditorButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8UCSkillSys09x_JumpToEditor_Button\"", axaml);
    }

    [Fact]
    public void View_HasImageImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8UCSkillSys09x_ImageImport_Button\"", axaml);
    }

    [Fact]
    public void View_HasAnimationImportButton_Wired()
    {
        string axaml = ReadAxaml();
        Assert.Contains("AutomationId=\"SkillConfigFE8UCSkillSys09x_AnimationImport_Button\"", axaml);
    }

    /// <summary>
    /// Write handler must wrap ROM mutation in `_undoService.Begin/Commit`.
    /// Roslyn-static read of the code-behind source - no Avalonia head needed.
    /// </summary>
    [Fact]
    public void View_WriteHandler_WrapsInUndoScope()
    {
        string repoRoot = FindRepoRoot();
        string sourcePath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "SkillConfigFE8UCSkillSys09xView.axaml.cs");
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
            "SkillConfigFE8UCSkillSys09xView.axaml");
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
    /// Build a tiny synthetic FE8U ROM that mimics the CSkillSys 0.9.x layout:
    ///   - 0xB2A614 = skill-info table pointer (gpSkillInfos)
    ///   - 0xB2A630 = animation-pointer table pointer (gpEfxSkillAnims)
    ///   - 0x22370  = skill palette pointer
    /// Populate row 0 (reserved), row 1 (icon=0x08123456, name=0x42, desc=0x43,
    /// anim=0x08100000), row 2 (icon=0 = null - this is the "mid-row invalid"
    /// case that LoadList must NOT early-exit on), and rows 3/4 with valid icons.
    /// </summary>
    static ROM MakeMinimalFe8uCSkillSysRom()
    {
        var bytes = new byte[0x1000000];

        // Palette pointer: ROM[0x22370] -> 0x08100000 (some valid palette area).
        // We just need it to dereference to a safe offset; palette content
        // isn't asserted by the tests below.
        WriteU32(bytes, 0x22370, 0x08100000u);

        // Skill info table base - plant a GBA pointer to 0x00800000 at 0xB2A614.
        const uint skillInfoBase = 0x00800000u;
        WriteU32(bytes, 0xB2A614, skillInfoBase | 0x08000000u);

        // Animation pointer table base - plant a GBA pointer to 0x00900000 at 0xB2A630.
        const uint animPtrTableBase = 0x00900000u;
        WriteU32(bytes, 0xB2A630, animPtrTableBase | 0x08000000u);

        // Row 0: reserved (zero entry).
        // Row 1: icon=0x08123456, nameMsg=0x42, descMsg=0x43.
        WriteU32(bytes, skillInfoBase + 1 * 8 + 0, 0x08123456u);
        WriteU16(bytes, skillInfoBase + 1 * 8 + 4, 0x0042);
        WriteU16(bytes, skillInfoBase + 1 * 8 + 6, 0x0043);
        // Row 2: icon=0 (null - simulate broken/invalid mid-row).
        WriteU32(bytes, skillInfoBase + 2 * 8 + 0, 0);
        WriteU16(bytes, skillInfoBase + 2 * 8 + 4, 0);
        WriteU16(bytes, skillInfoBase + 2 * 8 + 6, 0);
        // Row 3: icon=0x08234567, nameMsg=0x50, descMsg=0x51.
        WriteU32(bytes, skillInfoBase + 3 * 8 + 0, 0x08234567u);
        WriteU16(bytes, skillInfoBase + 3 * 8 + 4, 0x0050);
        WriteU16(bytes, skillInfoBase + 3 * 8 + 6, 0x0051);
        // Row 4: icon=0x08345678, nameMsg=0x60, descMsg=0x61.
        WriteU32(bytes, skillInfoBase + 4 * 8 + 0, 0x08345678u);
        WriteU16(bytes, skillInfoBase + 4 * 8 + 4, 0x0060);
        WriteU16(bytes, skillInfoBase + 4 * 8 + 6, 0x0061);

        // Animation pointer table: 4 bytes per slot. Plant a valid GBA pointer
        // at slot 1 so the IsAnimationValid branch is exercised.
        WriteU32(bytes, animPtrTableBase + 1 * 4, 0x08100000u);

        var rom = new ROM();
        rom.LoadLow("synthetic-cskillsys.gba", bytes, "BE8E01");
        return rom;
    }

    static void WriteU32(byte[] bytes, uint offset, uint value)
    {
        // Take an int offset internally - C# array indexers want Int32, and
        // forcing the cast at the call site documents the intent (Copilot
        // bot review on PR #516). Range was already validated by the caller.
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
