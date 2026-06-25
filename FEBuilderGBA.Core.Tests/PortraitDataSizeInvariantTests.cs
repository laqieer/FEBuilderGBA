using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// #1411 — the data fact that makes the generic 28-byte Portrait Editor wrong on
    /// FE6: the FE6 portrait table is 16 bytes/entry, while FE7/FE8 are 28 bytes/entry.
    /// The generic Avalonia ImagePortraitViewModel hardcodes a 28-byte stride
    /// (SIZE = 28), so opening it on FE6 (where the real stride is 16) reads/writes
    /// past entry boundaries and silently corrupts the next entry on Write.
    ///
    /// These tests pin the per-version portrait_datasize so any future change that
    /// equalizes them (and would mask the bug) is caught here.
    /// </summary>
    public class PortraitDataSizeInvariantTests
    {
        private static ROM MakeRom(string versionString)
        {
            var rom = new ROM();
            var data = new byte[0x200_0000]; // 32 MB
            rom.LoadLow("fake.gba", data, versionString);
            return rom;
        }

        [Fact]
        public void FE6_PortraitDataSize_Is16()
        {
            var info = new ROMFE6JP(MakeRom("AE6J00"));
            Assert.Equal(16u, info.portrait_datasize);
        }

        [Fact]
        public void FE7JP_PortraitDataSize_Is28()
        {
            var info = new ROMFE7JP(MakeRom("AE7J00"));
            Assert.Equal(28u, info.portrait_datasize);
        }

        [Fact]
        public void FE7U_PortraitDataSize_Is28()
        {
            var info = new ROMFE7U(MakeRom("AE7E00"));
            Assert.Equal(28u, info.portrait_datasize);
        }

        [Fact]
        public void FE8JP_PortraitDataSize_Is28()
        {
            var info = new ROMFE8JP(MakeRom("BE8J00"));
            Assert.Equal(28u, info.portrait_datasize);
        }

        [Fact]
        public void FE8U_PortraitDataSize_Is28()
        {
            var info = new ROMFE8U(MakeRom("AE8EE4"));
            Assert.Equal(28u, info.portrait_datasize);
        }

        /// <summary>
        /// The crux of #1411: FE6's portrait stride differs from the generic editor's
        /// hardcoded SIZE (28). Any editor that assumes 28 bytes must NOT be used on
        /// a ROM whose portrait_datasize != 28 (FE6 == 16).
        /// </summary>
        [Fact]
        public void FE6_PortraitStride_DiffersFromGeneric28ByteEditor()
        {
            var fe6 = new ROMFE6JP(MakeRom("AE6J00"));
            var fe8 = new ROMFE8U(MakeRom("AE8EE4"));

            Assert.NotEqual(28u, fe6.portrait_datasize); // FE6 is NOT 28 → generic editor unsafe
            Assert.Equal(28u, fe8.portrait_datasize);    // FE8 is 28 → generic editor safe
        }
    }
}
