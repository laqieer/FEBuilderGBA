using FEBuilderGBA;
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
        // ROM.LoadLow detects the version from the header code substring. Use the
        // codes it actually recognizes (the "01" variants) so LoadLow sets RomInfo
        // to the correct ROMFE* subclass — and assert success so the helper is honest
        // (Copilot review: the earlier "AE6J00"/"AE8EE4" codes were NOT recognized and
        // made LoadLow return false, leaving RomInfo unset).
        private const string FE6 = "AFEJ01";
        private const string FE7JP = "AE7J01";
        private const string FE7U = "AE7E01";
        private const string FE8JP = "BE8J01";
        private const string FE8U = "BE8E01";

        /// <summary>
        /// Loads a zero-filled 32 MB ROM as the requested version via ROM.LoadLow and
        /// asserts the load succeeded so RomInfo is the expected ROMFE* subclass.
        /// </summary>
        private static ROMFEINFO MakeRomInfo(string versionCode)
        {
            var rom = new ROM();
            var data = new byte[0x200_0000]; // 32 MB (standard GBA ROM size)
            bool ok = rom.LoadLow("fake.gba", data, versionCode);
            Assert.True(ok, $"ROM.LoadLow should recognize version code '{versionCode}'");
            Assert.NotNull(rom.RomInfo);
            return rom.RomInfo;
        }

        [Fact]
        public void FE6_PortraitDataSize_Is16()
        {
            ROMFEINFO info = MakeRomInfo(FE6);
            Assert.IsType<ROMFE6JP>(info);
            Assert.Equal(16u, info.portrait_datasize);
        }

        [Fact]
        public void FE7JP_PortraitDataSize_Is28()
        {
            ROMFEINFO info = MakeRomInfo(FE7JP);
            Assert.IsType<ROMFE7JP>(info);
            Assert.Equal(28u, info.portrait_datasize);
        }

        [Fact]
        public void FE7U_PortraitDataSize_Is28()
        {
            ROMFEINFO info = MakeRomInfo(FE7U);
            Assert.IsType<ROMFE7U>(info);
            Assert.Equal(28u, info.portrait_datasize);
        }

        [Fact]
        public void FE8JP_PortraitDataSize_Is28()
        {
            ROMFEINFO info = MakeRomInfo(FE8JP);
            Assert.IsType<ROMFE8JP>(info);
            Assert.Equal(28u, info.portrait_datasize);
        }

        [Fact]
        public void FE8U_PortraitDataSize_Is28()
        {
            ROMFEINFO info = MakeRomInfo(FE8U);
            Assert.IsType<ROMFE8U>(info);
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
            ROMFEINFO fe6 = MakeRomInfo(FE6);
            ROMFEINFO fe8 = MakeRomInfo(FE8U);

            Assert.NotEqual(28u, fe6.portrait_datasize); // FE6 is NOT 28 → generic editor unsafe
            Assert.Equal(28u, fe8.portrait_datasize);    // FE8 is 28 → generic editor safe
        }
    }
}
