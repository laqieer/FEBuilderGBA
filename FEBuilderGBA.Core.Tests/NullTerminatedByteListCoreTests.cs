using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="NullTerminatedByteListCore"/> — the id-neutral
    /// cross-platform Core helper for null-terminated 1-byte-ID lists (issue #926,
    /// #769 bucket 2 slice 1). All tests run on synthetic in-memory ROMs; the
    /// ROM-mutating tests set <see cref="CoreState.ROM"/> (Undo needs it) and
    /// restore it in a finally. Mirrors <c>ItemClassListCoreTests</c>.
    /// </summary>
    [Collection("SharedState")]
    public class NullTerminatedByteListCoreTests
    {
        readonly ITestOutputHelper _output;

        public NullTerminatedByteListCoreTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ---------------------------------------------------------------------
        // ScanByteList
        // ---------------------------------------------------------------------

        [Fact]
        public void ScanByteList_EmptyOnZeroAddress()
        {
            // baseAddr=0 is treated as a null pointer even when offset 0 holds data.
            byte[] data = new byte[64];
            data[0] = 0x10;
            data[1] = 0x20;
            data[2] = 0x00;
            var rom = MakeRom(data);
            var list = NullTerminatedByteListCore.ScanByteList(rom, baseAddr: 0);
            Assert.Empty(list);
        }

        [Fact]
        public void ScanByteList_TerminatesOnFirstZero()
        {
            byte[] data = new byte[64];
            data[4] = 0x10;
            data[5] = 0x20;
            data[6] = 0x30;
            data[7] = 0x00; // terminator
            data[8] = 0x40; // beyond terminator — must not be returned

            var rom = MakeRom(data);
            var list = NullTerminatedByteListCore.ScanByteList(rom, baseAddr: 4);
            Assert.Equal(3, list.Count);
            Assert.Equal(0x10u, list[0]);
            Assert.Equal(0x20u, list[1]);
            Assert.Equal(0x30u, list[2]);
        }

        [Fact]
        public void ScanByteList_EmptyOnOutOfBounds()
        {
            byte[] data = new byte[16];
            for (int i = 0; i < data.Length; i++) data[i] = 0x42;
            var rom = MakeRom(data);
            // baseAddr beyond the ROM => empty list, no throw.
            var list = NullTerminatedByteListCore.ScanByteList(rom, baseAddr: 0x1000);
            Assert.Empty(list);
        }

        [Fact]
        public void ScanByteList_HandlesBoundsSafely()
        {
            // Array that fills the entire ROM and never hits a terminator.
            byte[] data = new byte[8];
            for (int i = 0; i < data.Length; i++) data[i] = 0x42;
            var rom = MakeRom(data);
            var list = NullTerminatedByteListCore.ScanByteList(rom, baseAddr: 1);
            Assert.Equal(7, list.Count); // 7 bytes from offset 1 to end of ROM
        }

        [Fact]
        public void ScanByteList_HonorsIterationCap()
        {
            // A ROM larger than the 0x200 cap with no terminator. ScanByteList must
            // return at most 0x200 entries (not run to the end of a huge ROM).
            byte[] data = new byte[0x400];
            for (int i = 0; i < data.Length; i++) data[i] = 0x55;
            var rom = MakeRom(data);
            var list = NullTerminatedByteListCore.ScanByteList(rom, baseAddr: 1);
            Assert.Equal(0x200, list.Count);
        }

        // ---------------------------------------------------------------------
        // WriteByte
        // ---------------------------------------------------------------------

        [Fact]
        public void WriteByte_UpdatesByteAndUndoRestoresExactly()
        {
            byte[] data = new byte[16];
            data[3] = 0x11; // original value
            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom; // Undo needs CoreState.ROM
            try
            {
                var undo = NewUndo(rom);

                NullTerminatedByteListCore.WriteByte(rom, addr: 3, value: 0x55, undo: undo);

                Assert.Equal(0x55, rom.Data[3]);
                Assert.Single(undo.list);
                Assert.Equal(3u, undo.list[0].addr);
                Assert.Equal(1, undo.list[0].data.Length);

                // Undo restores the byte exactly.
                RollbackAll(rom, undo);
                Assert.Equal(0x11, rom.Data[3]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // WriteByteList
        // ---------------------------------------------------------------------

        [Fact]
        public void WriteByteList_WritesExactListWithSingleTerminator()
        {
            // B1 guard: a 3-entry list must scan back as EXACTLY 3 (no interior 0x00,
            // no placeholder/zero-fill heuristic).
            byte[] data = MakeRomWithListAt(out uint pointerAddr, out _, 64, new uint[] { 0x10, 0x20 });
            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                uint newBase = NullTerminatedByteListCore.WriteByteList(
                    rom, pointerAddr, new uint[] { 0xAA, 0xBB, 0xCC }, undo);

                Assert.NotEqual(0u, newBase);
                Assert.NotEqual(U.NOT_FOUND, newBase);

                // Pointer repointed to the new array.
                Assert.Equal(newBase | 0x08000000u, rom.u32(pointerAddr));

                // Exact bytes: [0xAA, 0xBB, 0xCC, 0x00].
                Assert.Equal(0xAA, rom.Data[newBase + 0]);
                Assert.Equal(0xBB, rom.Data[newBase + 1]);
                Assert.Equal(0xCC, rom.Data[newBase + 2]);
                Assert.Equal(0x00, rom.Data[newBase + 3]);

                // Scan returns EXACTLY 3.
                var scanned = NullTerminatedByteListCore.ScanByteList(rom, newBase);
                Assert.Equal(3, scanned.Count);
                Assert.Equal(0xAAu, scanned[0]);
                Assert.Equal(0xBBu, scanned[1]);
                Assert.Equal(0xCCu, scanned[2]);

                // OLD bytes preserved at old base 64: [0x10, 0x20, 0x00].
                Assert.Equal(0x10, rom.Data[64]);
                Assert.Equal(0x20, rom.Data[65]);
                Assert.Equal(0x00, rom.Data[66]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void WriteByteList_EmptyListWritesTerminatorOnly()
        {
            byte[] data = MakeRomWithListAt(out uint pointerAddr, out _, 64, new uint[] { 0x10, 0x20, 0x30 });
            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                uint newBase = NullTerminatedByteListCore.WriteByteList(
                    rom, pointerAddr, Array.Empty<uint>(), undo);

                Assert.Equal(0x00, rom.Data[newBase]); // terminator-only
                var scanned = NullTerminatedByteListCore.ScanByteList(rom, newBase);
                Assert.Empty(scanned);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void WriteByteList_ReorderAndReplaceRoundTrips()
        {
            byte[] data = MakeRomWithListAt(out uint pointerAddr, out _, 64, new uint[] { 0x01, 0x02, 0x03 });
            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                // Reorder + replace one element.
                uint newBase = NullTerminatedByteListCore.WriteByteList(
                    rom, pointerAddr, new uint[] { 0x03, 0x99, 0x01 }, undo);

                var scanned = NullTerminatedByteListCore.ScanByteList(rom, newBase);
                Assert.Equal(new List<uint> { 0x03u, 0x99u, 0x01u }, scanned);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void WriteByteList_RejectsListLongerThan255()
        {
            byte[] data = MakeRomWithListAt(out uint pointerAddr, out _, 64, new uint[] { 0x10 });
            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);
                var tooLong = new uint[0x100];
                Assert.Throws<ArgumentException>(() =>
                    NullTerminatedByteListCore.WriteByteList(rom, pointerAddr, tooLong, undo));
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // ExpandByteList
        // ---------------------------------------------------------------------

        [Fact]
        public void ExpandByteList_AppendsPlaceholderBeforeTerminator()
        {
            byte[] data = MakeRomWithListAt(out uint pointerAddr, out _, 32, new uint[] { 0x10, 0x20 });
            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                uint newBase = NullTerminatedByteListCore.ExpandByteList(rom, pointerAddr, undo);

                Assert.NotEqual(0u, newBase);
                Assert.NotEqual(U.NOT_FOUND, newBase);
                Assert.NotEqual(32u, newBase); // relocated

                // Pointer repointed.
                Assert.Equal(newBase | 0x08000000u, rom.u32(pointerAddr));

                // Layout: [0x10, 0x20, placeholder, 0x00].
                Assert.Equal(0x10, rom.Data[newBase + 0]);
                Assert.Equal(0x20, rom.Data[newBase + 1]);
                Assert.Equal((byte)NullTerminatedByteListCore.NewSlotPlaceholder, rom.Data[newBase + 2]);
                Assert.Equal(0x00, rom.Data[newBase + 3]);

                // Scan shows the new placeholder row (count = oldCount + 1).
                var scanned = NullTerminatedByteListCore.ScanByteList(rom, newBase);
                Assert.Equal(3, scanned.Count);
                Assert.Equal(0x10u, scanned[0]);
                Assert.Equal(0x20u, scanned[1]);
                Assert.Equal(NullTerminatedByteListCore.NewSlotPlaceholder, scanned[2]);

                // OLD bytes preserved.
                Assert.Equal(0x10, rom.Data[32]);
                Assert.Equal(0x20, rom.Data[33]);
                Assert.Equal(0x00, rom.Data[34]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void ExpandByteList_OnNullOwnerPointer_AllocatesFreshList()
        {
            // FE8N Ver2/Ver3 per-skill sub-list pointers (P4/P8/P12/P16/P20) are 0
            // until the skill grows its first entry. ExpandByteList must treat a NULL
            // owner pointer as an UNSET/empty list and allocate a fresh
            // [placeholder, 0x00] list (mirrors WriteByteList) rather than throwing.
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF; // free space everywhere
            WritePtr(data, 0, 0u); // owner pointer slot is NULL

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                uint newBase = 0;
                var ex = Record.Exception(() =>
                    newBase = NullTerminatedByteListCore.ExpandByteList(rom, pointerAddr: 0, undo: undo));
                Assert.Null(ex); // does NOT throw on a null owner pointer

                Assert.NotEqual(0u, newBase);
                Assert.NotEqual(U.NOT_FOUND, newBase);

                // Slot now points to the fresh array.
                Assert.Equal(newBase | 0x08000000u, rom.u32(0));

                // Fresh layout: [placeholder, 0x00] — one placeholder entry + terminator.
                Assert.Equal((byte)NullTerminatedByteListCore.NewSlotPlaceholder, rom.Data[newBase + 0]);
                Assert.Equal(0x00, rom.Data[newBase + 1]); // terminator present

                // ScanByteList returns exactly [0x01] (count 1).
                var scanned = NullTerminatedByteListCore.ScanByteList(rom, newBase);
                Assert.Single(scanned);
                Assert.Equal(NullTerminatedByteListCore.NewSlotPlaceholder, scanned[0]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void ExpandByteList_OnGarbageNonNullPointer_Throws()
        {
            // A NON-zero owner pointer that is not a ROM address is genuine garbage and
            // must still throw (the null relaxation does not weaken this guard).
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            WritePtr(data, 0, 0x12345678u); // non-zero, below 0x08000000 => not a pointer

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);
                Assert.Throws<InvalidOperationException>(() =>
                    NullTerminatedByteListCore.ExpandByteList(rom, pointerAddr: 0, undo: undo));
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // Shared-array preservation (implicit fork)
        // ---------------------------------------------------------------------

        [Fact]
        public void ExpandByteList_OnSharedArray_OtherOwnerUnchanged()
        {
            // Two owner pointers (offsets 0 and 8) reference the same array at 96.
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            data[96] = 0x42;
            data[97] = 0x00; // [0x42, 0]
            uint sharedPtr = 96u | 0x08000000u;
            WritePtr(data, 0, sharedPtr);
            WritePtr(data, 8, sharedPtr);

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                uint newBaseA = NullTerminatedByteListCore.ExpandByteList(rom, pointerAddr: 0, undo: undo);

                // Owner A scans [0x42, placeholder].
                var scanA = NullTerminatedByteListCore.ScanByteList(rom, newBaseA);
                Assert.Equal(2, scanA.Count);
                Assert.Equal(0x42u, scanA[0]);
                Assert.Equal(NullTerminatedByteListCore.NewSlotPlaceholder, scanA[1]);

                // Owner B's pointer + scan unchanged (implicit fork).
                Assert.Equal(sharedPtr, rom.u32(8));
                var scanB = NullTerminatedByteListCore.ScanByteList(rom, 96);
                Assert.Single(scanB);
                Assert.Equal(0x42u, scanB[0]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void WriteByteList_OnSharedArray_OtherOwnerUnchanged()
        {
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            data[64] = 0x10;
            data[65] = 0x20;
            data[66] = 0x00; // [0x10, 0x20, 0]
            uint sharedPtr = 64u | 0x08000000u;
            WritePtr(data, 0, sharedPtr);
            WritePtr(data, 8, sharedPtr);

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                NullTerminatedByteListCore.WriteByteList(rom, pointerAddr: 0, new uint[] { 0x77 }, undo);

                // Owner B unchanged.
                Assert.Equal(sharedPtr, rom.u32(8));
                var scanB = NullTerminatedByteListCore.ScanByteList(rom, 64);
                Assert.Equal(2, scanB.Count);
                Assert.Equal(0x10u, scanB[0]);
                Assert.Equal(0x20u, scanB[1]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // MakeIndependentCopy
        // ---------------------------------------------------------------------

        [Fact]
        public void MakeIndependentCopy_RelocatesAndRepointsOwnerOnly()
        {
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            data[64] = 0x10;
            data[65] = 0x20;
            data[66] = 0x30;
            data[67] = 0x00;
            uint sharedPtr = 64u | 0x08000000u;
            WritePtr(data, 0, sharedPtr); // owner 1
            WritePtr(data, 4, sharedPtr); // owner 2

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                uint newBase = NullTerminatedByteListCore.MakeIndependentCopy(
                    rom, sourceAddr: 64, ownerPointerAddr: 0, undo: undo);

                Assert.NotEqual(0u, newBase);
                Assert.NotEqual(U.NOT_FOUND, newBase);
                Assert.NotEqual(64u, newBase);

                // Owner 1 repointed; owner 2 still on the shared original.
                Assert.Equal(newBase | 0x08000000u, rom.u32(0));
                Assert.Equal(sharedPtr, rom.u32(4));

                // New copy equals the source (incl terminator).
                Assert.Equal(0x10, rom.Data[newBase + 0]);
                Assert.Equal(0x20, rom.Data[newBase + 1]);
                Assert.Equal(0x30, rom.Data[newBase + 2]);
                Assert.Equal(0x00, rom.Data[newBase + 3]);

                // Source untouched.
                Assert.Equal(0x10, rom.Data[64]);
                Assert.Equal(0x20, rom.Data[65]);
                Assert.Equal(0x30, rom.Data[66]);
                Assert.Equal(0x00, rom.Data[67]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // Alloc-failure path: throws + undo restores original length + bytes
        // ---------------------------------------------------------------------

        [Fact]
        public void WriteByteList_ThrowsWhenNoFreeSpace_UndoRestores()
        {
            // Fill the whole ROM with non-free bytes so FindFreeSpace cannot satisfy
            // any allocation. The list at 8 has a real terminator so the scan/realloc
            // path runs but the alloc fails.
            byte[] data = new byte[64];
            for (int i = 0; i < data.Length; i++) data[i] = 0x55; // no 0xFF/0x00 free runs
            uint listPtr = 8u | 0x08000000u;
            WritePtr(data, 0, listPtr);
            data[8] = 0x12;
            data[9] = 0x00; // terminator so ScanByteList is well-formed

            var rom = MakeRom(data);
            byte[] before = (byte[])rom.Data.Clone();
            int beforeLen = rom.Data.Length;
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                Assert.Throws<InvalidOperationException>(() =>
                    NullTerminatedByteListCore.WriteByteList(rom, pointerAddr: 0, new uint[] { 0xAA, 0xBB }, undo));

                // Roll back whatever partial writes were recorded.
                RollbackAll(rom, undo);
                Assert.Equal(beforeLen, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void ExpandByteList_ThrowsWhenNoFreeSpace_UndoRestores()
        {
            byte[] data = new byte[64];
            for (int i = 0; i < data.Length; i++) data[i] = 0x55;
            uint listPtr = 8u | 0x08000000u;
            WritePtr(data, 0, listPtr);
            data[8] = 0x12;
            data[9] = 0x00;

            var rom = MakeRom(data);
            byte[] before = (byte[])rom.Data.Clone();
            int beforeLen = rom.Data.Length;
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = NewUndo(rom);

                Assert.Throws<InvalidOperationException>(() =>
                    NullTerminatedByteListCore.ExpandByteList(rom, pointerAddr: 0, undo: undo));

                RollbackAll(rom, undo);
                Assert.Equal(beforeLen, rom.Data.Length);
                Assert.Equal(before, rom.Data);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>Create a minimal in-memory ROM wrapping a synthetic buffer.</summary>
        static ROM MakeRom(byte[] data)
        {
            var rom = new ROM();
            typeof(ROM).GetProperty("Data")!.SetValue(rom, data);
            return rom;
        }

        static Undo.UndoData NewUndo(ROM rom) => new Undo.UndoData
        {
            name = "test",
            list = new List<Undo.UndoPostion>(),
            filesize = (uint)rom.Data.Length,
        };

        /// <summary>Write a GBA pointer little-endian into <paramref name="data"/>.</summary>
        static void WritePtr(byte[] data, int at, uint gbaPtr)
        {
            for (int i = 0; i < 4; i++)
                data[at + i] = (byte)((gbaPtr >> (i * 8)) & 0xFF);
        }

        /// <summary>
        /// Build a 1KB 0xFF-filled ROM with an owner pointer at offset 0 referencing
        /// a null-terminated list at <paramref name="listOffset"/> holding
        /// <paramref name="ids"/>. Returns the buffer; out-params expose the pointer
        /// address (0) and the list base offset.
        /// </summary>
        static byte[] MakeRomWithListAt(out uint pointerAddr, out uint listBase, uint listOffset, uint[] ids)
        {
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            for (int i = 0; i < ids.Length; i++) data[listOffset + i] = (byte)(ids[i] & 0xFF);
            data[listOffset + (uint)ids.Length] = 0x00; // terminator
            WritePtr(data, 0, listOffset | 0x08000000u);
            pointerAddr = 0;
            listBase = listOffset;
            return data;
        }

        /// <summary>
        /// Roll back every recorded write by restoring each position's captured
        /// prior bytes, in REVERSE order (last write undone first). Each
        /// <see cref="Undo.UndoPostion"/> records the bytes that were present BEFORE
        /// the corresponding <c>write_*</c> ran, so replaying them in reverse
        /// reconstructs the original ROM (mirrors what <c>Undo.Patch</c> does for a
        /// single record, without needing the private method or a full Undo buffer).
        /// </summary>
        static void RollbackAll(ROM rom, Undo.UndoData undo)
        {
            for (int i = undo.list.Count - 1; i >= 0; i--)
            {
                var p = undo.list[i];
                rom.write_range(p.addr, p.data);
            }
        }
    }
}
