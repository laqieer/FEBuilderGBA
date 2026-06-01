// SPDX-License-Identifier: GPL-3.0-or-later
// #825 — WorldMapImage Border + IconData "List Expand" wiring tests.
//
// Proves the two previously-inert WorldMap list-expand buttons now grow their
// fixed-RomInfo-pointer tables via
//   WorldMapImageViewModel.ExpandBorderList / ExpandIconList
//     -> DataExpansionCore.ExpandTableTo   (move + copy + 0xFFFFFFFF terminator
//                                            + wipe old + single-slot repoint of
//                                            the canonical pointer)
//     -> DataExpansionCore.RepointAllReferences (raw 32-bit pointers + ARM-Thumb
//                                            LDR literal-pool loads to the old base)
// mirroring WF InputFormRef.ExpandsArea -> MoveToFreeSapceForm.SearchPointer's
// all-reference rescan.
//
// The riskiest behavior — that EVERY reference to the old base (the canonical
// RomInfo slot, a SECOND raw pointer, AND an ARM LDR literal-pool load) is
// repointed to the new base after expand, and ALL restored on rollback — is
// asserted here per the approved plan v2 / Blocking-3. We also pin:
//   * the row count grows by the requested delta,
//   * the 0xFFFFFFFF terminator lands at newBase + newCount*entrySize,
//   * existing rows are copied verbatim,
//   * the danger-zone refusal (base 0x0–0x200 -> RepointAllReferences == 0)
//     does NOT apply to these tables (their bases are well above 0x200),
//   * a CLEAN ROM with no secondary refs expands successfully with
//     RepointAllReferences returning 0 (NOTE A — 0 is success, not a rollback)
//     and NewCount set on the VM (NOTE B).
//
// NOTE on ROM source: the repo does NOT commit real .gba ROMs (the roms/ folder
// is empty locally and in CI). So — like the sibling WorldMapImageParityTests /
// SongInstrumentExpandTests — these build a deterministic synthetic FE8U ROM
// (header signature BE8E01, 16 MB so RomInfo resolves) and plant the canonical
// pointer + table rows + a known 0xFF free region for ExpandTableTo to relocate
// into.
//
// Marked [Collection("SharedState")] because the tests mutate CoreState.ROM /
// CoreState.Undo (matches the sibling suites).
using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests.GapSweep;

[Collection("SharedState")]
public class WorldMapImageListExpandTests : IDisposable
{
    const int BorderStride = 12;
    const int IconStride = 16;

    // Synthetic-ROM layout (all offsets; GBA pointers add 0x08000000).
    const uint TableBase = 0x00100000u;  // table base before expand
    const uint FreeRegion = 0x00180000u; // known 0xFF run -> ExpandTableTo lands here
    const uint RawSlot = 0x00004000u;    // a SECOND raw pointer to TableBase
    const uint LdrInstr = 0x00005000u;   // ARM Thumb LDR r0,[pc,#0] (0x4800)
    const uint LdrSlot = LdrInstr + 4;   // its literal-pool slot

    readonly ROM? _savedRom;
    readonly Undo? _savedUndo;

    public WorldMapImageListExpandTests()
    {
        _savedRom = CoreState.ROM;
        _savedUndo = CoreState.Undo;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Undo = _savedUndo;
    }

    // ------------------------------------------------------------------
    // Border list expand — repoint assertions (canonical + raw + LDR).
    // ------------------------------------------------------------------

    [Fact]
    public void ExpandBorderList_GrowsCount_WritesTerminator_CopiesRows_RepointsAllThreeRefs()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint ptr = rom.RomInfo.worldmap_county_border_pointer; // canonical slot (0xC2B74)
        Assert.True(U.toOffset(ptr) > 0x200u, "canonical slot must be > danger zone");

