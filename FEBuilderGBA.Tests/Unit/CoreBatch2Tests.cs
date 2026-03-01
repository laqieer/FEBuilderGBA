using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    public class CoreBatch2Tests
    {
        [Fact]
        public void LZ77_Compress_Decompress_RoundTrip()
        {
            byte[] original = new byte[256];
            for (int i = 0; i < original.Length; i++)
                original[i] = (byte)(i % 16); // repetitive data compresses well

            byte[] compressed = LZ77.compress(original);
            Assert.NotNull(compressed);
            Assert.True(compressed.Length > 0);

            byte[] decompressed = LZ77.decompress(compressed, 0);
            Assert.Equal(original, decompressed);
        }

        [Fact]
        public void LZ77_IsCompress_DetectsCompressedData()
        {
            byte[] data = new byte[256];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)(i % 16);

            byte[] compressed = LZ77.compress(data);
            Assert.True(LZ77.iscompress(compressed, 0));
        }

        [Fact]
        public void LZ77_GetUncompressSize_ReturnsCorrectSize()
        {
            byte[] original = new byte[128];
            for (int i = 0; i < original.Length; i++)
                original[i] = (byte)(i % 8);

            byte[] compressed = LZ77.compress(original);
            uint size = LZ77.getUncompressSize(compressed, 0);
            Assert.Equal((uint)original.Length, size);
        }

        [Fact]
        public void RegexCache_Regex_CachesCompiledRegex()
        {
            var r1 = RegexCache.Regex(@"\d+");
            var r2 = RegexCache.Regex(@"\d+");
            Assert.Same(r1, r2); // same cached instance
        }

        [Fact]
        public void RegexCache_IsMatch_WorksCorrectly()
        {
            Assert.True(RegexCache.IsMatch("abc123", @"\d+"));
            Assert.False(RegexCache.IsMatch("abcdef", @"\d+"));
        }

        [Fact]
        public void RegexCache_Replace_WorksCorrectly()
        {
            string result = RegexCache.Replace("hello 123 world", @"\d+", "NUM");
            Assert.Equal("hello NUM world", result);
        }

        [Fact]
        public void RegexCache_MatchSimple_ExtractsGroup()
        {
            string result = RegexCache.MatchSimple("version=1.2.3", @"version=(\d+\.\d+\.\d+)");
            Assert.Equal("1.2.3", result);
        }

        [Fact]
        public void TextEscape_Find_KnownEscapeCodes()
        {
            var te = new TextEscape();
            Assert.True(te.Find("[NL]"));
            Assert.True(te.Find("[Clear]"));
            Assert.True(te.Find("[A]"));
            Assert.False(te.Find("[NonExistent]"));
        }

        [Fact]
        public void TextEscape_TableReplace_ConvertsEscapeCodes()
        {
            var te = new TextEscape();
            string result = te.table_replace("@0001");
            Assert.Equal("[NL]", result);
        }

        [Fact]
        public void TextEscape_TableReplaceRev_ConvertsBack()
        {
            var te = new TextEscape();
            string result = te.table_replace_rev("[NL]");
            Assert.Equal("@0001", result);
        }

        [Fact]
        public void TextEscape_Add_AddsNewEscapeCode()
        {
            var te = new TextEscape();
            te.Add("@00FF", "[Custom]", "Custom escape");
            Assert.True(te.Find("[Custom]"));
        }

        [Fact]
        public void TextEscape_GetAddEscapeMappingSnapshot_ReturnsAddedEntries()
        {
            var te = new TextEscape();
            te.Add("@00FF", "[Custom]", "Custom escape");
            var snapshot = te.GetAddEscapeMappingSnapshot();
            Assert.True(snapshot.ContainsKey("@00FF"));
            Assert.Equal("[Custom]", snapshot["@00FF"].feditorAdv);
            Assert.Equal("Custom escape", snapshot["@00FF"].info);
        }

        [Fact]
        public void Elf_Constructor_HandlesInvalidFile()
        {
            // Elf constructor should handle missing/invalid files gracefully
            string tempFile = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllBytes(tempFile, new byte[] { 0, 0, 0, 0 });
                var elf = new Elf(tempFile, useHookMode: false);
                // Invalid ELF — should have empty program binary
                Assert.Empty(elf.ProgramBIN);
                Assert.Empty(elf.SymList);
            }
            finally
            {
                System.IO.File.Delete(tempFile);
            }
        }

        [Fact]
        public void SystemTextEncoderTBLEncodeInterface_IsAccessible()
        {
            // Verify the interface moved to Core is accessible
            Assert.True(typeof(SystemTextEncoderTBLEncodeInterface).IsInterface);
            Assert.True(typeof(SystemTextEncoderTBLEncode).GetInterfaces().Length > 0);
        }

        [Fact]
        public void U_IsSJIS1stCode_DetectsValidRanges()
        {
            Assert.True(U.isSJIS1stCode(0x81));
            Assert.True(U.isSJIS1stCode(0x9F));
            Assert.True(U.isSJIS1stCode(0xE0));
            Assert.False(U.isSJIS1stCode(0x20));
            Assert.False(U.isSJIS1stCode(0x7F));
        }

        [Fact]
        public void U_IsSJIS2ndCode_DetectsValidRanges()
        {
            Assert.True(U.isSJIS2ndCode(0x40));
            Assert.True(U.isSJIS2ndCode(0x7E));
            Assert.True(U.isSJIS2ndCode(0x80));
            Assert.True(U.isSJIS2ndCode(0xFC));
            Assert.False(U.isSJIS2ndCode(0x3F));
        }

        [Fact]
        public void U_TableReplace_ReplacesInOrder()
        {
            string[] table = new string[] { "A", "X", "B", "Y" };
            Assert.Equal("XY", U.table_replace("AB", table));
        }

        [Fact]
        public void U_TableReplaceRev_ReversesReplacements()
        {
            string[] table = new string[] { "A", "X", "B", "Y" };
            Assert.Equal("AB", U.table_replace_rev("XY", table));
        }

        [Fact]
        public void U_AppendU16_AddsLittleEndian()
        {
            var data = new System.Collections.Generic.List<byte>();
            U.append_u16(data, 0x1234);
            Assert.Equal(new byte[] { 0x34, 0x12 }, data.ToArray());
        }
    }
}
