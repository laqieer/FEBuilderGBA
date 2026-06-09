using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class DataExpansionCoreTests
    {
        /// <summary>Helper: build a minimal ROM with LoadLow using ROMFE0 ("NAZO").</summary>
        private static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            // Fill with 0x00 by default; tests will set up 0xFF free regions.
            rom.LoadLow("test.gba", data, "NAZO");
            return rom;
        }

        /// <summary>Write a GBA pointer (offset + 0x08000000) at the given ROM address.</summary>
        private static void WritePointer(ROM rom, uint addr, uint offset)
        {
            uint gbaPtr = offset + 0x08000000;
            rom.Data[addr + 0] = (byte)(gbaPtr & 0xFF);
            rom.Data[addr + 1] = (byte)((gbaPtr >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((gbaPtr >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((gbaPtr >> 24) & 0xFF);
        }

        // ────────────────────────────────────────────────
        // FindFreeSpace
        // ────────────────────────────────────────────────

        [Fact]
        public void FindFreeSpace_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(null, 16));
        }

        [Fact]
        public void FindFreeSpace_ZeroSize_ReturnsNotFound()
        {
            var rom = MakeRom(256);
            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(rom, 0));
        }

        [Fact]
        public void FindFreeSpace_SizeExceedsRom_ReturnsNotFound()
        {
            var rom = MakeRom(256);
            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(rom, 0x1000000));
        }

        [Fact]
        public void FindFreeSpace_FindsFF_Region()
        {
            var rom = MakeRom(0x200000);
            // Place a 64-byte 0xFF region at 0x100100 (4-byte aligned)
            for (int i = 0; i < 64; i++)
                rom.Data[0x100100 + i] = 0xFF;

            uint result = DataExpansionCore.FindFreeSpace(rom, 32, 0x100000);
            Assert.NotEqual(U.NOT_FOUND, result);
            Assert.True(result >= 0x100100 && result <= 0x100100 + 32);
            // Verify 4-byte alignment
            Assert.Equal(0u, result % 4);
        }

        [Fact]
        public void FindFreeSpace_SkipsNonFF_Bytes()
        {
            var rom = MakeRom(0x200000);
            // Put some non-FF at the start of the search region
            rom.Data[0x100000] = 0xFF;
            rom.Data[0x100001] = 0xFF;
            rom.Data[0x100002] = 0x42; // break

            // Put real free space later
            for (int i = 0; i < 32; i++)
                rom.Data[0x100100 + i] = 0xFF;

            uint result = DataExpansionCore.FindFreeSpace(rom, 16, 0x100000);
            Assert.NotEqual(U.NOT_FOUND, result);
            Assert.True(result >= 0x100100);
        }

        [Fact]
        public void FindFreeSpace_NoFreeRegion_ReturnsNotFound()
        {
            // ROM filled with 0x00 (not 0xFF) and we search for FF
            var rom = MakeRom(512);
            // Fill everything with non-FF/non-00 to be safe
            for (int i = 0; i < rom.Data.Length; i++)
                rom.Data[i] = 0x42;

            Assert.Equal(U.NOT_FOUND, DataExpansionCore.FindFreeSpace(rom, 16, 0));
        }

        // ────────────────────────────────────────────────
        // GetTableInfo
        // ────────────────────────────────────────────────

        [Fact]
        public void GetTableInfo_NullRom_ReturnsNull()
        {
            Assert.Null(DataExpansionCore.GetTableInfo(null, 0, 4));
        }

        [Fact]
        public void GetTableInfo_ZeroEntrySize_ReturnsNull()
        {
            var rom = MakeRom(256);
            Assert.Null(DataExpansionCore.GetTableInfo(rom, 0, 0));
        }

        [Fact]
        public void GetTableInfo_InvalidPointer_ReturnsNull()
        {
            var rom = MakeRom(256);
            // Pointer at 0x00 is all zeros → resolved offset is 0 via toOffset → invalid
            Assert.Null(DataExpansionCore.GetTableInfo(rom, 0, 4));
        }

        [Fact]
        public void GetTableInfo_ValidTable_ReturnsCorrectInfo()
        {
            var rom = MakeRom(0x1000);
            uint tableBase = 0x100;
            uint entrySize = 8;
            uint pointerAddr = 0x10;

            // Write pointer at pointerAddr → tableBase
            WritePointer(rom, pointerAddr, tableBase);

            // Write 3 non-zero entries, then a zero entry (terminator)
            for (int entry = 0; entry < 3; entry++)
            {
                uint addr = (uint)(tableBase + entry * entrySize);
                rom.Data[addr] = (byte)(entry + 1); // non-zero first byte
            }
            // Entry 3 is all zeros (terminator) — already 0x00 by default

            var info = DataExpansionCore.GetTableInfo(rom, pointerAddr, entrySize);
            Assert.NotNull(info);
            Assert.Equal(tableBase, info.BaseAddress);
            Assert.Equal(3u, info.EstimatedCount);
        }

        [Fact]
        public void GetTableInfo_AllZeroFirstEntry_CountIsZero()
        {
            var rom = MakeRom(0x1000);
            uint tableBase = 0x100;
            uint pointerAddr = 0x10;

            WritePointer(rom, pointerAddr, tableBase);
            // First entry is all zeros → count = 0

            var info = DataExpansionCore.GetTableInfo(rom, pointerAddr, 8);
            Assert.NotNull(info);
            Assert.Equal(0u, info.EstimatedCount);
        }

        // ────────────────────────────────────────────────
        // EstimateEntryCount
        // ────────────────────────────────────────────────

        [Fact]
        public void EstimateEntryCount_StopsAtZeroEntry()
        {
            var rom = MakeRom(256);
            uint baseAddr = 0x10;
            uint entrySize = 4;

            // 2 non-zero entries
            rom.Data[0x10] = 0x01;
            rom.Data[0x14] = 0x02;
            // 0x18 is all zeros → terminator

            uint count = DataExpansionCore.EstimateEntryCount(rom, baseAddr, entrySize);
            Assert.Equal(2u, count);
        }

        [Fact]
        public void EstimateEntryCount_StopsAtEndOfRom()
        {
            var rom = MakeRom(32);
            // Fill everything with non-zero
            for (int i = 0; i < rom.Data.Length; i++)
                rom.Data[i] = 0x42;

            uint count = DataExpansionCore.EstimateEntryCount(rom, 0, 4);
            Assert.Equal((uint)(rom.Data.Length / 4), count);
        }

        // ────────────────────────────────────────────────
        // ExpandTable
        // ────────────────────────────────────────────────

        [Fact]
        public void ExpandTable_NullRom_Fails()
        {
            var result = DataExpansionCore.ExpandTable(null, 0, 8, 1);
            Assert.False(result.Success);
            Assert.Contains("null", result.Error);
        }

        [Fact]
        public void ExpandTable_ZeroEntrySize_Fails()
        {
            var rom = MakeRom(256);
            var result = DataExpansionCore.ExpandTable(rom, 0, 0, 1);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTable_InvalidPointer_Fails()
        {
            var rom = MakeRom(256);
            var result = DataExpansionCore.ExpandTable(rom, 0, 8, 1);
            Assert.False(result.Success);
            Assert.Contains("invalid", result.Error.ToLower());
        }

        [Fact]
        public void ExpandTable_CopiesDataAndAddsEntry()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint entryCount = 3;

            // Set up the pointer
            WritePointer(rom, pointerAddr, tableBase);

            // Write 3 entries with known data
            for (uint e = 0; e < entryCount; e++)
            {
                for (uint b = 0; b < entrySize; b++)
                {
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x10 + e);
                }
            }

            // Create free space for the expanded table (need 4 * 8 = 32 bytes of 0xFF)
            uint freeAddr = 0x100100;
            for (int i = 0; i < 64; i++)
                rom.Data[freeAddr + (uint)i] = 0xFF;

            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, entrySize, entryCount);

            Assert.True(result.Success, result.Error);
            Assert.Equal(entryCount + 1, result.NewCount);
            Assert.NotEqual(tableBase, result.NewBaseAddress);

            // Verify the pointer was updated
            uint newBase = rom.p32(pointerAddr);
            Assert.Equal(result.NewBaseAddress, newBase);

            // Verify old data was copied correctly
            for (uint e = 0; e < entryCount; e++)
            {
                for (uint b = 0; b < entrySize; b++)
                {
                    Assert.Equal((byte)(0x10 + e), rom.Data[newBase + e * entrySize + b]);
                }
            }

            // Verify new entry is zero-filled
            uint newEntryStart = newBase + entryCount * entrySize;
            for (uint b = 0; b < entrySize; b++)
            {
                Assert.Equal(0x00, rom.Data[newEntryStart + b]);
            }

            // Verify old table location was freed (0xFF)
            for (uint i = 0; i < entryCount * entrySize; i++)
            {
                Assert.Equal(0xFF, rom.Data[tableBase + i]);
            }
        }

        [Fact]
        public void ExpandTable_PointerOutOfBounds_Fails()
        {
            var rom = MakeRom(256);
            // Pointer address beyond ROM
            var result = DataExpansionCore.ExpandTable(rom, 0x1000, 8, 1);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTable_TableExceedsRom_Fails()
        {
            var rom = MakeRom(0x1000);
            uint pointerAddr = 0x10;
            // Point to near the end of ROM so table overflows
            WritePointer(rom, pointerAddr, 0xFF0);

            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, 32, 5);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTable_PreservesOriginalEntryValues()
        {
            // Verify that specific byte patterns survive the copy
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 4;
            uint entryCount = 2;

            WritePointer(rom, pointerAddr, tableBase);

            // Entry 0: [0xAA, 0xBB, 0xCC, 0xDD]
            rom.Data[tableBase + 0] = 0xAA;
            rom.Data[tableBase + 1] = 0xBB;
            rom.Data[tableBase + 2] = 0xCC;
            rom.Data[tableBase + 3] = 0xDD;
            // Entry 1: [0x11, 0x22, 0x33, 0x44]
            rom.Data[tableBase + 4] = 0x11;
            rom.Data[tableBase + 5] = 0x22;
            rom.Data[tableBase + 6] = 0x33;
            rom.Data[tableBase + 7] = 0x44;

            // Free space
            for (int i = 0; i < 64; i++)
                rom.Data[0x100200 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, entrySize, entryCount);
            Assert.True(result.Success, result.Error);

            uint nb = result.NewBaseAddress;
            Assert.Equal(0xAA, rom.Data[nb + 0]);
            Assert.Equal(0xBB, rom.Data[nb + 1]);
            Assert.Equal(0xCC, rom.Data[nb + 2]);
            Assert.Equal(0xDD, rom.Data[nb + 3]);
            Assert.Equal(0x11, rom.Data[nb + 4]);
            Assert.Equal(0x22, rom.Data[nb + 5]);
            Assert.Equal(0x33, rom.Data[nb + 6]);
            Assert.Equal(0x44, rom.Data[nb + 7]);
        }

    }

    // ────────────────────────────────────────────────
    // ExpandTable undo completeness (gap-sweep #419)
    //
    // Copilot CLI plan review round 3 caught that the original
    // ExpandTable implementation used Array.Copy + direct
    // rom.Data[i] = ... mutations which bypass the ambient-undo
    // recording. The fix routes the copy / zero-fill / wipe through
    // rom.write_range / rom.write_fill so all three byte regions
    // are restored on rollback, in addition to the pointer.
    //
    // This sibling type is in [Collection("SharedState")] because the
    // undo-rollback test mutates CoreState.ROM under a `BeginUndoScope`
    // (Copilot bot review thread PRRT_kwDOH0Mc1M6ETSJK on PR #544;
    // wording fixed in thread PRRT_kwDOH0Mc1M6ETZ4j).
    // ────────────────────────────────────────────────
    [Collection("SharedState")]
    public class DataExpansionCoreUndoTests : IDisposable
    {
        readonly ROM? _savedRom;

        public DataExpansionCoreUndoTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        /// <summary>Helper: build a minimal ROM with LoadLow using ROMFE0 ("NAZO").</summary>
        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            rom.LoadLow("test.gba", data, "NAZO");
            return rom;
        }

        static void WritePointer(ROM rom, uint addr, uint offset)
        {
            uint gbaPtr = offset + 0x08000000;
            rom.Data[addr + 0] = (byte)(gbaPtr & 0xFF);
            rom.Data[addr + 1] = (byte)((gbaPtr >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((gbaPtr >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((gbaPtr >> 24) & 0xFF);
        }

        [Fact]
        public void ExpandTable_Rollback_RestoresAllByteRanges()
        {
            // The class-level [Collection("SharedState")] + Dispose
            // restoration of CoreState.ROM keep this test isolated.
            {
                var rom = MakeRom(0x200000);
                CoreState.ROM = rom;

                uint pointerAddr = 0x10;
                uint tableBase = 0x200;
                uint entrySize = 4;
                uint entryCount = 2;

                WritePointer(rom, pointerAddr, tableBase);

                // Entry 0 / 1 — recognizable patterns.
                rom.Data[tableBase + 0] = 0xAA;
                rom.Data[tableBase + 1] = 0xBB;
                rom.Data[tableBase + 2] = 0xCC;
                rom.Data[tableBase + 3] = 0xDD;
                rom.Data[tableBase + 4] = 0x11;
                rom.Data[tableBase + 5] = 0x22;
                rom.Data[tableBase + 6] = 0x33;
                rom.Data[tableBase + 7] = 0x44;

                // Free space 256 bytes — enough for 12-byte expanded table.
                for (int i = 0; i < 256; i++)
                    rom.Data[0x100200 + i] = 0xFF;

                // Take a deep snapshot of the ROM as the rollback target.
                byte[] snapshot = new byte[rom.Data.Length];
                System.Array.Copy(rom.Data, snapshot, rom.Data.Length);

                // Run ExpandTable under BeginUndoScope so the mutations
                // are tracked.
                var ud = new Undo.UndoData
                {
                    time = System.DateTime.Now,
                    name = "ExpandTable test",
                    list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length
                };
                DataExpansionCore.ExpandResult result;
                using (ROM.BeginUndoScope(ud))
                {
                    result = DataExpansionCore.ExpandTable(rom, pointerAddr, entrySize, entryCount);
                }
                Assert.True(result.Success, result.Error);
                Assert.NotEqual(tableBase, result.NewBaseAddress);

                // Sanity check: after expansion, pointer should match new base.
                Assert.Equal(result.NewBaseAddress, rom.p32(pointerAddr));

                // Rollback the recorded UndoPositions in reverse order
                // (mirrors Undo.RollbackROM).
                for (int i = ud.list.Count - 1; i >= 0; i--)
                {
                    var up = ud.list[i];
                    System.Array.Copy(up.data, 0, rom.Data, up.addr, up.data.Length);
                }

                // After rollback, every byte must match the pre-expansion snapshot.
                for (int i = 0; i < snapshot.Length; i++)
                {
                    if (snapshot[i] != rom.Data[i])
                    {
                        Assert.Fail($"Byte mismatch at 0x{i:X06}: snapshot=0x{snapshot[i]:X02}, post-rollback=0x{rom.Data[i]:X02}");
                    }
                }
            }
        }
    }

    // ────────────────────────────────────────────────
    // ExpandTableTo — table expansion to a specific count (#501)
    //
    // Mirrors WinForms `InputFormRef.ExpandsArea(ExpandsFillOption.NO, ...)`
    // semantics: copy current rows verbatim, zero-fill new rows, write a
    // `0xFFFFFFFF` terminator one dword past the last row so pointer-first
    // scan predicates (`!U.isSafetyPointerOrNull(D0)`) stop at exactly
    // `newCount` rows even when the helper resizes the ROM into a zeroed
    // region.
    //
    // Split out into its own [Collection("SharedState")] type because the
    // RepointsCommentCache test mutates CoreState.CommentCache. Tests that
    // do NOT touch CoreState are kept in the lighter-weight parallel class
    // above (DataExpansionCoreTests).
    // ────────────────────────────────────────────────
    [Collection("SharedState")]
    public class DataExpansionCoreExpandTableToTests : IDisposable
    {
        readonly ROM? _savedRom;
        readonly IEtcCache? _savedComment;
        readonly IEtcCache? _savedLint;
        readonly Undo? _savedUndo;

        public DataExpansionCoreExpandTableToTests()
        {
            _savedRom = CoreState.ROM;
            _savedComment = CoreState.CommentCache;
            _savedLint = CoreState.LintCache;
            _savedUndo = CoreState.Undo;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            CoreState.CommentCache = _savedComment;
            CoreState.LintCache = _savedLint;
            CoreState.Undo = _savedUndo;
        }

        /// <summary>Helper: build a minimal ROM with LoadLow using ROMFE0 ("NAZO").</summary>
        static ROM MakeRom(int size = 0x200000)
        {
            var rom = new ROM();
            byte[] data = new byte[size];
            rom.LoadLow("test.gba", data, "NAZO");
            return rom;
        }

        static void WritePointer(ROM rom, uint addr, uint offset)
        {
            uint gbaPtr = offset + 0x08000000;
            rom.Data[addr + 0] = (byte)(gbaPtr & 0xFF);
            rom.Data[addr + 1] = (byte)((gbaPtr >> 8) & 0xFF);
            rom.Data[addr + 2] = (byte)((gbaPtr >> 16) & 0xFF);
            rom.Data[addr + 3] = (byte)((gbaPtr >> 24) & 0xFF);
        }

        // ──────── Edge / failure cases ────────

        [Fact]
        public void ExpandTableTo_NullRom_Fails()
        {
            var result = DataExpansionCore.ExpandTableTo(null, 0, 8, 1, 5);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTableTo_ZeroEntrySize_Fails()
        {
            var rom = MakeRom(0x1000);
            var result = DataExpansionCore.ExpandTableTo(rom, 0, 0, 1, 5);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTableTo_NewCountSmaller_Fails()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            WritePointer(rom, pointerAddr, tableBase);

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, 8, 10, 5);
            Assert.False(result.Success);
        }

        [Fact]
        public void ExpandTableTo_SameCount_NoOp()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            WritePointer(rom, pointerAddr, tableBase);
            rom.Data[tableBase + 0] = 0xAA;
            uint oldPtr = rom.p32(pointerAddr);

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, 8, 5, 5);
            Assert.True(result.Success);
            // Pointer should not change for a no-op.
            Assert.Equal(oldPtr, rom.p32(pointerAddr));
            Assert.Equal(5u, result.NewCount);
            Assert.Equal(0xAA, rom.Data[tableBase + 0]);
        }

        // ──────── Happy path: data movement ────────

        [Fact]
        public void ExpandTableTo_CopiesAllExistingEntries()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 3;
            uint newCount = 7;

            WritePointer(rom, pointerAddr, tableBase);
            // Write 3 entries with recognizable patterns.
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x10 + e);

            // Plenty of free space.
            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            Assert.True(result.Success, result.Error);

            // Every old entry byte must survive at the new base.
            uint nb = result.NewBaseAddress;
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    Assert.Equal((byte)(0x10 + e), rom.Data[nb + e * entrySize + b]);
        }

        [Fact]
        public void ExpandTableTo_NewEntriesZeroFilled()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 3;
            uint newCount = 7;

            WritePointer(rom, pointerAddr, tableBase);
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x10 + e);

            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            Assert.True(result.Success, result.Error);

            // The (newCount - currentCount) new rows must be all 0x00.
            uint nb = result.NewBaseAddress;
            for (uint e = currentCount; e < newCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    Assert.Equal(0x00, rom.Data[nb + e * entrySize + b]);
        }

        [Fact]
        public void ExpandTableTo_UpdatesPointerToNewBase()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            WritePointer(rom, pointerAddr, tableBase);

            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, 8, 2, 6);
            Assert.True(result.Success, result.Error);

            // Pointer must point to the new base.
            Assert.NotEqual(tableBase, result.NewBaseAddress);
            Assert.Equal(result.NewBaseAddress, rom.p32(pointerAddr));
        }

        [Fact]
        public void ExpandTableTo_OldRegion_FilledWith0x00()
        {
            // v5 fix 3 / WF parity — InputFormRef.ExpandsArea wipes the old
            // region with 0x00, not 0xFF. (The +1 helper `ExpandTable` uses
            // 0xFF; the two helpers intentionally differ — see XML docs.)
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 3;
            uint newCount = 6;

            WritePointer(rom, pointerAddr, tableBase);
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0xAA);

            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            Assert.True(result.Success, result.Error);

            // Old region bytes must all be 0x00 (matching WinForms behavior).
            for (uint b = 0; b < currentCount * entrySize; b++)
                Assert.Equal(0x00, rom.Data[tableBase + b]);
        }

        [Fact]
        public void ExpandTableTo_FirstRowAllZero_StillCopiesAllCurrentCountRows()
        {
            // v5 fix 1 — the action-anime table allows row-0 to be all zero
            // (`isSafetyPointerOrNull(0) == true`). `EstimateEntryCount` would
            // stop at row 0; the helper MUST trust the caller-provided
            // `currentCount` and copy all `currentCount` rows verbatim.
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 4;
            uint newCount = 8;

            WritePointer(rom, pointerAddr, tableBase);
            // Row 0 stays all 0x00 (reserved empty entry).
            // Rows 1..3 get recognizable patterns.
            for (uint e = 1; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x10 + e);

            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            Assert.True(result.Success, result.Error);

            uint nb = result.NewBaseAddress;
            // Row 0 stays zero.
            for (uint b = 0; b < entrySize; b++)
                Assert.Equal(0x00, rom.Data[nb + b]);
            // Rows 1..3 carry their patterns.
            for (uint e = 1; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    Assert.Equal((byte)(0x10 + e), rom.Data[nb + e * entrySize + b]);
        }

        // ──────── Stop-row terminator (v5 fix 1 — false-positive-proof) ────────

        [Fact]
        public void ExpandTableTo_WritesTerminator_EvenWhenAllocationIsInZeroedRegion()
        {
            // v5 strengthened test: force the ROM-resize path so the new
            // allocation lands in a freshly-zeroed (0x00) region. If the
            // helper forgets the explicit 0xFFFFFFFF terminator write, the
            // dword at `newBase + newCount * entrySize` will be 0x00, not
            // 0xFFFFFFFF, and the pointer-first scan would continue past
            // newCount through valid-null rows.
            //
            // Setup: a small ROM with NO 0xFF free space at all. ExpandTableTo
            // must resize the ROM, append at the tail, and write the
            // terminator explicitly.
            var rom = MakeRom(0x10000);   // 64 KB, all 0x00.
            // The pointer-first scan predicate `isSafetyPointer` references
            // CoreState.ROM.Data.Length, so we must wire CoreState.ROM up.
            CoreState.ROM = rom;
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 2;
            uint newCount = 5;

            WritePointer(rom, pointerAddr, tableBase);
            // Seed the existing rows with VALID safe-pointer-or-null values
            // so the pointer-first scan accepts them (otherwise the scan
            // stops at row 0 on the 0xAA pattern, not at the terminator).
            // Row 0 = null (0x00000000 -- isSafetyPointerOrNull == true).
            // Row 1 = valid GBA pointer 0x08000300 (offset 0x300 inside ROM).
            for (uint b = 0; b < entrySize; b++)
                rom.Data[tableBase + 0 * entrySize + b] = 0x00;
            // Row 1 D0 = 0x08000300 (little-endian).
            rom.Data[tableBase + 1 * entrySize + 0] = 0x00;
            rom.Data[tableBase + 1 * entrySize + 1] = 0x03;
            rom.Data[tableBase + 1 * entrySize + 2] = 0x00;
            rom.Data[tableBase + 1 * entrySize + 3] = 0x08;
            // No 0xFF free space — helper MUST resize into a 0x00 region.

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            Assert.True(result.Success, result.Error);

            uint nb = result.NewBaseAddress;
            uint termAddr = nb + newCount * entrySize;
            // Terminator dword must be 0xFFFFFFFF (written explicitly).
            Assert.Equal(0xFFFFFFFFu, rom.u32(termAddr));

            // Pointer-first scan must stop at exactly `newCount` rows.
            uint scanned = 0;
            uint cur = nb;
            while (cur + 4 <= (uint)rom.Data.Length)
            {
                uint d = rom.u32(cur);
                if (!U.isSafetyPointerOrNull(d)) break;
                scanned++;
                cur += entrySize;
                // Safety bound — we should NEVER scan more than newCount
                // rows before hitting the terminator.
                if (scanned > newCount + 10) break;
            }
            Assert.Equal(newCount, scanned);
        }

        // ──────── Cache repoint (v5 fix 2 — forward-only) ────────

        [Fact]
        public void ExpandTableTo_RepointsCommentCache()
        {
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 3;
            uint newCount = 5;

            WritePointer(rom, pointerAddr, tableBase);
            for (uint e = 0; e < currentCount; e++)
                rom.Data[tableBase + e * entrySize] = (byte)(0x10 + e);

            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            // Install a headless comment cache with entries pointing at
            // existing rows.
            var cache = new HeadlessEtcCache();
            CoreState.CommentCache = cache;
            cache.Update(tableBase + 0 * entrySize, "row 0 comment");
            cache.Update(tableBase + 1 * entrySize, "row 1 comment");
            cache.Update(tableBase + 2 * entrySize, "row 2 comment");
            // Out-of-table key — must NOT be relocated.
            cache.Update(0x99999999, "unrelated");

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            Assert.True(result.Success, result.Error);

            uint nb = result.NewBaseAddress;
            Assert.Equal("row 0 comment", cache.At(nb + 0 * entrySize));
            Assert.Equal("row 1 comment", cache.At(nb + 1 * entrySize));
            Assert.Equal("row 2 comment", cache.At(nb + 2 * entrySize));
            // Out-of-table key stays put.
            Assert.Equal("unrelated", cache.At(0x99999999));
            // Old in-table addresses must no longer be in the cache.
            Assert.Equal("", cache.At(tableBase + 0 * entrySize));
        }

        // ──────── Undo rollback (v5 fix 2 — ROM-only) ────────

        [Fact]
        public void ExpandTableTo_Rollback_RestoresROMBytes()
        {
            // The cache repoint is forward-only (matches WF semantics). The
            // rollback only restores ROM byte ranges — the cache stays
            // pointing at the new addresses after rollback. This is a
            // documented WF parity gap.
            var rom = MakeRom(0x200000);
            CoreState.ROM = rom;

            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 3;
            uint newCount = 6;

            WritePointer(rom, pointerAddr, tableBase);
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x10 + e);

            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            byte[] snapshot = new byte[rom.Data.Length];
            Array.Copy(rom.Data, snapshot, rom.Data.Length);

            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "ExpandTableTo undo test",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };

            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(ud))
            {
                result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            }
            Assert.True(result.Success, result.Error);

            // Manually roll back the recorded undo positions (reverse order).
            for (int i = ud.list.Count - 1; i >= 0; i--)
            {
                var up = ud.list[i];
                Array.Copy(up.data, 0, rom.Data, up.addr, up.data.Length);
            }

            // Every byte must match the pre-expansion snapshot.
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != rom.Data[i])
                    Assert.Fail($"Byte mismatch at 0x{i:X06}: snapshot=0x{snapshot[i]:X02}, post-rollback=0x{rom.Data[i]:X02}");
            }
        }

        // ──────── fullZeroTerminatorRow (#1078) ────────

        [Fact]
        public void ExpandTableTo_FullZeroTerminatorRow_FreeSpace_WritesAllZeroTerminatorRow()
        {
            // #1078: with fullZeroTerminatorRow:true the terminator is a FULL
            // entrySize-byte all-zero row (NOT a 0xFFFFFFFF dword). Pre-fill the
            // target free region (rows + the would-be terminator-row bytes) with
            // 0xFF so the assertion proves the helper EXPLICITLY zeroes the
            // terminator row rather than leaving leftover 0xFF.
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 16;
            uint currentCount = 3;
            uint newCount = 7;

            WritePointer(rom, pointerAddr, tableBase);
            // Recognizable old rows.
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x10 + e);

            // 0xFF free region big enough for newCount rows + a full terminator
            // row (and then some). Pre-fill 0xFF so leftover bytes are detectable.
            uint freeBase = 0x100100;
            for (int i = 0; i < (int)((newCount + 4) * entrySize); i++)
                rom.Data[freeBase + (uint)i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(
                rom, pointerAddr, entrySize, currentCount, newCount, fullZeroTerminatorRow: true);
            Assert.True(result.Success, result.Error);

            uint nb = result.NewBaseAddress;

            // Old rows preserved.
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    Assert.Equal((byte)(0x10 + e), rom.Data[nb + e * entrySize + b]);

            // New rows zero-filled.
            for (uint e = currentCount; e < newCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    Assert.Equal(0x00, rom.Data[nb + e * entrySize + b]);

            // The FULL entrySize-byte terminator row at newBase + newCount*entrySize
            // must be all 0x00 — proves explicit zeroing, not leftover 0xFF, and
            // that NO 0xFFFFFFFF dword was written.
            uint termAddr = nb + newCount * entrySize;
            for (uint b = 0; b < entrySize; b++)
                Assert.Equal(0x00, rom.Data[termAddr + b]);
        }

        [Fact]
        public void ExpandTableTo_FullZeroTerminatorRow_ResizePath_TerminatorAndRollback()
        {
            // #1078: resize/no-free-space path with fullZeroTerminatorRow:true.
            // ROM has NO 0xFF free run, so the helper must resize (grows the ROM).
            // Same terminator + row assertions; the REAL filesize-restoring
            // rollback (CoreState.Undo.RunUndo) must restore the ORIGINAL length
            // AND every byte — proving the resize path is fully reversible
            // (Copilot review on PR #1080: a manual reverse-copy of the recorded
            // positions does NOT restore the resized length).
            var rom = MakeRom(0x10000);   // 64 KB, all 0x00 → no 0xFF free space.
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 16;
            uint currentCount = 2;
            uint newCount = 5;

            WritePointer(rom, pointerAddr, tableBase);
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0xA0 + e);

            int originalLength = rom.Data.Length;
            byte[] snapshot = new byte[originalLength];
            Array.Copy(rom.Data, snapshot, originalLength);

            // NewUndoData captures the pre-expansion filesize so RunUndo can
            // down-resize the ROM back to it (the same pattern as
            // MagicListExpandCoreTests / the View's UndoService.Begin).
            var ud = CoreState.Undo.NewUndoData("ExpandTableTo full-zero terminator resize test");
            DataExpansionCore.ExpandResult result;
            using (ROM.BeginUndoScope(ud))
            {
                result = DataExpansionCore.ExpandTableTo(
                    rom, pointerAddr, entrySize, currentCount, newCount, fullZeroTerminatorRow: true);
            }
            Assert.True(result.Success, result.Error);
            // The resize path actually grew the ROM (otherwise this test wouldn't
            // exercise the filesize-restore branch).
            Assert.True(rom.Data.Length > originalLength,
                $"expected the resize path to grow the ROM beyond {originalLength}, got {rom.Data.Length}");

            uint nb = result.NewBaseAddress;

            // Old rows preserved at the new base.
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    Assert.Equal((byte)(0xA0 + e), rom.Data[nb + e * entrySize + b]);

            // New rows zero, FULL terminator row zero.
            for (uint e = currentCount; e < newCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    Assert.Equal(0x00, rom.Data[nb + e * entrySize + b]);
            uint termAddr = nb + newCount * entrySize;
            for (uint b = 0; b < entrySize; b++)
                Assert.Equal(0x00, rom.Data[termAddr + b]);

            // REAL rollback: push the transaction then RunUndo. RunUndo restores
            // BOTH the recorded byte ranges AND the original filesize (Patch
            // down-resizes rom.Data back to ud.filesize).
            CoreState.Undo.Push(ud);
            CoreState.Undo.RunUndo();

            // Length restored to the ORIGINAL (pre-resize) length.
            Assert.Equal(originalLength, rom.Data.Length);
            // AND every byte over the whole original length matches the snapshot.
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] != rom.Data[i])
                    Assert.Fail($"Byte mismatch at 0x{i:X06}: snapshot=0x{snapshot[i]:X02}, post-rollback=0x{rom.Data[i]:X02}");
            }
        }

        [Fact]
        public void ExpandTableTo_DefaultPath_StillWritesFFFFFFFFDword()
        {
            // Default-path strict regression: fullZeroTerminatorRow:false (the
            // #501 caller) must still reserve only +4 and write the 0xFFFFFFFF
            // dword terminator — unchanged by the #1078 generalization.
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 16;
            uint currentCount = 2;
            uint newCount = 5;

            WritePointer(rom, pointerAddr, tableBase);
            for (uint e = 0; e < currentCount; e++)
                for (uint b = 0; b < entrySize; b++)
                    rom.Data[tableBase + e * entrySize + b] = (byte)(0x30 + e);

            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(
                rom, pointerAddr, entrySize, currentCount, newCount, fullZeroTerminatorRow: false);
            Assert.True(result.Success, result.Error);

            uint nb = result.NewBaseAddress;
            uint termAddr = nb + newCount * entrySize;
            // Default path: a single 0xFFFFFFFF dword terminator (NOT an all-zero row).
            Assert.Equal(0xFFFFFFFFu, rom.u32(termAddr));
        }

        [Fact]
        public void ExpandTableTo_DefaultPath_OmittedArg_IsByteIdenticalToFalse()
        {
            // The omitted-arg call (existing #501 callers) must behave exactly
            // like fullZeroTerminatorRow:false — same new base, same 0xFFFFFFFF
            // dword terminator.
            var rom = MakeRom(0x200000);
            uint pointerAddr = 0x10;
            uint tableBase = 0x200;
            uint entrySize = 8;
            uint currentCount = 3;
            uint newCount = 6;

            WritePointer(rom, pointerAddr, tableBase);
            for (int i = 0; i < 256; i++)
                rom.Data[0x100100 + i] = 0xFF;

            var result = DataExpansionCore.ExpandTableTo(rom, pointerAddr, entrySize, currentCount, newCount);
            Assert.True(result.Success, result.Error);
            uint termAddr = result.NewBaseAddress + newCount * entrySize;
            Assert.Equal(0xFFFFFFFFu, rom.u32(termAddr));
        }
    }
}