        const int currentCount = 3;
        PlantBorderTable(rom, ptr, TableBase, currentCount);
        PlantSecondaryRefs(rom, TableBase);   // raw pointer + LDR literal -> TableBase
        PlantFreeRegion(rom, FreeRegion, 0x4000);

        // Sanity: all three references resolve to TableBase before expand.
        Assert.Equal(TableBase, rom.p32(ptr));
        Assert.Equal(TableBase, rom.p32(RawSlot));
        Assert.Equal(TableBase, rom.p32(LdrSlot));

        var vm = new WorldMapImageViewModel();
        vm.LoadAll();
        var before = vm.LoadBorderList();
        Assert.Equal(currentCount, before.Count);
        Assert.Equal(currentCount, vm.BorderReadCount);

        const uint newCount = currentCount + 2; // delta = +2
        string err = Expand(vm, isBorder: true, newCount);
        Assert.Equal("", err);

        // NewCount honored on the VM (NOTE B) — no re-scan undercount.
        Assert.Equal((int)newCount, vm.BorderReadCount);
        uint newBase = U.toOffset(vm.BorderReadStartAddress);
        Assert.NotEqual(TableBase, newBase);

        // The displayed list (built WITHOUT re-scanning) shows the full count.
        var after = vm.BuildBorderListForCount(newBase, vm.BorderReadCount);
        Assert.Equal((int)newCount, after.Count);

        // 0xFFFFFFFF terminator at newBase + newCount*stride.
        uint termAddr = newBase + newCount * (uint)BorderStride;
        Assert.Equal(0xFFFFFFFFu, rom.u32(termAddr));

        // Existing rows copied verbatim (distinct per-row marker bytes).
        for (int i = 0; i < currentCount; i++)
        {
            uint a = newBase + (uint)(i * BorderStride);
            Assert.Equal(U.toPointer(0x200000u + (uint)i * 0x100u), rom.u32(a + 0));
            Assert.Equal(U.toPointer(0x300000u + (uint)i * 0x100u), rom.u32(a + 4));
        }
        // New rows are zero-filled.
        for (int i = currentCount; i < (int)newCount; i++)
        {
            uint a = newBase + (uint)(i * BorderStride);
            Assert.Equal(0u, rom.u32(a + 0));
            Assert.Equal(0u, rom.u32(a + 4));
        }

