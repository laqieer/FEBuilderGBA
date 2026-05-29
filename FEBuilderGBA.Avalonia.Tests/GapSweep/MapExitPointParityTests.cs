// SPDX-License-Identifier: GPL-3.0-or-later
// Phase 1+2 gap-sweep regression tests for MapExitPointView. (#425)
//
// Covers the gaps the issue called out: 21 WF-only labels (density: 32→6,
// -81.3%). After this PR the view rebuilds to a three-pane master-detail
// editor (Map list / per-map exit-point sub-list / detail panel) with
// Filter combo (Enemy / NPC), notice panel, and write/alloc affordances.
//
// NOTE: this issue is PURE density+labels (Phase 1+2). Unlike #441/#442/etc,
// `MapExitPointForm.cs` has ZERO `InputFormRef.JumpForm<T>(…)` callsites
// (verified with grep — Copilot v1 review #2). So there is no
// INavigationTargetSource manifest work; jump-parity assertions are
// intentionally omitted.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.GapSweep;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

/// <summary>
/// Tests proving the MapExitPoint parity raise (#425) is permanent.
/// Density target: AV ≥ ceil(WF * 0.75) = 24.
///
/// Marked [Collection("SharedState")] because the VM tests mutate
/// CoreState.ROM to plant a synthetic FE8U ROM.
/// </summary>
[Collection("SharedState")]
public class MapExitPointParityTests
{
    // -----------------------------------------------------------------
    // Density (Phase 1) — AV control count must reach MEDIUM verdict.
    // -----------------------------------------------------------------

    /// <summary>
    /// The WF Designer reports 32 controls; the MEDIUM threshold is
    /// `ceil(32 * 0.75) = 24`. Falling below this re-enters HIGH territory.
    /// </summary>
    [Fact]
    public void View_AvControlCount_AtOrAboveMediumVerdict()
    {
        string repoRoot = FindRepoRoot();
        string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
            "MapExitPointView.axaml");
        Assert.True(File.Exists(axamlPath), $"AXAML not found at {axamlPath}");

        var doc = XDocument.Load(axamlPath);
        int avCount = ControlDensityScanner.CountAvControlsInDocument(doc);

