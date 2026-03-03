using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class TranslateCoreTests
    {
        [Fact]
        public void DumpTexts_NullRom_ReturnsEmpty()
        {
            var result = TranslateCore.DumpTexts(null);
            Assert.Empty(result);
        }

        [Fact]
        public void DumpTexts_RomWithNullRomInfo_ReturnsEmpty()
        {
            var rom = new ROM();
            var result = TranslateCore.DumpTexts(rom);
            Assert.Empty(result);
        }

        [Fact]
        public void GetTextCount_NullRom_ReturnsZero()
        {
            uint count = TranslateCore.GetTextCount(null);
            Assert.Equal(0u, count);
        }

        [Fact]
        public void ExportToTSV_WritesHeaderAndEntries()
        {
            var entries = new List<(uint textId, string text)>
            {
                (0, "Hello"),
                (1, "World\nLine2"),
            };
            string path = System.IO.Path.GetTempFileName();
            try
            {
                TranslateCore.ExportToTSV(entries, path);
                string content = System.IO.File.ReadAllText(path);
                Assert.Contains("ID\tText", content);
                Assert.Contains("0\tHello", content);
                Assert.Contains("1\tWorld\\nLine2", content);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ExportToTSV_EmptyList_WritesOnlyHeader()
        {
            var entries = new List<(uint textId, string text)>();
            string path = System.IO.Path.GetTempFileName();
            try
            {
                TranslateCore.ExportToTSV(entries, path);
                string[] lines = System.IO.File.ReadAllLines(path);
                Assert.Single(lines); // Only header line (trailing newline makes it one line)
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ExportToTSV_EscapesTabsAndNewlines()
        {
            var entries = new List<(uint textId, string text)>
            {
                (5, "Tab\there\nNewline"),
            };
            string path = System.IO.Path.GetTempFileName();
            try
            {
                TranslateCore.ExportToTSV(entries, path);
                string content = System.IO.File.ReadAllText(path);
                Assert.Contains("5\tTab\\there\\nNewline", content);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ImportFromTSV_ParsesHeaderAndEntries()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, "ID\tText\n0\tHello\n1\tWorld\\nLine2\n");
                var result = TranslateCore.ImportFromTSV(path);
                Assert.Equal(2, result.Count);
                Assert.Equal((uint)0, result[0].textId);
                Assert.Equal("Hello", result[0].text);
                Assert.Equal((uint)1, result[1].textId);
                Assert.Equal("World\nLine2", result[1].text);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ImportFromTSV_NonexistentFile_ReturnsEmpty()
        {
            var result = TranslateCore.ImportFromTSV("/nonexistent/path/file.tsv");
            Assert.Empty(result);
        }

        [Fact]
        public void ImportFromTSV_SkipsBlankLines()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, "ID\tText\n0\tHello\n\n2\tWorld\n");
                var result = TranslateCore.ImportFromTSV(path);
                Assert.Equal(2, result.Count);
                Assert.Equal((uint)0, result[0].textId);
                Assert.Equal((uint)2, result[1].textId);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ImportFromTSV_SkipsLinesWithoutTab()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, "ID\tText\n0\tHello\nmalformed line\n2\tWorld\n");
                var result = TranslateCore.ImportFromTSV(path);
                Assert.Equal(2, result.Count);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void ImportFromTSV_SkipsInvalidIds()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                System.IO.File.WriteAllText(path, "ID\tText\nabc\tHello\n1\tWorld\n");
                var result = TranslateCore.ImportFromTSV(path);
                Assert.Single(result);
                Assert.Equal((uint)1, result[0].textId);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void WriteTexts_NullRom_ReturnsZero()
        {
            var entries = new List<(uint textId, string text)> { (0, "test") };
            int count = TranslateCore.WriteTexts(null, entries);
            Assert.Equal(0, count);
        }

        [Fact]
        public void ExportImport_RoundTrip_PreservesData()
        {
            var original = new List<(uint textId, string text)>
            {
                (0, "First entry"),
                (1, "Second\nwith newline"),
                (2, "Third\twith tab"),
                (3, ""),
            };
            string path = System.IO.Path.GetTempFileName();
            try
            {
                TranslateCore.ExportToTSV(original, path);
                var imported = TranslateCore.ImportFromTSV(path);

                Assert.Equal(original.Count, imported.Count);
                for (int i = 0; i < original.Count; i++)
                {
                    Assert.Equal(original[i].textId, imported[i].textId);
                    Assert.Equal(original[i].text, imported[i].text);
                }
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
