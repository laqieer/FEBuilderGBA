// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class MoveCostIndependenceCoreTests
{
    const uint ClassBase = 0x20000;
    const uint SharedCostTable = 0x30000;
    const uint ClassCount = 4;

    static uint GetMoveCostPointerOffset(ROM rom) => rom.RomInfo.version == 6 ? 52u : 56u;

    static ROM MakeRom(string gameCode = "BE8E01")
    {
        var data = new byte[0x1000000];
        Array.Fill(data, (byte)0xFF, data.Length / 2, data.Length / 2);
        var rom = new ROM();
        rom.LoadLow("synthetic-movecost.gba", data, gameCode);
        uint moveCostPointerOffset = GetMoveCostPointerOffset(rom);
        rom.write_p32(rom.RomInfo.class_pointer, ClassBase);

        for (uint id = 0; id < ClassCount; id++)
            rom.write_u8(ClassBase + id * rom.RomInfo.class_datasize + 4, id == 0 ? (byte)0 : (byte)1);

        for (uint i = 0; i < MoveCostIndependenceCore.GetTableSize(rom); i++)
            rom.write_u8(SharedCostTable + i, (byte)(i ^ 0x5A));

        rom.write_p32(ClassBase + moveCostPointerOffset, SharedCostTable);
        rom.write_p32(ClassBase + rom.RomInfo.class_datasize + moveCostPointerOffset, SharedCostTable);
        return rom;
    }

    static (ROM? rom, Undo? undo, Func<byte[], Undo.UndoData, uint>? append) Enter(ROM rom)
    {
        var previous = (CoreState.ROM, CoreState.Undo, CoreState.AppendBinaryData);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        CoreState.AppendBinaryData = (data, undo) =>
        {
            uint offset = rom.FindFreeSpace((uint)(rom.Data.Length / 2), (uint)data.Length);
            if (offset == U.NOT_FOUND) return U.NOT_FOUND;
            if (undo != null)
                rom.write_range(offset, data, undo);
            else
                rom.write_range(offset, data);
            return offset;
        };
        return previous;
    }

    static void Exit((ROM? rom, Undo? undo, Func<byte[], Undo.UndoData, uint>? append) previous)
    {
        CoreState.ROM = previous.rom;
        CoreState.Undo = previous.undo;
        CoreState.AppendBinaryData = previous.append;
    }

    [Theory]
    [InlineData("AFEJ01", 54u)]
    [InlineData("AE7E01", 68u)]
    [InlineData("BE8E01", 68u)]
    public void GetTableSize_UsesVersionSpecificCostBlockLength(string gameCode, uint expected)
    {
        var rom = MakeRom(gameCode);
        Assert.Equal(expected, MoveCostIndependenceCore.GetTableSize(rom));
    }

    [Fact]
    public void MakeIndependent_ClonesVerbatimAndRepointsOnlySelectedClass()
        => AssertMakeIndependentClone("BE8E01", MoveCostIndependenceCore.FE7AndFE8TableSize);

    [Theory]
    [InlineData("BE8E01", (byte)0x00, true)]
    [InlineData("BE8E01", (byte)0xFF, true)]
    [InlineData("BE8E01", (byte)0x00, false)]
    [InlineData("BE8E01", (byte)0xFF, false)]
    [InlineData("AFEJ01", (byte)0xFF, true)]
    public void UniformTables_RemainOccupiedAfterCloningAndUndo(
        string gameCode, byte fill, bool useAppender)
    {
        var rom = MakeRom(gameCode);
        uint size = MoveCostIndependenceCore.GetTableSize(rom);
        uint source = (uint)(rom.Data.Length / 2);
        uint selectedSlot = ClassBase + GetMoveCostPointerOffset(rom);
        uint otherSlot = selectedSlot + rom.RomInfo.class_datasize;
        rom.write_fill(source, size, fill);
        rom.write_p32(selectedSlot, source);
        rom.write_p32(otherSlot, source);
        var previous = Enter(rom);
        try
        {
            if (!useAppender) CoreState.AppendBinaryData = null;
            var undo = CoreState.Undo.NewUndoData("Uniform cost table");
            uint clone;
            using (ROM.BeginUndoScope(undo))
                clone = MoveCostIndependenceCore.MakeIndependent(
                    rom, ClassBase, selectedSlot, ClassCount, undo);

            Assert.NotEqual(U.NOT_FOUND, clone);
            Assert.True(clone >= source + size);
            Assert.Equal(source, rom.p32(otherSlot));

            uint next = rom.FindFreeSpace(source, size);
            Assert.NotEqual(U.NOT_FOUND, next);
            Assert.True(next >= clone + size);
            if (fill == 0xFF)
            {
                uint expansion = DataExpansionCore.FindFreeSpace(rom, size, source);
                Assert.NotEqual(U.NOT_FOUND, expansion);
                Assert.True(expansion >= clone + size);
            }
            Assert.NotEqual(clone, rom.Clone().FindFreeSpace(clone, size));
            rom.write_fill(next, size, 0xC3);
            Assert.All(rom.getBinaryData(source, size), value => Assert.Equal(fill, value));
            Assert.All(rom.getBinaryData(clone, size), value => Assert.Equal(fill, value));

            CoreState.Undo.Push(undo);
            CoreState.Undo.RunUndo();
            Assert.Equal(source, rom.p32(selectedSlot));
            Assert.Equal(clone, rom.FindFreeSpace(clone, size));
        }
        finally { Exit(previous); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreeSpace_UsesTheExplicitRomWhenGlobalRomIsAbsentOrDifferent(bool useDifferentRom)
    {
        var rom = MakeRom();
        uint size = MoveCostIndependenceCore.GetTableSize(rom);
        uint source = (uint)(rom.Data.Length / 2);
        uint clone = source + size;
        rom.write_p32(ClassBase + GetMoveCostPointerOffset(rom), source);
        rom.write_p32(ClassBase + rom.RomInfo.class_datasize + GetMoveCostPointerOffset(rom), clone);
        var previousRom = CoreState.ROM;
        try
        {
            if (useDifferentRom)
            {
                var different = new ROM();
                different.LoadLow("different-allocator-state.gba", new byte[0x1000], "NAZO");
                CoreState.ROM = different;
            }
            else
            {
                CoreState.ROM = null;
            }

            uint next = rom.FindFreeSpace(source, size);
            uint expansion = DataExpansionCore.FindFreeSpace(rom, size, source);
            Assert.NotEqual(U.NOT_FOUND, next);
            Assert.NotEqual(U.NOT_FOUND, expansion);
            Assert.True(next >= clone + size);
            Assert.True(expansion >= clone + size);
        }
        finally { CoreState.ROM = previousRom; }
    }

    [Theory]
    [InlineData("AFEJ01", 54u)]
    public void MakeIndependent_Fe6ClonesExactly54Bytes(string gameCode, uint expectedSize)
        => AssertMakeIndependentClone(gameCode, expectedSize);

    void AssertMakeIndependentClone(string gameCode, uint expectedSize)
    {
        var rom = MakeRom(gameCode);
        uint moveCostPointerOffset = GetMoveCostPointerOffset(rom);
        var previous = Enter(rom);
        try
        {
            uint selectedClass = ClassBase;
            uint selectedSlot = selectedClass + moveCostPointerOffset;
            uint otherSlot = selectedClass + rom.RomInfo.class_datasize + moveCostPointerOffset;
            uint oldPointer = rom.u32(selectedSlot);
            Assert.True(MoveCostIndependenceCore.IsTableShared(rom, selectedClass, selectedSlot, ClassCount));

            var undo = CoreState.Undo.NewUndoData("Move cost independence");
            uint cloneOffset;
            using (ROM.BeginUndoScope(undo))
                cloneOffset = MoveCostIndependenceCore.MakeIndependent(rom, selectedClass, selectedSlot, ClassCount, undo);

            Assert.NotEqual(U.NOT_FOUND, cloneOffset);
            Assert.Equal(U.toPointer(cloneOffset), rom.u32(selectedSlot));
            Assert.Equal(oldPointer, rom.u32(otherSlot));
            for (uint i = 0; i < expectedSize; i++)
            {
                Assert.Equal((byte)(i ^ 0x5A), rom.u8(SharedCostTable + i));
                Assert.Equal((byte)(i ^ 0x5A), rom.u8(cloneOffset + i));
            }
            Assert.Equal((byte)0xFF, rom.u8(cloneOffset + expectedSize));
            Assert.False(MoveCostIndependenceCore.IsTableShared(rom, selectedClass, selectedSlot, ClassCount));
        }
        finally { Exit(previous); }
    }

    [Fact]
    public void MakeIndependent_RollbackRestoresAllocationAndSelectedPointer()
    {
        var rom = MakeRom();
        var previous = Enter(rom);
        try
        {
            uint slot = ClassBase + GetMoveCostPointerOffset(rom);
            uint original = rom.u32(slot);
            uint allocatedOffset = rom.FindFreeSpace((uint)(rom.Data.Length / 2), 68);
            var undo = CoreState.Undo.NewUndoData("Move cost rollback");
            using (ROM.BeginUndoScope(undo))
                Assert.NotEqual(U.NOT_FOUND,
                    MoveCostIndependenceCore.MakeIndependent(rom, ClassBase, slot, ClassCount, undo));

            CoreState.Undo.Push(undo);
            CoreState.Undo.RunUndo();
            Assert.Equal(original, rom.u32(slot));
            Assert.Equal((byte)0xFF, rom.u8(allocatedOffset));
        }
        finally { Exit(previous); }
    }

    [Fact]
    public void MakeIndependent_FallbackAllocationWithExplicitUndoRestoresClonedBytes()
    {
        var rom = MakeRom();
        var previous = Enter(rom);
        try
        {
            CoreState.AppendBinaryData = null;
            uint slot = ClassBase + GetMoveCostPointerOffset(rom);
            uint originalPointer = rom.u32(slot);
            var undo = CoreState.Undo.NewUndoData("Move cost explicit undo");

            uint cloneOffset = MoveCostIndependenceCore.MakeIndependent(
                rom, ClassBase, slot, ClassCount, undo);

            Assert.NotEqual(U.NOT_FOUND, cloneOffset);
            CoreState.Undo.Push(undo);
            CoreState.Undo.RunUndo();

            Assert.Equal(originalPointer, rom.u32(slot));
            Assert.All(rom.getBinaryData(cloneOffset, MoveCostIndependenceCore.FE7AndFE8TableSize),
                value => Assert.Equal((byte)0xFF, value));
        }
        finally { Exit(previous); }
    }

    [Fact]
    public void MakeIndependent_RecordsEachAmbientWriteOnlyOnce()
    {
        var rom = MakeRom();
        var previous = Enter(rom);
        try
        {
            uint slot = ClassBase + GetMoveCostPointerOffset(rom);
            var undo = CoreState.Undo.NewUndoData("Move cost single undo record");
            uint cloneOffset;
            using (ROM.BeginUndoScope(undo))
                cloneOffset = MoveCostIndependenceCore.MakeIndependent(
                    rom, ClassBase, slot, ClassCount, undo);

            Assert.NotEqual(U.NOT_FOUND, cloneOffset);
            Assert.Collection(
                undo.list,
                allocation =>
                {
                    Assert.Equal(cloneOffset, allocation.addr);
                    Assert.Equal(MoveCostIndependenceCore.FE7AndFE8TableSize, (uint)allocation.data.Length);
                },
                pointer =>
                {
                    Assert.Equal(slot, pointer.addr);
                    Assert.Equal(4u, (uint)pointer.data.Length);
                });
        }
        finally { Exit(previous); }
    }

    [Fact]
    public void AllocationFailure_DoesNotRepointSelectedClass()
    {
        var rom = MakeRom();
        var previous = Enter(rom);
        try
        {
            uint slot = ClassBase + GetMoveCostPointerOffset(rom);
            uint original = rom.u32(slot);
            CoreState.AppendBinaryData = (_, _) => U.NOT_FOUND;
            var undo = CoreState.Undo.NewUndoData("Move cost allocation failure");
            using (ROM.BeginUndoScope(undo))
                Assert.Equal(U.NOT_FOUND,
                    MoveCostIndependenceCore.MakeIndependent(rom, ClassBase, slot, ClassCount, undo));
            Assert.Equal(original, rom.u32(slot));
        }
        finally { Exit(previous); }
    }
}