        const int WfControlCount = 32;
        int mediumThreshold = (int)Math.Ceiling(WfControlCount * 0.75); // 24
        Assert.True(avCount >= mediumThreshold,
            $"AV control count {avCount} must be ≥ {mediumThreshold} (75% of WF={WfControlCount}) — got HIGH verdict");
    }

    // -----------------------------------------------------------------
    // ViewModel behavior — filter switching + sub-list walk
    // -----------------------------------------------------------------

    [Fact]
    public void ViewModel_LoadMapList_FilterEnemy_ReturnsAtLeastOneEntry()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapExitPointViewModel();
            var list = vm.LoadMapList(filterIndex: 0);
            Assert.NotEmpty(list);
            // FilterIndex must be updated.
            Assert.Equal(0u, vm.FilterIndex);
            // ReadStartAddress must equal the dereferenced map_exit_point_pointer.
            Assert.Equal(rom.p32(rom.RomInfo.map_exit_point_pointer), vm.ReadStartAddress);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadMapList_FilterNpc_UsesNpcOffset()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapExitPointViewModel();
            vm.LoadMapList(filterIndex: 1);
            uint expected = rom.p32(rom.RomInfo.map_exit_point_pointer) +
                            4u * rom.RomInfo.map_exit_point_npc_blockadd;
            Assert.Equal(expected, vm.ReadStartAddress);
            Assert.Equal(1u, vm.FilterIndex);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadExitListForMap_DereferencesPointerSlot()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new MapExitPointViewModel();
            vm.LoadMapList(filterIndex: 0);
            // Synthetic ROM plants per-map pointer at slot 0 → 0x00810000
            // which contains 2 rows + terminator.
            uint slotAddr = rom.p32(rom.RomInfo.map_exit_point_pointer);
            var rows = vm.LoadExitListForMap(slotAddr);
            Assert.NotEmpty(rows);
            Assert.False(vm.IsBlank);
            Assert.True(vm.IsAllocated);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadExitListForMap_BlankPointer_SetsIsBlankTrue()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Plant a slot that points to the blank marker.
            uint slot = 0x00802000u;
            uint blank = rom.RomInfo.map_exit_point_blank;
            BitConverter.GetBytes(blank | 0x08000000u).CopyTo(rom.Data, slot);

            var vm = new MapExitPointViewModel();
            var rows = vm.LoadExitListForMap(slot);
            Assert.Empty(rows);
            Assert.True(vm.IsBlank);
            Assert.False(vm.IsAllocated);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_LoadExitPointEntry_ReadsXYEscapeFlag()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // Per-map block of map 0 sits at 0x00810000; first row = (0x10, 0x20, 0x03, 0x00).
            uint rowAddr = 0x00810000u;

            var vm = new MapExitPointViewModel();
            vm.LoadExitPointEntry(rowAddr);

            Assert.Equal(0x10u, vm.ExitX);
            Assert.Equal(0x20u, vm.ExitY);
            Assert.Equal(0x03u, vm.EscapeMethod);
            Assert.Equal(0x00u, vm.FlagId);
            Assert.True(vm.CanWrite);
            Assert.Equal(4u, vm.BlockSize);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ViewModel_NewAlloc_ReturnsValidAddress_AndRepointsSlot()
    {
        ROM rom = MakeMinimalFe8uRom();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();

            var vm = new MapExitPointViewModel();
            vm.LoadMapList(filterIndex: 0);
            uint slot = rom.p32(rom.RomInfo.map_exit_point_pointer);
            vm.LoadExitListForMap(slot);

            var undodata = CoreState.Undo.NewUndoData("MapExit NewAlloc test");
            uint newaddr;
            using (ROM.BeginUndoScope(undodata))
            {
                newaddr = vm.NewAlloc(undodata);
            }
            Assert.NotEqual(U.NOT_FOUND, newaddr);
            Assert.Equal(newaddr, rom.p32(slot));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // Roslyn AST walk — assert the View handlers use _undoService
    // -----------------------------------------------------------------

    [Fact]
    public void View_WriteHandler_UsesUndoService()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "Views", "MapExitPointView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        // The Write_Click handler must wrap _vm.WriteExitPoint() in
        // _undoService.Begin / Commit, with Rollback in the catch.
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+Write_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_WritePointerHandler_UsesUndoService()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "Views", "MapExitPointView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(new Regex(@"void\s+WritePointer_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+WritePointer_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+WritePointer_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_NewAllocHandler_UsesUndoService_AndPassesActiveUndoData()
    {
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "Views", "MapExitPointView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(new Regex(@"void\s+NewAlloc_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+NewAlloc_Click[\s\S]*?_undoService\.GetActiveUndoData", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+NewAlloc_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+NewAlloc_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    [Fact]
    public void View_ExpandListHandler_UsesUndoService_AndCallsExpandExitList()
    {
        // #773: the ExpandList_Click handler must wrap _vm.ExpandExitList(...)
        // in an _undoService.Begin/Commit scope (Rollback on failure/exception),
        // mirroring the NewAlloc handler. Guards the gap-fix against regressing
        // back to the old inert "deferred" stub.
        string repoRoot = FindRepoRoot();
        string codeBehindPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia",
            "Views", "MapExitPointView.axaml.cs");
        string source = File.ReadAllText(codeBehindPath);

        Assert.Matches(new Regex(@"void\s+ExpandList_Click[\s\S]*?_undoService\.Begin", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+ExpandList_Click[\s\S]*?_vm\.ExpandExitList", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+ExpandList_Click[\s\S]*?_undoService\.Commit", RegexOptions.Compiled), source);
        Assert.Matches(new Regex(@"void\s+ExpandList_Click[\s\S]*?_undoService\.Rollback", RegexOptions.Compiled), source);
    }

    // -----------------------------------------------------------------
    // EscapeMethod combo round-trip
    // -----------------------------------------------------------------

    [Theory]
    [InlineData(0u, 0)]
    [InlineData(1u, 1)]
    [InlineData(2u, 2)]
    [InlineData(3u, 3)]
    [InlineData(5u, 4)]
    [InlineData(99u, -1)]
    public void View_EscapeMethodValueToComboIndex_RoundTrip(uint value, int expectedIndex)
    {
        Assert.Equal(expectedIndex, MapExitPointView.EscapeMethodValueToComboIndex(value));
    }

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(1, 1u)]
    [InlineData(2, 2u)]
    [InlineData(3, 3u)]
    [InlineData(4, 5u)]
    [InlineData(-1, 0u)]
    public void View_EscapeMethodComboIndexToValue_RoundTrip(int index, uint expectedValue)
    {
        Assert.Equal(expectedValue, MapExitPointView.EscapeMethodComboIndexToValue(index));
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Build a synthetic FE8U ROM with:
    /// <list type="bullet">
    ///   <item>map_exit_point_pointer slot pointing to 0x00800000.</item>
    ///   <item>Slot 0 at that table pointing to 0x00810000 (per-map block 0).</item>
    ///   <item>The per-map block at 0x00810000 has 2 rows of (X=0x10+r, Y=0x20+r,
    ///         Escape=0x03, Flag=0) followed by a B0=0xFF terminator.</item>
    ///   <item>NPC region starts at 0x00800000 + 65*4 with one slot pointing
    ///         to 0x00890000 holding 2 rows of NPC data.</item>
    /// </list>
    /// </summary>
    static ROM MakeMinimalFe8uRom()
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        // FE8U: map_exit_point_pointer = 0x3E8AC (from ROMFE8U.cs — the
        // ROM offset of the pointer slot, NOT a GBA pointer), npc_blockadd = 65.
        uint mapExitPointerSlot = rom.RomInfo.map_exit_point_pointer;
        uint tableBase = 0x00800000u;
        BitConverter.GetBytes(tableBase | 0x08000000u).CopyTo(bytes, mapExitPointerSlot);

        // Plant first slot's pointer to per-map block at 0x00810000.
        uint perMapBase = 0x00810000u;
        BitConverter.GetBytes(perMapBase | 0x08000000u).CopyTo(bytes, tableBase);

        // Plant 2 rows then terminator at perMapBase.
        for (int r = 0; r < 2; r++)
        {
            int rowBase = (int)(perMapBase + r * 4);
            bytes[rowBase + 0] = (byte)(0x10 + r);
            bytes[rowBase + 1] = (byte)(0x20 + r);
            bytes[rowBase + 2] = 0x03;
            bytes[rowBase + 3] = 0x00;
        }
        bytes[perMapBase + 2 * 4] = 0xFF; // terminator

        // Plant NPC slot pointer.
        uint npcSlot = tableBase + 65u * 4u;
        uint npcBlock = 0x00890000u;
        BitConverter.GetBytes(npcBlock | 0x08000000u).CopyTo(bytes, npcSlot);
        for (int r = 0; r < 2; r++)
        {
            int rowBase = (int)(npcBlock + r * 4);
            bytes[rowBase + 0] = (byte)(0x50 + r);
            bytes[rowBase + 1] = (byte)(0x60 + r);
            bytes[rowBase + 2] = 0x02;
            bytes[rowBase + 3] = 0x00;
        }
        bytes[npcBlock + 2 * 4] = 0xFF;

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    /// <summary>Walk parents from test bin dir until we find FEBuilderGBA.sln.</summary>
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
