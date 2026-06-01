// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ItemAllocCore — extracts the cross-platform item "new-alloc"
// (StatBooster P12 / Effectiveness P16) logic mirrored from WinForms
// InputFormRef.AllocEvent (#831). Builds a synthetic FE8U ROM with an item
// record whose sub-data pointers are 0, then exercises:
//   - the EXACT WF default templates (byte[20] [1]=5; byte[12] all-1-except-[11]
//     non-Rework; byte[12] [1]=6,[2]=1,[5]=6,[6]=2 Rework),
//   - the toPointer conversion of the appended OFFSET into the item field,
//   - undo rollback restoring the field pointer (and the record u32) to 0,
//   - the no-clobber guard (Ptr!=0 -> no-op) and safety guards.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ItemAllocCoreTests
{
    // Free-space region the synthetic allocator will draw from. The item record
    // sits low; the allocator searches from rom.Data.Length/2 upward (all 0xFF =
    // free), so the new block lands well above the item record.
    const uint ItemAddr = 0x00400000u; // item record under test (index > 0)

    /// <summary>
    /// Build a minimal synthetic FE8U ROM with a single item record at
    /// <see cref="ItemAddr"/> whose P12 (addr+12) and P16 (addr+16) pointer
    /// slots are both 0. The upper half of the ROM is 0xFF (free space) so the
    /// direct FindFreeSpace fallback + the wired headless allocator both find a
    /// home for the appended block.
    /// </summary>
    static ROM MakeFe8uWithItem()
    {
        var bytes = new byte[0x1100000];
        // Fill the upper half with 0xFF so it reads as free space.
        for (int i = bytes.Length / 2; i < bytes.Length; i++) bytes[i] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        // Lay down a benign item record. The bytes don't matter except that the
        // P12/P16 slots (addr+12 / addr+16) are 0 (already zero from the array).
        // Give it an icon byte / weapon type so it's a plausible record.
        bytes[(int)ItemAddr + 6] = 0x01;  // item number
        bytes[(int)ItemAddr + 7] = 0x00;  // weapon type (sword)
        // P12 (addr+12..15) and P16 (addr+16..19) stay 0 — the alloc target.

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    static (ROM rom, ROM prevRom, Undo prevUndo) Enter()
    {
        ROM rom = MakeFe8uWithItem();
        var prevRom = CoreState.ROM;
        var prevUndo = CoreState.Undo;
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        return (rom, prevRom, prevUndo);
    }

    static void Exit(ROM prevRom, Undo prevUndo)
    {
        CoreState.ROM = prevRom;
        CoreState.Undo = prevUndo;
    }

    // =================================================================
    // Template builders — assert the EXACT WF default bytes.
    // =================================================================

    [Fact]
    public void BuildStatBoosterTemplate_Is20BytesWithByte1Equals5()
    {
        byte[] t = ItemAllocCore.BuildStatBoosterTemplate();
        Assert.Equal(20, t.Length);
        Assert.Equal((byte)5, t[1]); // HP+5
        for (int i = 0; i < t.Length; i++)
            if (i != 1) Assert.Equal((byte)0, t[i]);
    }

    [Fact]
    public void BuildEffectivenessTemplate_NonRework_Is12BytesAll1ExceptLast()
    {
        byte[] t = ItemAllocCore.BuildEffectivenessTemplate(skillSystemsRework: false);
        Assert.Equal(12, t.Length);
        for (int i = 0; i < 11; i++) Assert.Equal((byte)1, t[i]);
        Assert.Equal((byte)0, t[11]); // zero-terminator
    }

    [Fact]
    public void BuildEffectivenessTemplate_Rework_Is12BytesSeededArmorCavalry()
    {
        byte[] t = ItemAllocCore.BuildEffectivenessTemplate(skillSystemsRework: true);
        Assert.Equal(12, t.Length);
        // FillArray(12,0) then [1]=6,[2]=1,[5]=6,[6]=2 — everything else 0.
        Assert.Equal((byte)6, t[1]);
        Assert.Equal((byte)1, t[2]); // armor
        Assert.Equal((byte)6, t[5]);
        Assert.Equal((byte)2, t[6]); // cavalry
        Assert.Equal((byte)0, t[0]);
        Assert.Equal((byte)0, t[3]);
        Assert.Equal((byte)0, t[4]);
        for (int i = 7; i < 12; i++) Assert.Equal((byte)0, t[i]);
    }

    // =================================================================
    // AllocStatBonuses — append byte[20] [1]=5 + set toPointer(addr) at P12.
    // =================================================================

    [Fact]
    public void AllocStatBonuses_WritesTemplate_AndSetsPointer_WithUndoData()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            Assert.Equal(0u, rom.u32(ItemAddr + 12)); // precondition: P12 == 0

            var undodata = CoreState.Undo.NewUndoData("StatBooster alloc test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint addr = ItemAllocCore.AllocStatBonuses(rom, ItemAddr, undodata);
                Assert.NotEqual(U.NOT_FOUND, addr);

                // The P12 slot now holds the GBA pointer = toPointer(addr).
                Assert.Equal(U.toPointer(addr), rom.u32(ItemAddr + 12));
                Assert.True(U.isPointer(rom.u32(ItemAddr + 12)));

                // The appended block is the EXACT WF template: 20 bytes, [1]=5.
                int a = (int)addr;
                Assert.Equal((byte)0x00, rom.Data[a + 0]);
                Assert.Equal((byte)0x05, rom.Data[a + 1]);
                for (int i = 2; i < 20; i++)
                    Assert.Equal((byte)0x00, rom.Data[a + i]);
            }
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void AllocStatBonuses_RolledBack_RestoresPointerAndRecordToZero()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            uint original = rom.u32(ItemAddr + 12);
            Assert.Equal(0u, original);

            var undodata = CoreState.Undo.NewUndoData("StatBooster rollback test");
            uint addr;
            using (ROM.BeginUndoScope(undodata))
            {
                addr = ItemAllocCore.AllocStatBonuses(rom, ItemAddr, undodata);
                Assert.NotEqual(U.NOT_FOUND, addr);
                Assert.NotEqual(0u, rom.u32(ItemAddr + 12));
            }
            CoreState.Undo.Push(undodata);
            CoreState.Undo.RunUndo();

            // After rollback the field pointer AND the record u32@P12 are 0 again.
            Assert.Equal(0u, rom.u32(ItemAddr + 12));
        }
        finally { Exit(prevRom, prevUndo); }
    }

    // =================================================================
    // AllocEffectiveness (non-Rework) — append byte[12] all-1-except-[11] at P16.
    // =================================================================

    [Fact]
    public void AllocEffectiveness_NonRework_WritesTemplate_AndSetsPointer()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            Assert.Equal(0u, rom.u32(ItemAddr + 16)); // precondition: P16 == 0

            var undodata = CoreState.Undo.NewUndoData("Effectiveness alloc test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint addr = ItemAllocCore.AllocEffectiveness(rom, ItemAddr, skillSystemsRework: false, undodata);
                Assert.NotEqual(U.NOT_FOUND, addr);

                // P16 holds toPointer(addr).
                Assert.Equal(U.toPointer(addr), rom.u32(ItemAddr + 16));
                Assert.True(U.isPointer(rom.u32(ItemAddr + 16)));

                // EXACT non-Rework template: 12 bytes all 0x01 except [11]==0.
                int a = (int)addr;
                for (int i = 0; i < 11; i++)
                    Assert.Equal((byte)0x01, rom.Data[a + i]);
                Assert.Equal((byte)0x00, rom.Data[a + 11]);
            }
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void AllocEffectiveness_Rework_WritesReworkTemplate()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            var undodata = CoreState.Undo.NewUndoData("Effectiveness rework alloc test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint addr = ItemAllocCore.AllocEffectiveness(rom, ItemAddr, skillSystemsRework: true, undodata);
                Assert.NotEqual(U.NOT_FOUND, addr);

                Assert.Equal(U.toPointer(addr), rom.u32(ItemAddr + 16));
                // EXACT Rework template: [1]=6,[2]=1,[5]=6,[6]=2, rest 0.
                int a = (int)addr;
                Assert.Equal((byte)0x00, rom.Data[a + 0]);
                Assert.Equal((byte)0x06, rom.Data[a + 1]);
                Assert.Equal((byte)0x01, rom.Data[a + 2]);
                Assert.Equal((byte)0x00, rom.Data[a + 3]);
                Assert.Equal((byte)0x00, rom.Data[a + 4]);
                Assert.Equal((byte)0x06, rom.Data[a + 5]);
                Assert.Equal((byte)0x02, rom.Data[a + 6]);
                for (int i = 7; i < 12; i++)
                    Assert.Equal((byte)0x00, rom.Data[a + i]);
            }
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void AllocEffectiveness_RolledBack_RestoresPointerToZero()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            Assert.Equal(0u, rom.u32(ItemAddr + 16));

            var undodata = CoreState.Undo.NewUndoData("Effectiveness rollback test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint addr = ItemAllocCore.AllocEffectiveness(rom, ItemAddr, skillSystemsRework: false, undodata);
                Assert.NotEqual(U.NOT_FOUND, addr);
                Assert.NotEqual(0u, rom.u32(ItemAddr + 16));
            }
            CoreState.Undo.Push(undodata);
            CoreState.Undo.RunUndo();
            Assert.Equal(0u, rom.u32(ItemAddr + 16));
        }
        finally { Exit(prevRom, prevUndo); }
    }

    // =================================================================
    // No-clobber + safety guards.
    // =================================================================

    [Fact]
    public void AllocStatBonuses_AlreadyAllocated_NoOp_DoesNotClobber()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            // Pre-set P12 to an existing pointer.
            uint existing = 0x08123456u;
            rom.write_u32(ItemAddr + 12, existing);

            var undodata = CoreState.Undo.NewUndoData("no-clobber test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint addr = ItemAllocCore.AllocStatBonuses(rom, ItemAddr, undodata);
                Assert.Equal(U.NOT_FOUND, addr);        // refused
                Assert.Equal(existing, rom.u32(ItemAddr + 12)); // untouched
            }
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void AllocEffectiveness_AlreadyAllocated_NoOp_DoesNotClobber()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            uint existing = 0x08234567u;
            rom.write_u32(ItemAddr + 16, existing);

            var undodata = CoreState.Undo.NewUndoData("no-clobber test 2");
            using (ROM.BeginUndoScope(undodata))
            {
                uint addr = ItemAllocCore.AllocEffectiveness(rom, ItemAddr, skillSystemsRework: false, undodata);
                Assert.Equal(U.NOT_FOUND, addr);
                Assert.Equal(existing, rom.u32(ItemAddr + 16));
            }
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void Alloc_NullRom_ReturnsNotFound()
    {
        Assert.Equal(U.NOT_FOUND, ItemAllocCore.AllocStatBonuses(null!, ItemAddr, undodata: null));
        Assert.Equal(U.NOT_FOUND, ItemAllocCore.AllocEffectiveness(null!, ItemAddr, skillSystemsRework: false, undodata: null));
    }

    [Fact]
    public void Alloc_UnsafeItemAddress_ReturnsNotFound()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            // A header / low offset must be refused so the ROM header is never
            // corrupted under undo.
            Assert.Equal(U.NOT_FOUND, ItemAllocCore.AllocStatBonuses(rom, 0x10u, undodata: null));
        }
        finally { Exit(prevRom, prevUndo); }
    }

    // =================================================================
    // Wired-seam path — exercise the EXACT production allocator (#796).
    // =================================================================

    [Fact]
    public void AllocStatBonuses_ViaWiredAppendBinaryData_WritesTemplate()
    {
        var (rom, prevRom, prevUndo) = Enter();
        var prevAlloc = CoreState.AppendBinaryData;
        try
        {
            // Force-wire the headless production allocator (clear first so the
            // "only if not already set" guard re-wires onto THIS rom).
            CoreState.AppendBinaryData = null;
            CoreState.WireHeadlessAppendBinaryData();
            Assert.NotNull(CoreState.AppendBinaryData);

            var undodata = CoreState.Undo.NewUndoData("wired seam test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint addr = ItemAllocCore.AllocStatBonuses(rom, ItemAddr, undodata);
                Assert.NotEqual(U.NOT_FOUND, addr);
                Assert.Equal(U.toPointer(addr), rom.u32(ItemAddr + 12));
                Assert.Equal((byte)0x05, rom.Data[(int)addr + 1]);
            }
        }
        finally
        {
            CoreState.AppendBinaryData = prevAlloc;
            Exit(prevRom, prevUndo);
        }
    }
}
