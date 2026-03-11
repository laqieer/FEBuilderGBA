using System;
using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class TranslateCoreTests
    {
        // ================================================================ ValidateRoundTrip null guards

        [Fact]
        public void ValidateRoundTrip_NullRom_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => TranslateCore.ValidateRoundTrip(null));
        }

        [Fact]
        public void ValidateRoundTrip_RomWithNullRomInfo_ThrowsArgumentException()
        {
            var rom = new ROM();
            Assert.Throws<ArgumentException>(() => TranslateCore.ValidateRoundTrip(rom));
        }

        // ================================================================ TextRoundTripResult logic

        [Fact]
        public void TextRoundTripResult_IsLossless_WhenNoMismatches()
        {
            var result = new TranslateCore.TextRoundTripResult
            {
                TotalEntries = 10,
                MatchCount = 10,
                MismatchCount = 0,
            };
            Assert.True(result.IsLossless);
        }

        [Fact]
        public void TextRoundTripResult_IsNotLossless_WhenMismatchesExist()
        {
            var result = new TranslateCore.TextRoundTripResult
            {
                TotalEntries = 10,
                MatchCount = 9,
                MismatchCount = 1,
            };
            result.Mismatches.Add((5, "before", "after"));
            Assert.False(result.IsLossless);
        }

        [Fact]
        public void TextRoundTripResult_DefaultMismatches_IsEmptyList()
        {
            var result = new TranslateCore.TextRoundTripResult();
            Assert.NotNull(result.Mismatches);
            Assert.Empty(result.Mismatches);
            Assert.True(result.IsLossless);
        }

        // ================================================================ DumpTexts
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

        [Fact]
        public void ExportImportReExport_RoundTrip_FilesIdentical()
        {
            // Comprehensive edge cases: empty, newlines, tabs, control codes, long text
            var entries = new List<(uint textId, string text)>
            {
                (0, ""),
                (1, "Simple text"),
                (2, "Line1\nLine2\nLine3"),
                (3, "Tab\there\tand\there"),
                (4, "Mixed\nnewline\tand\ttab"),
                (5, "@0001@0002@0010@0100"),  // Control codes
                (6, "[LoadFace][0x001]"),       // FEditor format codes
                (7, new string('A', 500)),     // Long text
                (8, "Special chars: !@#$%^&*(){}|:<>?"),
                (9, "Unicode: café résumé naïve"),
                (10, "Backslash: C:\\Users\\data\\file"),
                (11, "Quotes: \"hello\" 'world'"),
                (12, "\n\n\n"),                // Only newlines
                (13, "\t\t"),                  // Only tabs
            };

            string pathA = System.IO.Path.GetTempFileName();
            string pathB = System.IO.Path.GetTempFileName();
            try
            {
                // Export → Import → Re-export
                TranslateCore.ExportToTSV(entries, pathA);
                var imported = TranslateCore.ImportFromTSV(pathA);
                TranslateCore.ExportToTSV(imported, pathB);

                // Files should be identical
                string contentA = System.IO.File.ReadAllText(pathA);
                string contentB = System.IO.File.ReadAllText(pathB);
                Assert.Equal(contentA, contentB);

                // Also verify entry-level round-trip
                Assert.Equal(entries.Count, imported.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    Assert.Equal(entries[i].textId, imported[i].textId);
                    Assert.Equal(entries[i].text, imported[i].text);
                }
            }
            finally
            {
                System.IO.File.Delete(pathA);
                System.IO.File.Delete(pathB);
            }
        }
    }
}
