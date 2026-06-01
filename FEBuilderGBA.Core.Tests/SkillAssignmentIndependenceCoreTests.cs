// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for SkillAssignmentIndependenceCore — the cross-platform "Make
// Selected Class Independent" logic mirrored from WinForms
// SkillAssignmentClassSkillSystemForm.IndependenceButton_Click ->
// PatchUtil.WriteIndependence (#834).
//
// This is the DELIBERATE INVERSE of NV1a's all-reference repoint
// (DataExpansionCore.RepointAllReferences). The defining proof: a per-class
// level-up table SHARED behind TWO class slots (A + B both -> the same base),
// run Independence on slot A, and assert ONLY slot A is repointed to a fresh
// clone while slot B stays on the intact original table. An all-reference
// rescan would have dragged slot B along too — that would BREAK independence.
//
// Builds a synthetic FE8U ROM:
//   - assignLevelUp pointer table at AssignLevelUpBase with 4-byte slots,
//   - slot A (classId 1) and slot B (classId 2) both -> the same shared
//     level-up table (a 0x0000-terminated u16 array),
//   - the upper half is 0xFF (free space) so the appended clone has a home.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class SkillAssignmentIndependenceCoreTests
{
    // Offsets inside the synthetic ROM (low region, well clear of the header).
    const uint AssignLevelUpBase = 0x00200000u; // per-class pointer table base
    const uint SharedTableOffset = 0x00210000u;  // the shared level-up table
    const uint ClassA = 1;   // the class we make independent
    const uint ClassB = 2;   // a class that SHARES the same table

    // The shared table: 3 rows (lv|skill pairs) then a 0x0000 terminator.
    // Row bytes are distinctive so we can assert the clone is byte-verbatim.
    static readonly byte[] SharedRows =
    {
        0x01, 0x11,   // row 0: lv 1, skill 0x11
        0x05, 0x22,   // row 1: lv 5, skill 0x22
        0x0A, 0x33,   // row 2: lv 10, skill 0x33
        0x00, 0x00,   // terminator (0x0000)
    };

    static ROM MakeFe8uWithSharedTable()
    {
        var bytes = new byte[0x1100000];
        // Upper half = 0xFF (free space) so the allocator finds a home.
        for (int i = bytes.Length / 2; i < bytes.Length; i++) bytes[i] = 0xFF;

        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        // Lay down the shared level-up table.
        Array.Copy(SharedRows, 0, bytes, (int)SharedTableOffset, SharedRows.Length);

        // Point BOTH class A and class B slots at the same shared table (GBA
        // pointer = offset + 0x08000000).
        uint sharedGbaPtr = U.toPointer(SharedTableOffset);
        WriteU32(bytes, AssignLevelUpBase + ClassA * 4, sharedGbaPtr);
        WriteU32(bytes, AssignLevelUpBase + ClassB * 4, sharedGbaPtr);

        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    static void WriteU32(byte[] b, uint addr, uint v)
    {
        b[addr + 0] = (byte)(v & 0xFF);
        b[addr + 1] = (byte)((v >> 8) & 0xFF);
        b[addr + 2] = (byte)((v >> 16) & 0xFF);
        b[addr + 3] = (byte)((v >> 24) & 0xFF);
    }

    static (ROM rom, ROM prevRom, Undo prevUndo) Enter()
    {
        ROM rom = MakeFe8uWithSharedTable();
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
    // Row counting + shared detection.
    // =================================================================

    [Fact]
    public void CountLevelUpRows_StopsAtTerminator()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            uint n = SkillAssignmentIndependenceCore.CountLevelUpRows(rom, SharedTableOffset);
            Assert.Equal(3u, n); // 3 rows before the 0x0000 terminator
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void IsTableShared_TrueWhenTwoClassesPointSame()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            // classCount = 4 (covers slots 0..3). A and B share -> true for A.
            Assert.True(SkillAssignmentIndependenceCore.IsTableShared(rom, AssignLevelUpBase, ClassA, 4));
            Assert.True(SkillAssignmentIndependenceCore.IsTableShared(rom, AssignLevelUpBase, ClassB, 4));
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void IsTableShared_FalseWhenUnique()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            // Repoint B elsewhere (a different, in-bounds, safe offset) so A is
            // no longer shared.
            uint other = U.toPointer(SharedTableOffset + 0x1000);
            rom.write_u32(AssignLevelUpBase + ClassB * 4, other);
            Assert.False(SkillAssignmentIndependenceCore.IsTableShared(rom, AssignLevelUpBase, ClassA, 4));
        }
        finally { Exit(prevRom, prevUndo); }
    }

    // =================================================================
    // THE single-slot proof (inverse of NV1a's all-reference repoint).
    // =================================================================

    [Fact]
    public void MakeIndependent_RepointsOnlySlotA_SlotBUntouched_OldRegionIntact()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            uint slotA = AssignLevelUpBase + ClassA * 4;
            uint slotB = AssignLevelUpBase + ClassB * 4;
            uint sharedGbaPtr = U.toPointer(SharedTableOffset);

            // Precondition: A and B both point at the same shared table.
            Assert.Equal(sharedGbaPtr, rom.u32(slotA));
            Assert.Equal(sharedGbaPtr, rom.u32(slotB));

            var undodata = CoreState.Undo.NewUndoData("Independence single-slot test");
            uint newOffset;
            using (ROM.BeginUndoScope(undodata))
            {
                newOffset = SkillAssignmentIndependenceCore.MakeIndependent(
                    rom, AssignLevelUpBase, ClassA, undodata);
            }
            Assert.NotEqual(U.NOT_FOUND, newOffset);
            Assert.NotEqual(0u, newOffset);

            // SLOT A now points at the NEW clone (a DIFFERENT pointer).
            uint newGbaPtr = U.toPointer(newOffset);
            Assert.Equal(newGbaPtr, rom.u32(slotA));
            Assert.NotEqual(sharedGbaPtr, rom.u32(slotA));

            // SLOT B is UNTOUCHED — still points at the original shared table.
            // THIS is the single-slot proof (the inverse of NV1a all-reference).
            Assert.Equal(sharedGbaPtr, rom.u32(slotB));

            // The OLD shared region is byte-for-byte intact (not wiped/moved).
            for (uint i = 0; i < SharedRows.Length; i++)
                Assert.Equal(SharedRows[i], rom.Data[SharedTableOffset + i]);

            // The clone is byte-verbatim ((rowCount+1)*2 = 8 bytes incl. term).
            for (uint i = 0; i < SharedRows.Length; i++)
                Assert.Equal(SharedRows[i], rom.Data[newOffset + i]);
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void MakeIndependent_RolledBack_RestoresSlotAToSharedPointer()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            uint slotA = AssignLevelUpBase + ClassA * 4;
            uint slotB = AssignLevelUpBase + ClassB * 4;
            uint sharedGbaPtr = U.toPointer(SharedTableOffset);

            var undodata = CoreState.Undo.NewUndoData("Independence rollback test");
            uint newOffset;
            using (ROM.BeginUndoScope(undodata))
            {
                newOffset = SkillAssignmentIndependenceCore.MakeIndependent(
                    rom, AssignLevelUpBase, ClassA, undodata);
                Assert.NotEqual(U.NOT_FOUND, newOffset);
                Assert.NotEqual(sharedGbaPtr, rom.u32(slotA)); // moved
            }
            CoreState.Undo.Push(undodata);
            CoreState.Undo.RunUndo();

            // After rollback, slot A is back to the shared pointer and slot B
            // is (as always) unchanged.
            Assert.Equal(sharedGbaPtr, rom.u32(slotA));
            Assert.Equal(sharedGbaPtr, rom.u32(slotB));
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void MakeIndependent_ClonesViaWiredAppendBinaryData_SharedSeam()
    {
        // Prove the helper uses the production CoreState.AppendBinaryData seam
        // (#796) when wired — the exact allocator the Avalonia/CLI heads use.
        var (rom, prevRom, prevUndo) = Enter();
        var prevAppend = CoreState.AppendBinaryData;
        try
        {
            bool seamCalled = false;
            CoreState.AppendBinaryData = (data, undo) =>
            {
                seamCalled = true;
                uint addr = rom.FindFreeSpace((uint)(rom.Data.Length / 2), (uint)data.Length);
                if (addr == U.NOT_FOUND) return U.NOT_FOUND;
                if (undo != null) rom.write_range(addr, data, undo);
                else rom.write_range(addr, data);
                return addr;
            };

            var undodata = CoreState.Undo.NewUndoData("Independence seam test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint newOffset = SkillAssignmentIndependenceCore.MakeIndependent(
                    rom, AssignLevelUpBase, ClassA, undodata);
                Assert.NotEqual(U.NOT_FOUND, newOffset);
            }
            Assert.True(seamCalled, "MakeIndependent must allocate via the wired CoreState.AppendBinaryData seam.");
        }
        finally
        {
            CoreState.AppendBinaryData = prevAppend;
            Exit(prevRom, prevUndo);
        }
    }

    // =================================================================
    // Guards / no-op cases.
    // =================================================================

    [Fact]
    public void MakeIndependent_AbsentPointer_IsNoOp()
    {
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            // Class 3's slot is 0 (no table). Independence must be a no-op.
            const uint classNoTable = 3;
            uint slot = AssignLevelUpBase + classNoTable * 4;
            Assert.Equal(0u, rom.u32(slot)); // precondition: empty slot

            var undodata = CoreState.Undo.NewUndoData("Independence absent-ptr test");
            uint result;
            using (ROM.BeginUndoScope(undodata))
            {
                result = SkillAssignmentIndependenceCore.MakeIndependent(
                    rom, AssignLevelUpBase, classNoTable, undodata);
            }
            Assert.Equal(U.NOT_FOUND, result);
            // Slot stays 0 — nothing written.
            Assert.Equal(0u, rom.u32(slot));
        }
        finally { Exit(prevRom, prevUndo); }
    }

    [Fact]
    public void MakeIndependent_NullRom_ReturnsNotFound()
    {
        Assert.Equal(U.NOT_FOUND,
            SkillAssignmentIndependenceCore.MakeIndependent(null, AssignLevelUpBase, ClassA, null));
    }

    [Fact]
    public void MakeIndependent_DoesNotMoveOtherClassWhenManySharers()
    {
        // Extra-strong inverse-of-NV1a proof: THREE classes share the table.
        // Making one independent must leave the OTHER TWO on the original base.
        var (rom, prevRom, prevUndo) = Enter();
        try
        {
            uint sharedGbaPtr = U.toPointer(SharedTableOffset);
            const uint ClassC = 3;
            rom.write_u32(AssignLevelUpBase + ClassC * 4, sharedGbaPtr); // C joins the share

            uint slotA = AssignLevelUpBase + ClassA * 4;
            uint slotB = AssignLevelUpBase + ClassB * 4;
            uint slotC = AssignLevelUpBase + ClassC * 4;

            var undodata = CoreState.Undo.NewUndoData("Independence many-sharer test");
            using (ROM.BeginUndoScope(undodata))
            {
                uint newOffset = SkillAssignmentIndependenceCore.MakeIndependent(
                    rom, AssignLevelUpBase, ClassA, undodata);
                Assert.NotEqual(U.NOT_FOUND, newOffset);
                Assert.NotEqual(sharedGbaPtr, rom.u32(slotA));
            }

            // B and C BOTH still point at the original shared table.
            Assert.Equal(sharedGbaPtr, rom.u32(slotB));
            Assert.Equal(sharedGbaPtr, rom.u32(slotC));
        }
        finally { Exit(prevRom, prevUndo); }
    }
}
