using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1124 — stream-based ROM load/save (LoadFromStream/SaveToStream + async).
    /// These guard that the new stream seam is byte-for-byte identical to the
    /// existing path Load/Save (used on Android where a SAF content:// pick has
    /// no local filesystem path so the bytes must be read/written via streams).
    /// </summary>
    public class RomStreamIoTests
    {
        // Build the smallest buffer LoadLow accepts as a real ROM: FE8U needs
        // >= 0x1000000 bytes with the 6-byte game code "BE8E01" at 0x080000AC
        // (offset 0xAC). Mirrors the AsmMapSymbolFileTests fixture.
        static byte[] MakeFe8uBuffer()
        {
            byte[] data = new byte[0x1000000];
            byte[] code = System.Text.Encoding.ASCII.GetBytes("BE8E01");
            uint off = U.toOffset(0x080000AC); // 0xAC
            Array.Copy(code, 0, data, (int)off, code.Length);
            // Sprinkle a few non-zero bytes so byte-identity assertions are
            // meaningful (not all zeros).
            for (int i = 0; i < 256; i++) data[0x200 + i] = (byte)(i & 0xFF);
            return data;
        }

        [Fact]
        public void LoadFromStream_MatchesPathLoad_ByteIdentical()
        {
            byte[] buffer = MakeFe8uBuffer();
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, buffer);

                var rom1 = new ROM();
                bool ok1 = rom1.Load(tempFile, out string version1);

                var rom2 = new ROM();
                bool ok2;
                using (var ms = new MemoryStream(buffer))
                {
                    ok2 = rom2.LoadFromStream(ms, "BE8E01.gba", out string version2);
                    Assert.Equal(version1, version2);
                }

                Assert.True(ok1);
                Assert.True(ok2);
                Assert.Equal("BE8E01", version1);
                Assert.True(rom1.Data.SequenceEqual(rom2.Data));
                Assert.Equal(rom1.RomInfo.GetType(), rom2.RomInfo.GetType());
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task LoadFromStreamAsync_MatchesSync()
        {
            byte[] buffer = MakeFe8uBuffer();

            var romSync = new ROM();
            bool okSync = romSync.LoadFromStream(new MemoryStream(buffer), "BE8E01.gba", out string versionSync);

            var romAsync = new ROM();
            (bool ok, string version) result;
            using (var ms = new MemoryStream(buffer))
            {
                result = await romAsync.LoadFromStreamAsync(ms, "BE8E01.gba");
            }

            Assert.True(okSync);
            Assert.True(result.ok);
            Assert.Equal(versionSync, result.version);
            Assert.True(romSync.Data.SequenceEqual(romAsync.Data));
        }

        [Fact]
        public void SaveToStream_MatchesPathSave_ByteIdentical()
        {
            byte[] buffer = MakeFe8uBuffer();
            var rom = new ROM();
            Assert.True(rom.LoadFromStream(new MemoryStream(buffer), "BE8E01.gba", out _));

            string tempFile = Path.GetTempFileName();
            try
            {
                rom.Save(tempFile, false);
                byte[] pathBytes = File.ReadAllBytes(tempFile);

                byte[] streamBytes;
                using (var ms = new MemoryStream())
                {
                    rom.SaveToStream(ms);
                    streamBytes = ms.ToArray();
                }

                Assert.True(pathBytes.SequenceEqual(streamBytes));
                Assert.True(streamBytes.SequenceEqual(rom.Data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SaveToStream_Silent_PreservesModifiedSemantics()
        {
            byte[] buffer = MakeFe8uBuffer();

            // silent=true must NOT clear Modified.
            var romSilent = new ROM();
            Assert.True(romSilent.LoadFromStream(new MemoryStream(buffer), "BE8E01.gba", out _));
            romSilent.write_u8(0x200, 0x99); // dirties Modified=true
            Assert.True(romSilent.Modified);
            using (var ms = new MemoryStream())
            {
                romSilent.SaveToStream(ms, true);
            }
            Assert.True(romSilent.Modified);

            // silent=false (default) clears Modified — mirrors Save(name, false).
            var romNotSilent = new ROM();
            Assert.True(romNotSilent.LoadFromStream(new MemoryStream(buffer), "BE8E01.gba", out _));
            romNotSilent.write_u8(0x200, 0x99);
            Assert.True(romNotSilent.Modified);
            using (var ms = new MemoryStream())
            {
                romNotSilent.SaveToStream(ms);
            }
            Assert.False(romNotSilent.Modified);
        }

        [Fact]
        public async Task SaveToStreamAsync_MatchesSync()
        {
            byte[] buffer = MakeFe8uBuffer();
            var rom = new ROM();
            Assert.True(rom.LoadFromStream(new MemoryStream(buffer), "BE8E01.gba", out _));

            byte[] syncBytes;
            using (var ms = new MemoryStream())
            {
                rom.SaveToStream(ms);
                syncBytes = ms.ToArray();
            }

            byte[] asyncBytes;
            using (var ms = new MemoryStream())
            {
                await rom.SaveToStreamAsync(ms);
                asyncBytes = ms.ToArray();
            }

            Assert.True(syncBytes.SequenceEqual(asyncBytes));
            Assert.True(asyncBytes.SequenceEqual(rom.Data));
        }

        // #1124 Copilot review — SaveToStream into a pre-filled LARGER seekable
        // stream (the real SAF OpenWriteAsync write-back case) must truncate to
        // exactly Data.Length so no stale trailing bytes remain (corrupt ROM).
        [Fact]
        public void SaveToStream_IntoPrefilledLargerStream_TruncatesToExactLength()
        {
            byte[] buffer = MakeFe8uBuffer();
            var rom = new ROM();
            Assert.True(rom.LoadFromStream(new MemoryStream(buffer), "BE8E01.gba", out _));

            using var ms = new MemoryStream();
            // Pre-fill so Length = Data.Length + 123 and position is at the end.
            byte[] filler = new byte[rom.Data.Length + 123];
            for (int i = 0; i < filler.Length; i++) filler[i] = 0xCC;
            ms.Write(filler, 0, filler.Length);
            Assert.Equal(rom.Data.Length + 123, ms.Length);

            rom.SaveToStream(ms);

            Assert.Equal(rom.Data.Length, ms.Length);
            Assert.True(ms.ToArray().SequenceEqual(rom.Data));
        }

        [Fact]
        public async Task SaveToStreamAsync_IntoPrefilledLargerStream_TruncatesToExactLength()
        {
            byte[] buffer = MakeFe8uBuffer();
            var rom = new ROM();
            Assert.True(rom.LoadFromStream(new MemoryStream(buffer), "BE8E01.gba", out _));

            using var ms = new MemoryStream();
            byte[] filler = new byte[rom.Data.Length + 123];
            for (int i = 0; i < filler.Length; i++) filler[i] = 0xCC;
            await ms.WriteAsync(filler, 0, filler.Length);
            Assert.Equal(rom.Data.Length + 123, ms.Length);

            await rom.SaveToStreamAsync(ms);

            Assert.Equal(rom.Data.Length, ms.Length);
            Assert.True(ms.ToArray().SequenceEqual(rom.Data));
        }

        // #1124 Copilot review — LoadFromStream must rewind a seekable input that
        // is not at position 0 (path Load always reads from the start), otherwise
        // it would load a truncated ROM.
        [Fact]
        public void LoadFromStream_NonZeroPosition_RewindsAndLoadsFull()
        {
            byte[] buffer = MakeFe8uBuffer();
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, buffer);
                var romPath = new ROM();
                Assert.True(romPath.Load(tempFile, out string versionPath));

                using var ms = new MemoryStream();
                ms.Write(buffer, 0, buffer.Length);
                ms.Position = 100; // non-zero — must be rewound by the loader

                var romStream = new ROM();
                bool ok = romStream.LoadFromStream(ms, "BE8E01.gba", out string versionStream);

                Assert.True(ok);
                Assert.Equal(versionPath, versionStream);
                Assert.True(romPath.Data.SequenceEqual(romStream.Data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task LoadFromStreamAsync_NonZeroPosition_RewindsAndLoadsFull()
        {
            byte[] buffer = MakeFe8uBuffer();
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, buffer);
                var romPath = new ROM();
                Assert.True(romPath.Load(tempFile, out string versionPath));

                using var ms = new MemoryStream();
                ms.Write(buffer, 0, buffer.Length);
                ms.Position = 100;

                var romStream = new ROM();
                var result = await romStream.LoadFromStreamAsync(ms, "BE8E01.gba");

                Assert.True(result.ok);
                Assert.Equal(versionPath, result.version);
                Assert.True(romPath.Data.SequenceEqual(romStream.Data));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
