// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for SummonsDemonKingExpandCore (#1606): the Core-side list-expand for the
// Demon-King Summon editor. Grows the 20-byte summons_demon_king table AND writes
// the new entry count to summons_demon_king_count_address, under the caller's
// ambient undo scope, with the MapSettingCore #885 byte-identical fault restore.
//
// Synthetic FE8U/FE8J/FE7U/FE6 ROM bytes exercise IsEnabled gating + the expand
// mutation + the rollback / fault-restore atomicity without a real ROM.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class SummonsDemonKingExpandCoreTests
{
    const string FE8U_CODE = "BE8E01";
    const string FE8J_CODE = "BE8J01";
    const string FE7U_CODE = "AE7E01";
    const string FE6_CODE = "AFEJ01";

    // FE8U RomInfo addresses (ROMFE8U.cs:321-322).
    const uint FE8U_PointerAddr = 0x7B32Cu;   // summons_demon_king_pointer
    const uint FE8U_CountAddr = 0x7B2BCu;      // summons_demon_king_count_address

    // Table base: >= 0x200 (U.isSafetyOffset passes), well clear of the
    // pointer/count addresses, room for many 20-byte entries.
    const uint TableBase = 0x00200000u;
    const uint EntrySize = 20u;

    /// <summary>
    /// Build a synthetic FE8U ROM (16 MB so ROM.LoadLow assigns the FE8U RomInfo)
    /// with the pointer planted at summons_demon_king_pointer -> TableBase and the
    /// count byte at summons_demon_king_count_address. The table rows get a
    /// non-zero unit id so they look populated.
    /// </summary>
    static ROM MakeFe8U(byte countByte)
    {
        var bytes = new byte[0x01000000];

        // Pointer -> TableBase (GBA pointer).
        BitConverter.GetBytes(TableBase | 0x08000000u).CopyTo(bytes, (int)FE8U_PointerAddr);
        bytes[(int)FE8U_CountAddr] = countByte;

        // Fill enough rows with a non-zero unit id so EstimateEntryCount-style
        // scans see populated data.
        for (uint i = 0; i < 0x120; i++)
        {
            int row = (int)(TableBase + i * EntrySize);
            bytes[row + 0] = 0x01; // unit id
            bytes[row + 1] = 0x02; // class id
        }

        var rom = new ROM();
        bool ok = rom.LoadLow("synth-fe8u-1606.gba", bytes, FE8U_CODE);
        Assert.True(ok, "Synthetic FE8U ROM must be recognized by LoadLow");
        return rom;
    }

    static ROM MakeRom(string code)
    {
        var rom = new ROM();
        rom.LoadLow("synth-1606.gba", new byte[0x01000000], code);
        return rom;
    }

    // ------------------------------------------------------------------
    // IsEnabled gating.
    // ------------------------------------------------------------------

    [Fact]
    public void IsEnabled_Fe8U_True()
    {
        Assert.True(SummonsDemonKingExpandCore.IsEnabled(MakeFe8U(11)));
    }

    [Fact]
    public void IsEnabled_Fe8J_True()
    {
        // FE8J also defines the pointer/count; plant a valid pointer so the base
        // resolves safely.
        var rom = MakeRom(FE8J_CODE);
        uint ptr = rom.RomInfo.summons_demon_king_pointer;
        Assert.True(ptr != 0, "FE8J must define summons_demon_king_pointer");
        var bytes = rom.Data;
        BitConverter.GetBytes(TableBase | 0x08000000u).CopyTo(bytes, (int)ptr);
        rom.LoadLow("synth-fe8j-1606.gba", bytes, FE8J_CODE);
        Assert.True(SummonsDemonKingExpandCore.IsEnabled(rom));
    }

    [Fact]
    public void IsEnabled_Fe7U_False()
    {
        // FE7 leaves the pointer + count address at 0.
        var rom = MakeRom(FE7U_CODE);
        Assert.Equal(0u, rom.RomInfo.summons_demon_king_pointer);
        Assert.False(SummonsDemonKingExpandCore.IsEnabled(rom));
    }

    [Fact]
    public void IsEnabled_Fe6_False()
    {
        var rom = MakeRom(FE6_CODE);
        Assert.Equal(0u, rom.RomInfo.summons_demon_king_pointer);
        Assert.False(SummonsDemonKingExpandCore.IsEnabled(rom));
    }

    [Fact]
    public void IsEnabled_NullRom_False()
    {
        Assert.False(SummonsDemonKingExpandCore.IsEnabled(null!));
    }

    // ------------------------------------------------------------------
    // ReadCountByte.
    // ------------------------------------------------------------------

    [Fact]
    public void ReadCountByte_ReturnsCountByte()
    {
        Assert.Equal(11u, SummonsDemonKingExpandCore.ReadCountByte(MakeFe8U(11)));
    }

    [Fact]
    public void ReadCountByte_Fe6_ReturnsZero()
    {
        Assert.Equal(0u, SummonsDemonKingExpandCore.ReadCountByte(MakeRom(FE6_CODE)));
    }

    // ------------------------------------------------------------------
    // Expand — happy path: table grown + count byte written.
    // ------------------------------------------------------------------

    [Fact]
    public void Expand_GrowsTableAndWritesCountByte()
    {
        var rom = MakeFe8U(11);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = new Undo().NewUndoData("test", "expand");
            using (ROM.BeginUndoScope(undo))
            {
                var result = SummonsDemonKingExpandCore.Expand(rom, 15, undo, out string err);
                Assert.True(result.Success, err);
                Assert.Equal(15u, result.NewCountByte);
            }

            // Count byte updated.
            Assert.Equal(15u, rom.u8(FE8U_CountAddr));
            // Pointer repointed to the new base (not the old TableBase).
            uint newBase = rom.p32(FE8U_PointerAddr);
            Assert.NotEqual(TableBase, newBase);
            // The new table holds newCountByte + 1 = 16 rows; row 15 must be in
            // ROM bounds (and zero-filled by the expand).
            uint row15 = newBase + 15 * EntrySize;
            Assert.True(row15 + EntrySize <= (uint)rom.Data.Length);
            // Existing populated rows copied verbatim.
            Assert.Equal(0x01u, rom.u8(newBase));
            Assert.Equal(0x02u, rom.u8(newBase + 1));
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ------------------------------------------------------------------
    // Expand — rejection cases (ZERO mutation).
    // ------------------------------------------------------------------

    [Theory]
    [InlineData((byte)11, 100u)]  // new >= 100 (corrupt threshold)
    [InlineData((byte)11, 255u)]  // new way over max
    [InlineData((byte)11, 11u)]   // new == current (not greater)
    [InlineData((byte)11, 5u)]    // new < current
    public void Expand_RejectsInvalidNewCount_NoMutation(byte current, uint newCount)
    {
        var rom = MakeFe8U(current);
        byte[] before = (byte[])rom.Data.Clone();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = new Undo().NewUndoData("test", "reject");
            using (ROM.BeginUndoScope(undo))
            {
                var result = SummonsDemonKingExpandCore.Expand(rom, newCount, undo, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }
            Assert.Equal(before.Length, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Theory]
    [InlineData((byte)99)]   // already at max → cannot grow (new would be > 99)
    [InlineData((byte)100)]  // corrupt count
    [InlineData((byte)255)]  // corrupt count
    public void Expand_RejectsMaxOrCorruptCurrent_NoMutation(byte current)
    {
        var rom = MakeFe8U(current);
        byte[] before = (byte[])rom.Data.Clone();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = new Undo().NewUndoData("test", "reject-current");
            using (ROM.BeginUndoScope(undo))
            {
                // Try to grow to the largest valid count (99). For current==99 this
                // is not greater; for corrupt currents the corrupt guard fires.
                var result = SummonsDemonKingExpandCore.Expand(rom, 99, undo, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }
            Assert.Equal(before.Length, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ------------------------------------------------------------------
    // #885 byte-identical rollback — the FULL undo replay restores the ROM,
    // including across the resize-append path. Assert BOTH bytes AND length
    // (Copilot plan-review non-blocking note).
    // ------------------------------------------------------------------

    [Fact]
    public void Expand_ThenUndo_RestoresByteIdentical()
    {
        var rom = MakeFe8U(11);
        byte[] before = (byte[])rom.Data.Clone();
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = new Undo();
            var undoData = undo.NewUndoData("test", "expand-undo");
            using (ROM.BeginUndoScope(undoData))
            {
                var result = SummonsDemonKingExpandCore.Expand(rom, 50, undoData, out string err);
                Assert.True(result.Success, err);
            }
            // Push + replay the undo (mirrors UndoService.Rollback / RunUndo).
            undo.Push(undoData);
            undo.RunUndo();

            Assert.Equal(before.Length, rom.Data.Length);
            Assert.Equal(before, rom.Data);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ------------------------------------------------------------------
    // Fault restore — a forced ExpandTableTo failure (corrupt pointer slot)
    // leaves the ROM byte-identical and clears the caller's undo ranges.
    // ------------------------------------------------------------------

    [Fact]
    public void Expand_FailedExpand_RestoresByteIdentical_AndClearsUndo()
    {
        var rom = MakeFe8U(11);
        // Corrupt the pointer so ExpandTableTo's "table pointer invalid" / bounds
        // check fails AFTER IsEnabled (IsEnabled still passes because the base
        // resolves; we point it just past EOF so the copy/bounds check fails).
        // Use an out-of-bounds base that still passes isSafetyOffset on the slot
        // but makes ExpandTableTo fail its "extends beyond ROM bounds" guard.
        BitConverter.GetBytes((uint)(rom.Data.Length - 4) | 0x08000000u)
            .CopyTo(rom.Data, (int)FE8U_PointerAddr);
        byte[] before = (byte[])rom.Data.Clone();

        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undoData = new Undo().NewUndoData("test", "fault");
            using (ROM.BeginUndoScope(undoData))
            {
                var result = SummonsDemonKingExpandCore.Expand(rom, 50, undoData, out string err);
                Assert.False(result.Success);
                Assert.False(string.IsNullOrEmpty(err));
            }
            // The helper restored the ROM itself (byte + length) and cleared the
            // recorded undo ranges so a later caller Rollback is a no-op.
            Assert.Equal(before.Length, rom.Data.Length);
            Assert.Equal(before, rom.Data);
            Assert.Empty(undoData.list);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Expand_NullRom_Fails()
    {
        var dummy = MakeFe8U(11);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = dummy;
            var undoData = new Undo().NewUndoData("test", "null");
            var result = SummonsDemonKingExpandCore.Expand(null!, 15, undoData, out string err);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(err));
        }
        finally { CoreState.ROM = prevRom; }
    }
}