        // ALL THREE references now point at the new base.
        Assert.Equal(newBase, rom.p32(ptr));       // canonical (ExpandTableTo)
        Assert.Equal(newBase, rom.p32(RawSlot));   // raw secondary (RepointAllReferences)
        Assert.Equal(newBase, rom.p32(LdrSlot));   // LDR literal (RepointAllReferences)
    }

    [Fact]
    public void ExpandBorderList_Rollback_RestoresAllThreeRefs_AndOldRegion()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint ptr = rom.RomInfo.worldmap_county_border_pointer;
        const int currentCount = 3;
        PlantBorderTable(rom, ptr, TableBase, currentCount);
        PlantSecondaryRefs(rom, TableBase);
        PlantFreeRegion(rom, FreeRegion, 0x4000);

        byte[] oldRegion = rom.getBinaryData(TableBase, (uint)(currentCount * BorderStride));

        var vm = new WorldMapImageViewModel();
        vm.LoadAll();
        Assert.Equal(currentCount, vm.LoadBorderList().Count);

        string err = Expand(vm, isBorder: true, currentCount + 2);
        Assert.Equal("", err);
        Assert.NotEqual(TableBase, rom.p32(ptr)); // moved away

        CoreState.Undo.RunUndo();

        // Every reference restored to the old base.
        Assert.Equal(TableBase, rom.p32(ptr));
        Assert.Equal(TableBase, rom.p32(RawSlot));
        Assert.Equal(TableBase, rom.p32(LdrSlot));
        // Old region bytes restored verbatim (ExpandTableTo had wiped them to 0).
        Assert.Equal(oldRegion, rom.getBinaryData(TableBase, (uint)(currentCount * BorderStride)));
    }

    // ------------------------------------------------------------------
    // IconData list expand — same three-reference repoint assertions.
    // ------------------------------------------------------------------

    [Fact]
    public void ExpandIconList_GrowsCount_WritesTerminator_CopiesRows_RepointsAllThreeRefs()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint ptr = rom.RomInfo.worldmap_icon_data_pointer; // canonical slot (0xBB674)
        Assert.True(U.toOffset(ptr) > 0x200u, "canonical slot must be > danger zone");

        const int currentCount = 4;
        PlantIconTable(rom, ptr, TableBase, currentCount);
        PlantSecondaryRefs(rom, TableBase);
        PlantFreeRegion(rom, FreeRegion, 0x4000);

        Assert.Equal(TableBase, rom.p32(ptr));
        Assert.Equal(TableBase, rom.p32(RawSlot));
        Assert.Equal(TableBase, rom.p32(LdrSlot));

        var vm = new WorldMapImageViewModel();
        vm.LoadAll();
        var before = vm.LoadIconList();
        Assert.Equal(currentCount, before.Count);
        Assert.Equal(currentCount, vm.IconReadCount);

        const uint newCount = currentCount + 3; // delta = +3
        string err = Expand(vm, isBorder: false, newCount);
        Assert.Equal("", err);

        Assert.Equal((int)newCount, vm.IconReadCount);
        uint newBase = U.toOffset(vm.IconReadStartAddress);
        Assert.NotEqual(TableBase, newBase);

        var after = vm.BuildIconListForCount(newBase, vm.IconReadCount);
        Assert.Equal((int)newCount, after.Count);

        uint termAddr = newBase + newCount * (uint)IconStride;
        Assert.Equal(0xFFFFFFFFu, rom.u32(termAddr));

        // Existing rows copied verbatim (the +4 pointer field is the marker).
        for (int i = 0; i < currentCount; i++)
        {
            uint a = newBase + (uint)(i * IconStride);
            Assert.Equal(U.toPointer(0x200000u + (uint)i * 0x100u), rom.u32(a + 4));
        }
        // New rows zero-filled.
        for (int i = currentCount; i < (int)newCount; i++)
        {
            uint a = newBase + (uint)(i * IconStride);
            Assert.Equal(0u, rom.u32(a + 4));
        }

        Assert.Equal(newBase, rom.p32(ptr));
        Assert.Equal(newBase, rom.p32(RawSlot));
        Assert.Equal(newBase, rom.p32(LdrSlot));
    }

    [Fact]
    public void ExpandIconList_Rollback_RestoresAllThreeRefs_AndOldRegion()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint ptr = rom.RomInfo.worldmap_icon_data_pointer;
        const int currentCount = 4;
        PlantIconTable(rom, ptr, TableBase, currentCount);
        PlantSecondaryRefs(rom, TableBase);
        PlantFreeRegion(rom, FreeRegion, 0x4000);

        byte[] oldRegion = rom.getBinaryData(TableBase, (uint)(currentCount * IconStride));

        var vm = new WorldMapImageViewModel();
        vm.LoadAll();
        Assert.Equal(currentCount, vm.LoadIconList().Count);

        string err = Expand(vm, isBorder: false, currentCount + 1);
        Assert.Equal("", err);
        Assert.NotEqual(TableBase, rom.p32(ptr));

        CoreState.Undo.RunUndo();

        Assert.Equal(TableBase, rom.p32(ptr));
        Assert.Equal(TableBase, rom.p32(RawSlot));
        Assert.Equal(TableBase, rom.p32(LdrSlot));
        Assert.Equal(oldRegion, rom.getBinaryData(TableBase, (uint)(currentCount * IconStride)));
    }

    // ------------------------------------------------------------------
    // NOTE A — a CLEAN ROM (canonical pointer ONLY, no secondary refs)
    // expands successfully; RepointAllReferences returns 0 (no further
    // slots) and that is NOT treated as a rollback. NewCount is set.
    // ------------------------------------------------------------------

    [Fact]
    public void ExpandBorderList_CleanRom_NoSecondaryRefs_SucceedsWithZeroRepoint_AndSetsNewCount()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint ptr = rom.RomInfo.worldmap_county_border_pointer;
        const int currentCount = 2;
        PlantBorderTable(rom, ptr, TableBase, currentCount);
        // Deliberately NO secondary references (only the canonical pointer).
        PlantFreeRegion(rom, FreeRegion, 0x2000);

        // RepointAllReferences alone would find only the canonical slot, which
        // ExpandTableTo has already moved to the new base — so it sees ZERO
        // remaining references to the OLD base and returns 0. That is success.
        uint oldBase = rom.p32(ptr);
        Assert.Equal(TableBase, oldBase);

        var vm = new WorldMapImageViewModel();
        vm.LoadAll();
        Assert.Equal(currentCount, vm.LoadBorderList().Count);

        const uint newCount = currentCount + 1;
        string err = Expand(vm, isBorder: true, newCount);

        // Expand succeeded despite RepointAllReferences returning 0 (NOTE A).
        Assert.Equal("", err);
        Assert.Equal((int)newCount, vm.BorderReadCount); // NewCount set (NOTE B)
        uint newBase = U.toOffset(vm.BorderReadStartAddress);
        Assert.NotEqual(TableBase, newBase);
        // The canonical pointer still resolves to the (new) base — not orphaned.
        Assert.Equal(newBase, rom.p32(ptr));

        // Cross-check: a direct RepointAllReferences for the now-stale oldBase
        // (post-move) finds no references and returns 0 without throwing.
        int n = DataExpansionCore.RepointAllReferences(rom, oldBase, newBase, null);
        Assert.Equal(0, n);
    }

    // ------------------------------------------------------------------
    // Danger-zone non-applicability: both canonical slots are > 0x200, so the
    // RepointAllReferences danger-zone refusal (base 0x0–0x200 -> returns 0)
    // can NOT silently no-op a legitimate expand of these tables.
    // ------------------------------------------------------------------

    [Fact]
    public void WorldMapTableBases_AreAboveDangerZone_RepointNotSilentlyNoOped()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;

        // The table base we relocate to (and from) is well above 0x200, so the
        // base-level danger-zone guard in RepointAllReferences does not apply.
        Assert.True(TableBase > 0x200u);
        Assert.True(FreeRegion > 0x200u);

        // A genuine reference at TableBase IS repointed (not skipped): plant one
        // raw pointer to TableBase and confirm the helper rewrites it.
        PlantSecondaryRefs(rom, TableBase);
        uint dest = 0x00190000u;
        int n = DataExpansionCore.RepointAllReferences(rom, TableBase, dest, null);
        Assert.True(n >= 2, $"expected >=2 repointed refs (raw + LDR), got {n}");
        Assert.Equal(dest, rom.p32(RawSlot));
        Assert.Equal(dest, rom.p32(LdrSlot));
    }

    [Fact]
    public void ExpandBorderList_NewCountEqualsCurrent_IsNoOpSuccess()
    {
        ROM rom = MakeFe8uRom();
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        uint ptr = rom.RomInfo.worldmap_county_border_pointer;
        const int currentCount = 3;
        PlantBorderTable(rom, ptr, TableBase, currentCount);

        var vm = new WorldMapImageViewModel();
        vm.LoadAll();
        Assert.Equal(currentCount, vm.LoadBorderList().Count);

        // Equal count -> no-op success, base unchanged, no error.
        string err = Expand(vm, isBorder: true, currentCount);
        Assert.Equal("", err);
        Assert.Equal(TableBase, rom.p32(ptr));
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    /// <summary>Run the expand inside an ambient undo scope (mirrors the
    /// View's UndoService.Begin / Commit), pushing the transaction so
    /// CoreState.Undo.RunUndo() can roll it back.</summary>
    static string Expand(WorldMapImageViewModel vm, bool isBorder, uint newCount)
    {
        var ud = CoreState.Undo.NewUndoData("WorldMap ExpandList test");
        string err;
        using (ROM.BeginUndoScope(ud))
        {
            err = isBorder ? vm.ExpandBorderList(newCount, ud)
                           : vm.ExpandIconList(newCount, ud);
        }
        if (string.IsNullOrEmpty(err))
            CoreState.Undo.Push(ud);
        return err;
    }

    static ROM MakeFe8uRom()
    {
        var rom = new ROM();
        rom.LoadLow("synth-fe8u.gba", new byte[0x1000000], "BE8E01");
        return rom;
    }

    /// <summary>Plant a 12-byte border table with <paramref name="count"/>
    /// valid rows (two pointer fields each), the canonical pointer slot pointing
    /// at <paramref name="baseAddr"/>, and a 0xFFFFFFFF stop row after.</summary>
    static void PlantBorderTable(ROM rom, uint ptrSlot, uint baseAddr, int count)
    {
        PlantPointer(rom, ptrSlot, baseAddr);
        for (int i = 0; i < count; i++)
        {
            uint row = baseAddr + (uint)(i * BorderStride);
            PlantU32(rom, row + 0, U.toPointer(0x200000u + (uint)i * 0x100u));
            PlantU32(rom, row + 4, U.toPointer(0x300000u + (uint)i * 0x100u));
            PlantU32(rom, row + 8, 0u);
        }
        // Terminator row so the read scan stops at `count`.
        PlantU32(rom, baseAddr + (uint)(count * BorderStride) + 0, 0xFFFFFFFFu);
    }

    /// <summary>Plant a 16-byte icon-data table with <paramref name="count"/>
    /// valid rows (the +4 pointer field is the validity predicate).</summary>
    static void PlantIconTable(ROM rom, uint ptrSlot, uint baseAddr, int count)
    {
        PlantPointer(rom, ptrSlot, baseAddr);
        for (int i = 0; i < count; i++)
        {
            uint row = baseAddr + (uint)(i * IconStride);
            PlantU32(rom, row + 4, U.toPointer(0x200000u + (uint)i * 0x100u));
        }
        // IconData has no terminator row in the data model; the next row's +4 is
        // left zero (0x00000000), which fails U.isPointer -> scan stops.
        PlantU32(rom, baseAddr + (uint)(count * IconStride) + 4, 0x00000000u);
    }

    /// <summary>Plant a SECOND raw 32-bit pointer + an ARM Thumb LDR literal-pool
    /// load, both referencing <paramref name="baseAddr"/>.</summary>
    static void PlantSecondaryRefs(ROM rom, uint baseAddr)
    {
        PlantPointer(rom, RawSlot, baseAddr);          // raw 32-bit pointer
        rom.Data[LdrInstr + 0] = 0x00;                 // ldr r0,[pc,#0]
        rom.Data[LdrInstr + 1] = 0x48;                 // = 0x4800
        PlantPointer(rom, LdrSlot, baseAddr);          // literal-pool slot
    }

    static void PlantFreeRegion(ROM rom, uint start, int length)
    {
        for (int i = 0; i < length; i++)
            rom.Data[start + (uint)i] = 0xFF;
    }

    static void PlantU32(ROM rom, uint addr, uint value)
    {
        rom.Data[addr + 0] = (byte)(value & 0xFF);
        rom.Data[addr + 1] = (byte)((value >> 8) & 0xFF);
        rom.Data[addr + 2] = (byte)((value >> 16) & 0xFF);
        rom.Data[addr + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void PlantPointer(ROM rom, uint slotAddr, uint targetOffset)
        => PlantU32(rom, slotAddr, U.toPointer(targetOffset));
}
