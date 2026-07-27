using System;
using System.Linq;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageImportCoreWriteCompressedTests : IDisposable
    {
        readonly ROM? _savedRom;

        public ImageImportCoreWriteCompressedTests()
        {
            _savedRom = CoreState.ROM;
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        static ROM CreateRom(int size = 0x8000)
        {
            byte[] data = Enumerable.Repeat((byte)0xAA, size).ToArray();
            for (int i = size / 2; i < size / 2 + 0x1000; i++)
                data[i] = 0x00;

            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic.gba", data, "NAZO"));
            CoreState.ROM = rom;
            return rom;
        }

        static byte[] LiteralLz77(byte seed, int uncompressedSize)
        {
            Assert.True(uncompressedSize >= 3);
            byte[] raw = Enumerable.Range(0, uncompressedSize)
                .Select(i => (byte)(seed + i))
                .ToArray();
            int flagCount = (uncompressedSize + 7) / 8;
            byte[] compressed = new byte[4 + flagCount + uncompressedSize];
            compressed[0] = 0x10;
            compressed[1] = (byte)(uncompressedSize & 0xFF);
            compressed[2] = (byte)((uncompressedSize >> 8) & 0xFF);
            compressed[3] = (byte)((uncompressedSize >> 16) & 0xFF);
            int src = 0, dst = 4;
            while (src < raw.Length)
            {
                compressed[dst++] = 0x00;
                int count = Math.Min(8, raw.Length - src);
                Array.Copy(raw, src, compressed, dst, count);
                src += count;
                dst += count;
            }
            return compressed;
        }

        static void SeedPointer(ROM rom, uint pointerEntryAddr, uint dataAddr, byte[] compressed)
        {
            rom.write_p32(pointerEntryAddr, dataAddr);
            rom.write_range(dataAddr, compressed);
        }

        static void AssertZeroFilled(ROM rom, uint addr, uint length)
        {
            for (uint i = 0; i < length; i++)
                Assert.Equal(0x00, rom.Data[addr + i]);
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_Fit_ReusesPointerAndZeroFillsSlack()
        {
            var rom = CreateRom();
            const uint ptr = 0x200;
            const uint oldAddr = 0x900;
            byte[] oldBlob = LiteralLz77(0x10, 64);
            byte[] newBlob = LiteralLz77(0x40, 16);
            uint oldSize = U.Padding4(LZ77.getCompressedSize(oldBlob, 0));
            SeedPointer(rom, ptr, oldAddr, oldBlob);

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);

            Assert.Equal(oldAddr, written);
            Assert.Equal(oldAddr, rom.p32(ptr));
            Assert.Equal(newBlob, rom.getBinaryData(oldAddr, (uint)newBlob.Length));
            AssertZeroFilled(rom, oldAddr + (uint)newBlob.Length, oldSize - (uint)newBlob.Length);
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_GrowthRelocatesAndFreesOldPrivateBlob()
        {
            var rom = CreateRom();
            const uint ptr = 0x204;
            const uint oldAddr = 0xA00;
            byte[] oldBlob = LiteralLz77(0x20, 8);
            byte[] newBlob = LiteralLz77(0x50, 128);
            uint oldSize = U.Padding4(LZ77.getCompressedSize(oldBlob, 0));
            SeedPointer(rom, ptr, oldAddr, oldBlob);

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.NotEqual(oldAddr, written);
            Assert.Equal(written, rom.p32(ptr));
            Assert.Equal(newBlob, rom.getBinaryData(written, (uint)newBlob.Length));
            AssertZeroFilled(rom, oldAddr, oldSize);
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_SharedGrowthDetachesWithoutZeroingSiblingBlob()
        {
            var rom = CreateRom();
            const uint ptrA = 0x208;
            const uint ptrB = 0x20C;
            const uint sharedAddr = 0xB00;
            byte[] oldBlob = LiteralLz77(0x30, 8);
            byte[] newBlob = LiteralLz77(0x60, 128);
            SeedPointer(rom, ptrA, sharedAddr, oldBlob);
            rom.write_p32(ptrB, sharedAddr);
            byte[] beforeSibling = rom.getBinaryData(sharedAddr, (uint)oldBlob.Length);

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptrA, newBlob);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.NotEqual(sharedAddr, written);
            Assert.Equal(written, rom.p32(ptrA));
            Assert.Equal(sharedAddr, rom.p32(ptrB));
            Assert.Equal(beforeSibling, rom.getBinaryData(sharedAddr, (uint)oldBlob.Length));
            Assert.Equal(newBlob, rom.getBinaryData(written, (uint)newBlob.Length));
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_RepeatedPaintSizedWrites_DoNotConsumeFreeSpaceOrCorruptSentinel()
        {
            var rom = CreateRom();
            const uint ptr = 0x210;
            const uint oldAddr = 0xC00;
            const uint sentinelAddr = 0x1800;
            byte[] sentinel = Enumerable.Repeat((byte)0x5C, 64).ToArray();
            rom.write_range(sentinelAddr, sentinel);
            byte[] oldBlob = LiteralLz77(0x11, 128);
            SeedPointer(rom, ptr, oldAddr, oldBlob);

            int freeBefore = rom.Data.Skip(0x4000).Take(0x1000).Count(b => b == 0x00);
            uint firstAddr = 0;
            for (int i = 0; i < 64; i++)
            {
                byte[] next = LiteralLz77((byte)(0x20 + i), 96);
                uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, next);
                if (i == 0) firstAddr = written;
                Assert.Equal(firstAddr, written);
                Assert.Equal(oldAddr, written);
                Assert.Equal(oldAddr, rom.p32(ptr));
            }
            int freeAfter = rom.Data.Skip(0x4000).Take(0x1000).Count(b => b == 0x00);

            Assert.Equal(freeBefore, freeAfter);
            Assert.Equal(sentinel, rom.getBinaryData(sentinelAddr, (uint)sentinel.Length));
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_AmbientUndoRestoresPointerAndBytes()
        {
            var rom = CreateRom();
            const uint ptr = 0x214;
            const uint oldAddr = 0xD00;
            byte[] oldBlob = LiteralLz77(0x70, 8);
            byte[] newBlob = LiteralLz77(0x80, 128);
            SeedPointer(rom, ptr, oldAddr, oldBlob);
            byte[] beforeRom = rom.Data.ToArray();

            var undo = new Undo();
            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "compressed-write",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = (uint)rom.Data.Length
            };

            using (ROM.BeginUndoScope(ud))
            {
                uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);
                Assert.NotEqual(U.NOT_FOUND, written);
                Assert.NotEqual(oldAddr, written);
            }

            undo.Rollback(ud);

            Assert.Equal(oldAddr, rom.p32(ptr));
            Assert.Equal(beforeRom, rom.Data);
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_OldPointerThreeBytesFromEof_DoesNotThrowAndRelocatesCleanly()
        {
            var rom = CreateRom();
            const uint ptr = 0x218;
            uint oldAddr = (uint)rom.Data.Length - 3;
            rom.write_p32(ptr, oldAddr);
            rom.write_u8(oldAddr, 0x10);
            byte[] tailBefore = rom.getBinaryData(oldAddr, 3);
            byte[] newBlob = LiteralLz77(0x90, 32);

            var ex = Record.Exception(() =>
            {
                uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);
                Assert.NotEqual(U.NOT_FOUND, written);
                Assert.NotEqual(oldAddr, written);
                Assert.Equal(written, rom.p32(ptr));
                Assert.Equal(newBlob, rom.getBinaryData(written, (uint)newBlob.Length));
            });

            Assert.Null(ex);
            Assert.Equal(tailBefore, rom.getBinaryData(oldAddr, 3));
        }

        static ROM CreateFilledRom(uint length, byte fill = 0xAA)
        {
            byte[] data = new byte[length];
            Array.Fill(data, fill);
            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic-boundary.gba", data, "NAZO"));
            CoreState.ROM = rom;
            return rom;
        }

        // #2017: FindAndWriteData's generic mid-ROM 0x00/0xFF scan cannot tell a
        // genuinely free run from a live pointer-referenced blob (e.g. a
        // legitimately all-zero map/tile block). These tests seed such a
        // referenced range inside the relocation search window and assert the
        // relocation path never touches it — it must append at the aligned ROM
        // end instead. They fail against the pre-fix code, which happily reuses
        // the referenced range and reports success.
        [Fact]
        public void WriteCompressedInPlaceOrRelocate_Growth_AppendsInsteadOfOverwritingReferencedInteriorData()
        {
            var rom = CreateRom();
            const uint ptr = 0x220;
            const uint oldAddr = 0xE00;
            const uint referencedPointerAddr = 0x140;
            const uint referencedTargetAddr = 0x4010; // inside the CreateRom() zero-fill interior range
            byte[] oldBlob = LiteralLz77(0xA0, 8);
            byte[] newBlob = LiteralLz77(0xB0, 256);
            SeedPointer(rom, ptr, oldAddr, oldBlob);
            rom.write_p32(referencedPointerAddr, referencedTargetAddr);
            byte[] referencedBefore = rom.getBinaryData(referencedTargetAddr, 64);
            uint expectedAppendAddr = U.Padding4((uint)rom.Data.Length);

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.Equal(expectedAppendAddr, written);
            Assert.Equal(written, rom.p32(ptr));
            Assert.Equal(newBlob, rom.getBinaryData(written, (uint)newBlob.Length));
            Assert.Equal(referencedBefore, rom.getBinaryData(referencedTargetAddr, 64));
            Assert.Equal(referencedTargetAddr, rom.p32(referencedPointerAddr));
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_SharedBlob_AppendsAndPreservesSiblingAndInteriorTarget()
        {
            var rom = CreateRom();
            const uint ptrA = 0x224;
            const uint ptrB = 0x228;
            const uint sharedAddr = 0xF00;
            const uint referencedPointerAddr = 0x144;
            const uint referencedTargetAddr = 0x4020; // inside the CreateRom() zero-fill interior range
            byte[] oldBlob = LiteralLz77(0x30, 8);
            byte[] newBlob = LiteralLz77(0x60, 256);
            SeedPointer(rom, ptrA, sharedAddr, oldBlob);
            rom.write_p32(ptrB, sharedAddr);
            rom.write_p32(referencedPointerAddr, referencedTargetAddr);
            byte[] beforeSibling = rom.getBinaryData(sharedAddr, (uint)oldBlob.Length);
            byte[] referencedBefore = rom.getBinaryData(referencedTargetAddr, 64);
            uint expectedAppendAddr = U.Padding4((uint)rom.Data.Length);

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptrA, newBlob);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.Equal(expectedAppendAddr, written);
            Assert.Equal(written, rom.p32(ptrA));
            Assert.Equal(sharedAddr, rom.p32(ptrB));
            Assert.Equal(beforeSibling, rom.getBinaryData(sharedAddr, (uint)oldBlob.Length));
            Assert.Equal(newBlob, rom.getBinaryData(written, (uint)newBlob.Length));
            Assert.Equal(referencedBefore, rom.getBinaryData(referencedTargetAddr, 64));
            Assert.Equal(referencedTargetAddr, rom.p32(referencedPointerAddr));
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_ExactCapAppend_SucceedsAndEndsAtThirtyTwoMebibyteCap()
        {
            byte[] newBlob = LiteralLz77(0xC0, 32);
            uint needSize = U.Padding4((uint)newBlob.Length);
            uint romLength = 0x02000000 - needSize; // already 4-aligned
            var rom = CreateFilledRom(romLength);
            const uint ptr = 0x230;
            const uint oldAddr = 0x300;
            byte[] oldBlob = LiteralLz77(0xD0, 8);
            SeedPointer(rom, ptr, oldAddr, oldBlob);

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);

            Assert.NotEqual(U.NOT_FOUND, written);
            Assert.Equal(romLength, written);
            Assert.Equal(0x02000000u, (uint)rom.Data.Length);
            Assert.Equal(written, rom.p32(ptr));
            Assert.Equal(newBlob, rom.getBinaryData(written, (uint)newBlob.Length));
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_UnalignedNearCapOverflow_ReturnsNotFoundWithoutMutation()
        {
            const uint romLength = 0x01FFFFFD; // unaligned; Padding4() rounds up to the 32MB cap already
            var rom = CreateFilledRom(romLength);
            const uint ptr = 0x234;
            const uint oldAddr = 0x340;
            byte[] oldBlob = LiteralLz77(0xE0, 3); // padded compressed size 8: smaller than newBlob below, forcing relocation
            SeedPointer(rom, ptr, oldAddr, oldBlob);
            byte[] oldBlobBefore = rom.getBinaryData(oldAddr, (uint)oldBlob.Length);
            byte[] newBlob = LiteralLz77(0xE8, 4); // small, but padding alone already exceeds the cap

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);

            Assert.Equal(U.NOT_FOUND, written);
            Assert.Equal(romLength, (uint)rom.Data.Length);
            Assert.Equal(oldAddr, rom.p32(ptr));
            Assert.Equal(oldBlobBefore, rom.getBinaryData(oldAddr, (uint)oldBlob.Length));
        }

        // Mirrors the maintainer-confirmed #2017 scenario: a near-cap ROM whose
        // relocation search window still contains a pointer-referenced
        // zero-filled interior range. The pre-fix code silently reuses that
        // range (reporting success while corrupting live data) instead of
        // hitting the 32MB cap. The fix must fail atomically: no pointer write,
        // no payload write, no old-blob clear, and the referenced range and ROM
        // length are byte-for-byte and length-for-length unchanged.
        [Fact]
        public void WriteCompressedInPlaceOrRelocate_NearCapWithReferencedInteriorRange_FailsAtomicallyWithoutMutation()
        {
            const uint romLength = 0x01FFFFF0;
            var rom = CreateFilledRom(romLength);
            const uint interiorFreeAddr = 0x01000000;
            const uint interiorFreeLength = 0x1000;
            const uint referencedPointerAddr = 0x400;
            const uint referencedTargetAddr = interiorFreeAddr + 0x10;
            rom.write_fill(interiorFreeAddr, interiorFreeLength, 0x00);
            rom.write_p32(referencedPointerAddr, referencedTargetAddr);
            byte[] referencedBefore = rom.getBinaryData(referencedTargetAddr, 64);

            const uint ptr = 0x404;
            const uint oldAddr = 0x500;
            byte[] oldBlob = LiteralLz77(0xF0, 8);
            SeedPointer(rom, ptr, oldAddr, oldBlob);
            byte[] oldBlobBefore = rom.getBinaryData(oldAddr, (uint)oldBlob.Length);
            byte[] newBlob = LiteralLz77(0xF8, 24); // compressed+padded size is exactly 0x20
            Assert.Equal(0x20u, U.Padding4((uint)newBlob.Length));
            uint beforeLength = (uint)rom.Data.Length;

            uint written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);

            Assert.Equal(U.NOT_FOUND, written);
            Assert.Equal(beforeLength, (uint)rom.Data.Length);
            Assert.Equal(oldAddr, rom.p32(ptr));
            Assert.Equal(oldBlobBefore, rom.getBinaryData(oldAddr, (uint)oldBlob.Length));
            Assert.Equal(referencedBefore, rom.getBinaryData(referencedTargetAddr, 64));
            Assert.Equal(referencedTargetAddr, rom.p32(referencedPointerAddr));
        }

        [Fact]
        public void WriteCompressedInPlaceOrRelocate_AmbientUndoRestoresPointerBytesAndRomLength_AfterAppendRelocation()
        {
            var rom = CreateRom();
            const uint ptr = 0x238;
            const uint oldAddr = 0x1000;
            byte[] oldBlob = LiteralLz77(0x60, 8);
            byte[] newBlob = LiteralLz77(0x65, 300);
            SeedPointer(rom, ptr, oldAddr, oldBlob);
            byte[] beforeRom = rom.Data.ToArray();
            uint beforeLength = (uint)rom.Data.Length;

            var undo = new Undo();
            var ud = new Undo.UndoData
            {
                time = DateTime.Now,
                name = "compressed-write-append",
                list = new System.Collections.Generic.List<Undo.UndoPostion>(),
                filesize = beforeLength
            };

            uint written;
            using (ROM.BeginUndoScope(ud))
            {
                written = ImageImportCore.WriteCompressedInPlaceOrRelocate(rom, ptr, newBlob);
            }

            Assert.NotEqual(U.NOT_FOUND, written);
            // The default CreateRom() interior zero-fill range would fit this
            // payload; growth proves the append path was used instead of that
            // ambiguous interior reuse.
            Assert.True(rom.Data.Length > beforeLength);
            Assert.Equal(written, rom.p32(ptr));

            undo.Rollback(ud);

            Assert.Equal(beforeLength, (uint)rom.Data.Length);
            Assert.Equal(oldAddr, rom.p32(ptr));
            Assert.Equal(beforeRom, rom.Data);
        }
    }
}
