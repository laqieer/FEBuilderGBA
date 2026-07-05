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
    }
}
