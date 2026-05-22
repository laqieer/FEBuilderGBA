// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ImageBattleBGCore — the Core-side extraction of
// ImageBattleBGForm's list-expansion + X_REF terrain-lookup helpers (#434).
//
// These tests construct synthetic FE8U ROM bytes so we can exercise the
// pointer-table relocation + row-preservation + row[0]-fill + undo-rollback
// behaviors of `ExpandList` and the cross-reference build of
// `MakeListByUseTerrain` without needing a real ROM file on disk.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class ImageBattleBGCoreTests
{
    /// <summary>
    /// Create a fresh UndoData instance for tests. Uses the same shape
    /// as `AmbientUndoTests` and `Undo.NewUndoData` callers.
    /// </summary>
    static Undo.UndoData NewUndo(string name = "test") => new Undo.UndoData
    {
        time = System.DateTime.Now,
        name = name,
        list = new System.Collections.Generic.List<Undo.UndoPostion>(),
        filesize = 0,
    };

    /// <summary>
    /// Build a tiny synthetic FE8U ROM with a small Battle BG pointer table
    /// at a known free address, and the `battle_bg_pointer` slot pointing
    /// to it. Each entry is 12 bytes: u32 image / u32 tsa / u32 palette,
    /// all valid GBA pointers so the WF `is_data_exists_callback` would
    /// consider the slot allocated.
    ///
    /// rowCount entries are laid out, each with a distinguishing byte at
    /// offset 0 so the test can detect which row was copied where.
    ///
    /// Also sets <c>CoreState.ROM</c> to the constructed ROM — necessary
    /// for the undo-snapshotting path inside <see cref="Undo.UndoPostion(uint, uint)"/>
    /// which reads from <c>CoreState.ROM</c>. Callers should save and
    /// restore the previous value via the returned helper.
    /// </summary>
    static ROM MakeFe8uWithBgTable(int rowCount, uint tableAddr = 0x00800000u)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");

        uint pointerSlot = rom.RomInfo.battle_bg_pointer;
        Assert.True(pointerSlot != 0, "ROMFE8U must define battle_bg_pointer");

        // Lay out rowCount entries at tableAddr — each entry is 12 bytes:
        //   +0 image_ptr (u32, GBA-format pointer)
        //   +4 tsa_ptr   (u32, GBA-format pointer)
        //   +8 pal_ptr   (u32, GBA-format pointer)
        // We seed each row's image_ptr to be `0x08400000 | (i << 12)` so it
        // is a valid GBA pointer AND distinct per row — easy to detect.
        for (int i = 0; i < rowCount; i++)
        {
            uint rowBase = tableAddr + (uint)(i * 12);
            uint imgPtr = 0x08400000u | ((uint)i << 12);
            uint tsaPtr = 0x08500000u | ((uint)i << 12);
            uint palPtr = 0x08600000u | ((uint)i << 12);
            BitConverter.GetBytes(imgPtr).CopyTo(bytes, rowBase + 0);
            BitConverter.GetBytes(tsaPtr).CopyTo(bytes, rowBase + 4);
            BitConverter.GetBytes(palPtr).CopyTo(bytes, rowBase + 8);
        }

        // Plant a terminator after rowCount entries — a 0/0 pair so the WF
        // is-data-exists callback stops there.
        if (rowCount > 0)
        {
            uint termAddr = tableAddr + (uint)(rowCount * 12);
            BitConverter.GetBytes(0u).CopyTo(bytes, termAddr + 0);
            BitConverter.GetBytes(0u).CopyTo(bytes, termAddr + 4);
        }

        // Point battle_bg_pointer at the table (GBA-format pointer).
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, pointerSlot);

        // Re-load so ROM caches the data.
        rom.LoadLow("synthetic-fe8u.gba", bytes, "BE8E01");
        return rom;
    }

    // -----------------------------------------------------------------
    // ExpandList — pointer repoint + row preservation + row[0] fill
    // -----------------------------------------------------------------

    [Fact]
    public void ExpandList_RepointsBattleBgPointer_ToNewBase()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            // `rom.p32(addr)` returns the offset (GBA pointer with the
            // 0x08000000 base stripped) — same shape `ImageBattleBGCore
            // .ExpandList` returns. Compare offset-vs-offset (Copilot
            // bot review on PR #513 — the original assertion compared
            // origPointer offset against `newBase | 0x08000000u` and
            // always passed because the operands were in different
            // formats).
            uint origPointer = rom.p32(rom.RomInfo.battle_bg_pointer);

            var undo = NewUndo();
            uint newBase = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);

            Assert.NotEqual(U.NOT_FOUND, newBase);
            // Repoint check — both values are offsets.
            Assert.NotEqual(origPointer, newBase);

            uint finalPointer = rom.p32(rom.RomInfo.battle_bg_pointer);
            Assert.Equal(newBase, finalPointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_PreservesOldRows_ByteForByte()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint origBase = rom.p32(rom.RomInfo.battle_bg_pointer);

            // Capture the original 4 rows BEFORE expansion.
            byte[] origRows = new byte[4 * 12];
            Array.Copy(rom.Data, origBase, origRows, 0, 4 * 12);

            var undo = NewUndo();
            uint newBase = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // The first 4 rows at the new base must match byte-for-byte.
            for (int i = 0; i < 4 * 12; i++)
            {
                Assert.Equal(origRows[i], rom.Data[newBase + i]);
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_NewRows_AreClonesOfRow0()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint origBase = rom.p32(rom.RomInfo.battle_bg_pointer);

            byte[] row0 = new byte[12];
            Array.Copy(rom.Data, origBase, row0, 0, 12);

            var undo = NewUndo();
            uint newBase = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // Rows 4..9 (the new ones) must each equal row0 byte-for-byte.
            for (int rowIdx = 4; rowIdx < 10; rowIdx++)
            {
                for (int byteIdx = 0; byteIdx < 12; byteIdx++)
                {
                    Assert.Equal(
                        row0[byteIdx],
                        rom.Data[newBase + rowIdx * 12 + byteIdx]);
                }
            }
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_RejectsNewCountAbove255()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();

            // 256 is the first out-of-range value — the WF
            // `AddressListExpandsButton_255` button caps at 255 inclusive
            // (the suffix is the max, not the limit).
            uint result = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 256, undo);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_AcceptsNewCount255_AtLimit()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();

            uint result = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 255, undo);
            Assert.NotEqual(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_RejectsShrinkRequest()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 10);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();

            // ExpandList is one-way — caller should never shrink. Helper
            // returns NOT_FOUND rather than silently truncating.
            uint result = ImageBattleBGCore.ExpandList(rom, oldCount: 10, newCount: 5, undo);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_RejectsZeroOldCount()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 0);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();

            // Zero rows means no row[0] to clone — refuse the operation.
            uint result = ImageBattleBGCore.ExpandList(rom, oldCount: 0, newCount: 5, undo);
            Assert.Equal(U.NOT_FOUND, result);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ExpandList_UndoData_RestoresOriginalPointerOnRollback()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint origPointer = rom.p32(rom.RomInfo.battle_bg_pointer);
            uint pointerSlot = rom.RomInfo.battle_bg_pointer;
            // Snapshot the original pointer-slot bytes so we can compare
            // after rollback.
            byte[] origPointerBytes = rom.getBinaryData(pointerSlot, 4);

            var undo = NewUndo();
            uint newBase = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);
            Assert.NotEqual(U.NOT_FOUND, newBase);

            // Sanity: pointer was repointed.
            Assert.NotEqual(origPointer, rom.p32(pointerSlot));

            // Verify the undo data records the pointer write so a rollback
            // restores the original state.
            Assert.NotEmpty(undo.list);
            bool foundPointerWrite = false;
            foreach (var pos in undo.list)
            {
                if (pos.addr == pointerSlot && pos.data != null && pos.data.Length >= 4)
                {
                    foundPointerWrite = true;
                    break;
                }
            }
            Assert.True(foundPointerWrite,
                "Undo data must include the battle_bg_pointer write");

            // Actually perform the rollback and verify the pointer is
            // restored to its pre-expansion value (Copilot bot review on
            // PR #513 — the original test only asserted the undo list
            // entry existed but never exercised the rollback path).
            for (int i = undo.list.Count - 1; i >= 0; i--)
            {
                var pos = undo.list[i];
                if (pos.data == null) continue;
                for (int b = 0; b < pos.data.Length; b++)
                {
                    rom.write_u8(pos.addr + (uint)b, pos.data[b]);
                }
            }
            // After replaying the undo, the pointer must match the original
            // value byte-for-byte.
            byte[] restoredBytes = rom.getBinaryData(pointerSlot, 4);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(origPointerBytes[i], restoredBytes[i]);
            }
            Assert.Equal(origPointer, rom.p32(pointerSlot));
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Regression test for Copilot CLI re-review on PR #513: the
    /// ambient-scope variant must NOT double-snapshot writes when the
    /// caller has already opened a `ROM.BeginUndoScope`. Each ROM write
    /// inside the helper should produce exactly one `UndoPostion` entry
    /// in the active ambient UndoData, not two.
    /// </summary>
    [Fact]
    public void ExpandList_AmbientScope_DoesNotDoubleSnapshotWrites()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();
            // Open the ambient scope, then invoke the parameterless
            // overload — what the View handler does after
            // `_undoService.Begin(...)`.
            using (ROM.BeginUndoScope(undo))
            {
                uint result = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 10);
                Assert.NotEqual(U.NOT_FOUND, result);
            }

            // Count how many undo entries cover the new pointer slot —
            // there must be EXACTLY one for the pointer write. (The
            // previous explicit-overload chain produced 2 because both
            // the explicit and ambient layers appended.)
            uint pointerSlot = rom.RomInfo.battle_bg_pointer;
            int pointerEntries = 0;
            foreach (var pos in undo.list)
            {
                if (pos.addr == pointerSlot)
                    pointerEntries++;
            }
            Assert.Equal(1, pointerEntries);
        }
        finally { CoreState.ROM = prevRom; }
    }

    /// <summary>
    /// Regression mirror of the above for the explicit-undo
    /// compatibility overload: when the supplied UndoData IS the
    /// active ambient one, the helper must NOT double-snapshot
    /// (the explicit overload now detects the alias and dispatches
    /// to the parameterless variant — Copilot CLI re-review on PR #513).
    /// </summary>
    [Fact]
    public void ExpandList_ExplicitUndoAliasingAmbient_DoesNotDoubleSnapshot()
    {
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undo = NewUndo();
            using (ROM.BeginUndoScope(undo))
            {
                // Caller passes the SAME UndoData via the explicit
                // overload AND opened an ambient scope around it — the
                // helper should not double-append.
                uint result = ImageBattleBGCore.ExpandList(rom, oldCount: 4, newCount: 10, undo);
                Assert.NotEqual(U.NOT_FOUND, result);
            }

            uint pointerSlot = rom.RomInfo.battle_bg_pointer;
            int pointerEntries = 0;
            foreach (var pos in undo.list)
            {
                if (pos.addr == pointerSlot)
                    pointerEntries++;
            }
            Assert.Equal(1, pointerEntries);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // -----------------------------------------------------------------
    // MakeListByUseTerrain — cross-reference build
    // -----------------------------------------------------------------

    [Fact]
    public void MakeListByUseTerrain_ReturnsEmptyForUnusedTerrain()
    {
        // With no terrain table seeded, the helper returns an empty list.
        ROM rom = MakeFe8uWithBgTable(rowCount: 4);
        var result = ImageBattleBGCore.MakeListByUseTerrain(rom, terrainId: 99);
        Assert.NotNull(result);
        // No usage rows — empty list is the expected outcome for a
        // synthetic ROM that has no terrain-lookup table populated.
        Assert.Empty(result);
    }

    [Fact]
    public void MakeListByUseTerrain_HandlesNullSafely()
    {
        // The helper must not throw on a ROM without RomInfo or with
        // a 0 pointer slot — return an empty list instead.
        var result = ImageBattleBGCore.MakeListByUseTerrain(null, terrainId: 0);
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
