using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for LZ77ToolCore — the cross-platform helpers powering the LZ77
    /// Tool's Move + Recompress tabs. Closes the residual scope of issue #371
    /// from PR #468.
    /// </summary>
    [Collection("SharedState")]
    public class LZ77ToolCoreTests
    {
        static ROM CreateRom(int size = 0x10000)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        static Undo.UndoData NewUndoData(ROM rom, string name = "test")
        {
            return new Undo.UndoData
            {
                time = System.DateTime.Now,
                name = name,
                list = new List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length,
            };
        }

        // -------- MoveCompressedData: validation paths --------

        [Fact]
        public void MoveCompressedData_NullRom_Fails()
        {
            var result = LZ77ToolCore.MoveCompressedData(null, 0x1000, 0x2000, 0x10);
            Assert.False(result.Ok);
            Assert.Contains("ROM is null", result.ErrorMessage);
        }

        [Fact]
        public void MoveCompressedData_ZeroSrcAddr_Fails()
        {
            var rom = CreateRom();
            var result = LZ77ToolCore.MoveCompressedData(rom, 0, 0x2000, 0x10);
            Assert.False(result.Ok);
            Assert.Contains("source address is 0", result.ErrorMessage);
        }

        [Fact]
        public void MoveCompressedData_ZeroLength_Fails()
        {
            var rom = CreateRom();
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x2000, 0);
            Assert.False(result.Ok);
            Assert.Contains("length is 0", result.ErrorMessage);
        }

        [Fact]
        public void MoveCompressedData_LengthTooLarge_Fails()
        {
            var rom = CreateRom();
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x2000, LZ77ToolCore.MOVE_LENGTH_LIMIT);
            Assert.False(result.Ok);
            Assert.Contains("too large", result.ErrorMessage);
        }

        [Fact]
        public void MoveCompressedData_SrcOutOfBounds_Fails()
        {
            var rom = CreateRom(0x1000);
            var result = LZ77ToolCore.MoveCompressedData(rom, 0xF00, 0x100, 0x500);
            Assert.False(result.Ok);
            Assert.Contains("source range", result.ErrorMessage);
        }

        [Fact]
        public void MoveCompressedData_DestOutOfBounds_Fails()
        {
            var rom = CreateRom(0x1000);
            rom.Data[0x500] = 0x01; // make srcBytes not all-zero
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x500, 0xF80, 0x100);
            Assert.False(result.Ok);
            Assert.Contains("destination range", result.ErrorMessage);
        }

        [Fact]
        public void MoveCompressedData_AlreadyMoved_Fails()
        {
            var rom = CreateRom();
            // 0x1000 + 0x10 of all-zeros -> "already moved" guard.
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x2000, 0x10);
            Assert.False(result.Ok);
            Assert.Contains("already moved", result.ErrorMessage);
        }

        // -------- MoveCompressedData: success paths --------

        [Fact]
        public void MoveCompressedData_BasicMove_CopiesAndZerosSource()
        {
            var rom = CreateRom();
            // Source payload.
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF, 0x12, 0x34, 0x56, 0x78 };
            for (int i = 0; i < payload.Length; i++) rom.Data[0x1000 + i] = payload[i];

            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x4000, (uint)payload.Length);
            Assert.True(result.Ok, result.ErrorMessage);
            Assert.Equal(0x4000u, result.NewAddress);
            Assert.False(result.AutoAllocated);
            // Source zeroed, destination has payload.
            for (int i = 0; i < payload.Length; i++)
            {
                Assert.Equal(0, rom.Data[0x1000 + i]);
                Assert.Equal(payload[i], rom.Data[0x4000 + i]);
            }
        }

        [Fact]
        public void MoveCompressedData_AutoAllocate_ZeroToAddr_AllocatesAtEnd()
        {
            // Small ROM where free space search fits at end-of-file.
            var rom = CreateRom(0x800);
            // Fill almost everything so FindFreeSpace doesn't find a hole.
            for (int i = 0x100; i < rom.Data.Length; i++) rom.Data[i] = 0x55;
            // Mark source payload.
            byte[] payload = { 1, 2, 3, 4 };
            for (int i = 0; i < payload.Length; i++) rom.Data[0x200 + i] = payload[i];

            uint origLen = (uint)rom.Data.Length;
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x200, 0, (uint)payload.Length);

            Assert.True(result.Ok, result.ErrorMessage);
            Assert.True(result.AutoAllocated);
            Assert.True(result.NewAddress >= origLen,
                $"NewAddress 0x{result.NewAddress:X} should be at original end 0x{origLen:X}");
            // Source zeroed.
            for (int i = 0; i < payload.Length; i++) Assert.Equal(0, rom.Data[0x200 + i]);
            // Destination has payload.
            for (int i = 0; i < payload.Length; i++)
                Assert.Equal(payload[i], rom.Data[result.NewAddress + i]);
        }

        [Fact]
        public void MoveCompressedData_AutoAllocate_FindsFreeSpaceBeforeEnd()
        {
            var rom = CreateRom(0x10000);
            // ROM is all zeros -> FindFreeSpace will return a low offset.
            // Mark source payload.
            byte[] payload = { 9, 8, 7, 6 };
            for (int i = 0; i < payload.Length; i++) rom.Data[0x1000 + i] = payload[i];

            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0, (uint)payload.Length);
            Assert.True(result.Ok, result.ErrorMessage);
            Assert.True(result.AutoAllocated);
            Assert.True(result.NewAddress < rom.Data.Length);
        }

        // -------- Auto-allocate slack regression (PR #481 review point 1) --------

        [Fact]
        public void MoveCompressedData_AutoAllocate_ExactSizedFreeRun_DoesNotClobberSentinel()
        {
            // Regression: AutoAllocateDestination must request slack + payload bytes
            // so that returning `freespace + 16` does NOT write past the free area.
            // Place a sentinel just past an exact-sized free run and verify it's untouched.
            var rom = CreateRom(0x10000);
            // Fill most of ROM with 0xFF to leave only a small free run.
            for (int i = 0x100; i < rom.Data.Length; i++) rom.Data[i] = 0xFF;
            // Create a free run from 0x2000 to 0x2100 (256 bytes), surrounded by 0xFF.
            uint runStart = 0x2000;
            uint runSize = 0x100;
            for (uint i = runStart; i < runStart + runSize; i++) rom.Data[i] = 0;
            // Place a sentinel BYTE just past the free run.
            uint sentinelAddr = runStart + runSize;
            byte sentinel = 0xAB;
            rom.Data[sentinelAddr] = sentinel;
            // Mark source payload (small).
            byte[] payload = { 1, 2, 3, 4 };
            for (int i = 0; i < payload.Length; i++) rom.Data[0x500 + i] = payload[i];

            var result = LZ77ToolCore.MoveCompressedData(rom, 0x500, 0, (uint)payload.Length);
            // Regardless of where the allocator chose to land (free run + 16 or end-of-file),
            // the sentinel just past the free run must NOT be clobbered.
            Assert.True(result.Ok, result.ErrorMessage);
            Assert.Equal(sentinel, rom.Data[sentinelAddr]);
        }

        // -------- Overlap rejection regression (PR #481 review point 2) --------

        [Fact]
        public void MoveCompressedData_OverlappingDestInsideSource_Rejects()
        {
            var rom = CreateRom(0x10000);
            // Source: 0x1000..0x1100 (256 bytes).
            for (int i = 0; i < 256; i++) rom.Data[0x1000 + i] = (byte)(i & 0xFF);
            // Destination INSIDE source: 0x1080.
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x1080, 0x100);
            Assert.False(result.Ok);
            Assert.Contains("overlap", result.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MoveCompressedData_OverlappingSourceInsideDest_Rejects()
        {
            var rom = CreateRom(0x10000);
            // Source: 0x2000..0x2010 (16 bytes) — inside larger destination 0x1F80..0x2080.
            for (int i = 0; i < 16; i++) rom.Data[0x2000 + i] = (byte)(i + 1);
            // Destination range overlapping with source.
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x2000, 0x1F80, 0x100);
            Assert.False(result.Ok);
            Assert.Contains("overlap", result.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MoveCompressedData_AdjacentNonOverlapping_Succeeds()
        {
            var rom = CreateRom(0x10000);
            for (int i = 0; i < 16; i++) rom.Data[0x1000 + i] = (byte)(i + 1);
            // Destination immediately AFTER source (no overlap: dest start == src end).
            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x1010, 0x10);
            Assert.True(result.Ok, result.ErrorMessage);
        }

        // -------- Pointer-search gating (review point 2) --------

        [Fact]
        public void SearchPointers_ZeroAddress_ReturnsEmpty()
        {
            var rom = CreateRom();
            var r = LZ77ToolCore.SearchPointersForAddress(rom.Data, 0);
            Assert.Empty(r.Pointers);
            Assert.False(r.UsedRawFallback);
            Assert.True(r.MissingEventAwareCoverage);
        }

        [Fact]
        public void SearchPointers_LdrHits_DoesNotFallbackToRaw()
        {
            var rom = CreateRom();
            // Place a synthetic LDR-style pointer reference at 0x500 -> 0x1000.
            // GrepLDRData scans Thumb 0x4800..0x4Fxx + PC-relative target alignment.
            // For test purposes the simpler check is: if LDR returns >=1, raw is NOT run.
            // Inject the LDR pattern: at addr 0x500 put "01 48" (LDR r0, [pc, #4]) so
            // PC-relative target = (0x500+4) aligned + 1*4 = 0x508. Place pointer 0x08001000 at 0x508.
            rom.Data[0x500] = 0x01;
            rom.Data[0x501] = 0x48;
            U.write_u32(rom.Data, 0x508, 0x08001000);

            // Also place a raw 4-byte pointer 0x08001000 somewhere else (would be a "raw hit").
            U.write_u32(rom.Data, 0x800, 0x08001000);

            var r = LZ77ToolCore.SearchPointersForAddress(rom.Data, 0x1000);
            Assert.False(r.UsedRawFallback);
            Assert.True(r.MissingEventAwareCoverage);
            // LDR hit was at 0x508 (the data pointer location). Raw 0x800 hit should NOT appear.
            Assert.Contains(0x508u, r.Pointers);
            Assert.DoesNotContain(0x800u, r.Pointers);
        }

        [Fact]
        public void SearchPointers_NoLdrHits_FallsBackToRawAndSetsFlag()
        {
            var rom = CreateRom();
            // Place only a raw 4-byte pointer at 0x800 (no LDR pattern anywhere).
            U.write_u32(rom.Data, 0x800, 0x08001000);

            var r = LZ77ToolCore.SearchPointersForAddress(rom.Data, 0x1000);
            Assert.True(r.UsedRawFallback);
            Assert.True(r.MissingEventAwareCoverage);
            Assert.Contains(0x800u, r.Pointers);
        }

        [Fact]
        public void SearchPointers_AlwaysSetsMissingEventAwareCoverageFlag()
        {
            var rom = CreateRom();
            // Empty result: still has MissingEventAwareCoverage = true.
            var r = LZ77ToolCore.SearchPointersForAddress(rom.Data, 0x9999);
            Assert.True(r.MissingEventAwareCoverage);
        }

        // -------- Move + pointer rewrite --------

        [Fact]
        public void MoveCompressedData_RewritesLDRPointer()
        {
            var rom = CreateRom();
            // Source data at 0x1000.
            byte[] payload = { 1, 2, 3, 4 };
            for (int i = 0; i < payload.Length; i++) rom.Data[0x1000 + i] = payload[i];
            // LDR pattern at 0x500 -> 0x508; the value at 0x508 is the pointer (0x08001000).
            rom.Data[0x500] = 0x01;
            rom.Data[0x501] = 0x48;
            U.write_u32(rom.Data, 0x508, 0x08001000);

            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x4000, (uint)payload.Length);
            Assert.True(result.Ok, result.ErrorMessage);
            // Pointer at 0x508 should now read 0x08004000.
            uint rewritten = U.u32(rom.Data, 0x508);
            Assert.Equal(0x08004000u, rewritten);
            Assert.Contains(0x508u, result.PointersRewritten);
        }

        [Fact]
        public void MoveCompressedData_RewritesRawPointer_WhenNoLDR()
        {
            var rom = CreateRom();
            // Source data at 0x1000.
            byte[] payload = { 1, 2, 3, 4 };
            for (int i = 0; i < payload.Length; i++) rom.Data[0x1000 + i] = payload[i];
            // Raw 4-byte pointer (no LDR pattern).
            U.write_u32(rom.Data, 0x800, 0x08001000);

            var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x4000, (uint)payload.Length);
            Assert.True(result.Ok, result.ErrorMessage);
            Assert.True(result.UsedRawFallback);
            uint rewritten = U.u32(rom.Data, 0x800);
            Assert.Equal(0x08004000u, rewritten);
            Assert.Contains(0x800u, result.PointersRewritten);
        }

        // -------- Undo non-duplication (review point 1) --------

        [Fact]
        public void MoveCompressedData_WithBeginUndoScope_RecordsPositionsOnceNoDuplicates()
        {
            var rom = CreateRom();
            byte[] payload = { 0xAB, 0xCD, 0xEF, 0x01 };
            for (int i = 0; i < payload.Length; i++) rom.Data[0x1000 + i] = payload[i];

            var ud = NewUndoData(rom);
            using (ROM.BeginUndoScope(ud))
            {
                var result = LZ77ToolCore.MoveCompressedData(rom, 0x1000, 0x4000, (uint)payload.Length);
                Assert.True(result.Ok);
            }

            // Expected: 1 write_range(dest, 4) + 1 write_fill(src, 4) = 2 positions.
            // No pointer rewrites in this test (no pointers placed) so it's just 2.
            Assert.Equal(2, ud.list.Count);
            // Verify all are unique pairs (no duplicate addr+length).
            var unique = new HashSet<(uint, int)>();
            foreach (var p in ud.list) unique.Add((p.addr, p.data.Length));
            Assert.Equal(ud.list.Count, unique.Count);
        }

        // -------- RecompressAt validation --------

        [Fact]
        public void RecompressAt_NullRom_Fails()
        {
            var r = LZ77ToolCore.RecompressAt(null, 0x100, 0x100);
            Assert.False(r.Ok);
            Assert.Contains("ROM is null", r.ErrorMessage);
        }

        [Fact]
        public void RecompressAt_NotAligned_ReturnsZero()
        {
            var rom = CreateRom();
            var r = LZ77ToolCore.RecompressAt(rom, 0x101, 0x100);
            Assert.False(r.Ok);
            Assert.Contains("4-byte aligned", r.ErrorMessage);
        }

        [Fact]
        public void RecompressAt_NotLZ77Magic_ReturnsZero()
        {
            var rom = CreateRom();
            rom.Data[0x100] = 0x55; // not 0x10
            var r = LZ77ToolCore.RecompressAt(rom, 0x100, 0x100);
            Assert.False(r.Ok);
            Assert.Contains("not LZ77 magic", r.ErrorMessage);
        }

        [Fact]
        public void RecompressAt_AllocatedLengthNotAligned_ReturnsError()
        {
            // PR #481 review point 4: allocatedLength must be 4-byte aligned
            // because the helper docstring promises 4-aligned semantics.
            var rom = CreateRom();
            rom.Data[0x100] = 0x10;
            // Use any non-4-aligned allocated length.
            var r = LZ77ToolCore.RecompressAt(rom, 0x100, 0x101);
            Assert.False(r.Ok);
            Assert.Contains("not 4-byte aligned", r.ErrorMessage);
        }

        [Fact]
        public void RecompressAt_BadUncompressSize_ReturnsZero()
        {
            var rom = CreateRom();
            rom.Data[0x100] = 0x10;
            // header size = 0 -> getUncompressSize returns 0.
            rom.Data[0x101] = 0;
            rom.Data[0x102] = 0;
            rom.Data[0x103] = 0;
            var r = LZ77ToolCore.RecompressAt(rom, 0x100, 0x100);
            Assert.False(r.Ok);
            Assert.Contains("getUncompressSize=0", r.ErrorMessage);
        }

        [Fact]
        public void RecompressAt_WriteBoundedByAllocatedLength()
        {
            var rom = CreateRom();
            // Build a valid LZ77 stream that's larger than necessary.
            // Take a small, repetitive uncompressed payload, compress it, place it at 0x200.
            byte[] uncompressed = new byte[64];
            for (int i = 0; i < uncompressed.Length; i++) uncompressed[i] = (byte)(i % 4);
            byte[] compressed = LZ77.compress(uncompressed);
            // Place at 0x200 with extra padding (allocatedLength bigger than actual compressed size).
            for (int i = 0; i < compressed.Length; i++) rom.Data[0x200 + i] = compressed[i];
            // Pad after with non-zero sentinel so we can verify it gets zeroed.
            uint allocatedLength = U.Padding4((uint)compressed.Length) + 32u;
            for (uint i = (uint)compressed.Length; i < allocatedLength; i++)
                rom.Data[0x200 + i] = 0xAA;
            // Ensure post-allocation area has a sentinel that must NOT be touched.
            uint postAddr = 0x200 + allocatedLength;
            rom.Data[postAddr] = 0xFF;
            rom.Data[postAddr + 1] = 0xFF;

            var r = LZ77ToolCore.RecompressAt(rom, 0x200, allocatedLength);
            // Either succeeded with savings or returned no-benefit; either way the
            // post-allocation byte must be untouched.
            Assert.True(r.Ok, r.ErrorMessage);
            Assert.Equal(0xFF, rom.Data[postAddr]);
            Assert.Equal(0xFF, rom.Data[postAddr + 1]);
        }

        [Fact]
        public void RecompressAt_AlreadyOptimal_ReturnsZero()
        {
            var rom = CreateRom();
            // Place an already-optimal compressed stream at 0x300 with exact allocatedLength.
            byte[] uncompressed = new byte[16];
            for (int i = 0; i < 16; i++) uncompressed[i] = (byte)i;
            byte[] compressed = LZ77.compress(uncompressed);
            for (int i = 0; i < compressed.Length; i++) rom.Data[0x300 + i] = compressed[i];

            uint allocatedLength = U.Padding4((uint)compressed.Length);
            var r = LZ77ToolCore.RecompressAt(rom, 0x300, allocatedLength);
            // Same size -> savings = 0 -> Ok=true but SavedBytes=0.
            Assert.True(r.Ok);
            Assert.Equal(0u, r.SavedBytes);
        }

        // -------- ScanForLZ77Candidates --------

        [Fact]
        public void ScanForLZ77Candidates_FindsValidEntry()
        {
            var rom = CreateRom();
            byte[] uncompressed = new byte[64];
            for (int i = 0; i < 64; i++) uncompressed[i] = (byte)(i & 3);
            byte[] compressed = LZ77.compress(uncompressed);

            // Place at 0x400 (4-byte aligned).
            for (int i = 0; i < compressed.Length; i++) rom.Data[0x400 + i] = compressed[i];

            var list = LZ77ToolCore.ScanForLZ77Candidates(rom);
            Assert.Contains(list, a => a.Addr == 0x400);
        }

        [Fact]
        public void ScanForLZ77Candidates_DedupsByAddress()
        {
            var rom = CreateRom();
            byte[] uncompressed = new byte[32];
            for (int i = 0; i < 32; i++) uncompressed[i] = (byte)(i & 1);
            byte[] compressed = LZ77.compress(uncompressed);
            for (int i = 0; i < compressed.Length; i++) rom.Data[0x400 + i] = compressed[i];

            var list = LZ77ToolCore.ScanForLZ77Candidates(rom);
            int count = 0;
            foreach (var a in list)
                if (a.Addr == 0x400) count++;
            Assert.Equal(1, count);
        }

        [Fact]
        public void ScanForLZ77Candidates_RejectsFalsePositives_BadHeader()
        {
            var rom = CreateRom();
            // Place a fake 0x10 magic with a tiny garbage stream that won't decompress to the header size.
            rom.Data[0x400] = 0x10;
            rom.Data[0x401] = 0xFF; rom.Data[0x402] = 0xFF; rom.Data[0x403] = 0xFF;
            // Following bytes are zero; LZ77.decompress would short-circuit or produce wrong length.

            var list = LZ77ToolCore.ScanForLZ77Candidates(rom);
            // 0x400 should NOT appear as a valid candidate (rejected by validation).
            foreach (var a in list) Assert.NotEqual(0x400u, a.Addr);
        }
    }
}
