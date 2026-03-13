using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class ClassFE6LayoutTests
    {
        /// <summary>
        /// Creates a ROM with enough zero-filled data to survive ROMFE constructor
        /// probing (they read addresses in the first ~1MB to detect patches).
        /// </summary>
        private static ROM MakeRom()
        {
            var rom = new ROM();
            // LoadLow expects a version string to select the ROMFEINFO subclass,
            // so we set Data directly via reflection or use a large enough buffer.
            // The ROMFE constructors read from rom.Data, so we need it populated.
            var data = new byte[0x200_0000]; // 32 MB (standard GBA ROM size)
            rom.LoadLow("fake.gba", data, "AE8EE4"); // FE8U version string
            return rom;
        }

        private static ROM MakeRomFE6()
        {
            var rom = new ROM();
            var data = new byte[0x200_0000];
            rom.LoadLow("fake.gba", data, "AE6J00"); // FE6 version string
            return rom;
        }

        private static ROM MakeRomFE7U()
        {
            var rom = new ROM();
            var data = new byte[0x200_0000];
            rom.LoadLow("fake.gba", data, "AE7E00"); // FE7U version string
            return rom;
        }

        [Fact]
        public void FE6_ClassDataSize_Is72()
        {
            var rom = MakeRomFE6();
            // The ROMFE6JP constructor sets class_datasize = 72
            var info = new ROMFE6JP(rom);
            Assert.Equal(72u, info.class_datasize);
        }

        [Fact]
        public void FE7U_ClassDataSize_Is84()
        {
            var rom = MakeRomFE7U();
            var info = new ROMFE7U(rom);
            Assert.Equal(84u, info.class_datasize);
        }

        [Fact]
        public void FE8U_ClassDataSize_Is84()
        {
            var rom = MakeRom();
            var info = new ROMFE8U(rom);
            Assert.Equal(84u, info.class_datasize);
        }

        [Fact]
        public void FE6_Has_Smaller_ClassStruct_Than_FE78()
        {
            var fe6 = new ROMFE6JP(MakeRomFE6());
            var fe7u = new ROMFE7U(MakeRomFE7U());
            var fe8u = new ROMFE8U(MakeRom());

            // FE6 struct is smaller -- no rain/snow move cost pointers
            Assert.True(fe6.class_datasize < fe7u.class_datasize,
                $"FE6 class struct ({fe6.class_datasize}) should be smaller than FE7 ({fe7u.class_datasize})");
            Assert.True(fe6.class_datasize < fe8u.class_datasize,
                $"FE6 class struct ({fe6.class_datasize}) should be smaller than FE8 ({fe8u.class_datasize})");
        }

        [Fact]
        public void FE6_Version_Is6()
        {
            var info = new ROMFE6JP(MakeRomFE6());
            Assert.Equal(6, info.version);
        }

        [Fact]
        public void FE7U_Version_Is7()
        {
            var info = new ROMFE7U(MakeRomFE7U());
            Assert.Equal(7, info.version);
        }

        [Fact]
        public void FE8U_Version_Is8()
        {
            var info = new ROMFE8U(MakeRom());
            Assert.Equal(8, info.version);
        }

        [Fact]
        public void FE6_ClassDataSize_Difference_Is12()
        {
            // FE7/8 have 3 extra pointer fields (rain/snow/unknown move costs) = 12 bytes
            var fe6 = new ROMFE6JP(MakeRomFE6());
            var fe7u = new ROMFE7U(MakeRomFE7U());
            Assert.Equal(12u, fe7u.class_datasize - fe6.class_datasize);
        }
    }
}
