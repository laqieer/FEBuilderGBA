// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the STRUCT (.h C-header) + NMM (No$gba memory map) pure formatters
// added to StructExportCore for #1012. These mirror the WinForms
// DumpStructSelectDialogForm.MakeStructString / MakNMMString output.
//
// Setup: a synthetic FE8U ROM (LoadLow + zeroed 16 MB) provides the RomInfo
// constants the units TableDef callbacks read (unit_datasize / unit_maxcount).
// The real struct metadata is loaded from config/data via LoadStructDef, so the
// StructDef field list is exactly what production uses. The formatters are pure
// over StructDef + TableDef — they never read table DATA — so a zeroed ROM is
// sufficient and the output is fully deterministic.
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class StructExportCoreStructNmmTests
    {
        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "config", "data")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
                if (dir == null) break;
            }
            return null;
        }

        static void EnsureBaseDirectory()
        {
            if (CoreState.BaseDirectory != null) return;
            string root = FindRepoRoot();
            if (root != null)
                CoreState.BaseDirectory = root;
        }

        /// <summary>
        /// Build a synthetic FE8U ROM, install it into CoreState, run the action,
        /// then restore prior CoreState. FAILS LOUDLY if config/data (the source of
        /// the struct metadata) is not reachable — a silent no-op here would let the
        /// whole assertion body be skipped and report a false-positive pass. The
        /// repo-root config/data is present in CI and every dev checkout.
        /// </summary>
        static void WithSyntheticFE8U(Action<ROM> action)
        {
            EnsureBaseDirectory();
            Assert.True(CoreState.BaseDirectory != null,
                "config/data BaseDirectory must resolve so LoadStructDef can read the struct metadata "
                + "(repo-root config/data is present in CI and dev checkouts); refusing to no-op this test.");

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                bool ok = rom.LoadLow("x.gba", new byte[0x1000000], "BE8E01");
                Assert.True(ok, "synthetic FE8U ROM must load");
                CoreState.ROM = rom;
                action(rom);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>Assert the file does NOT begin with a UTF-8 BOM (EF BB BF).</summary>
        static void AssertNoUtf8Bom(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            Assert.False(hasBom, "exported file must not start with a UTF-8 BOM");
        }

        // ===================================================================
        // FormatSTRUCT — C-header layout.
        // ===================================================================

        [Fact]
        public void FormatSTRUCT_UnitsTable_HasHeaderTypesAndFooter()
        {
            WithSyntheticFE8U(rom =>
            {
                var table = StructExportCore.GetTable("units");
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);

                string text = StructExportCore.FormatSTRUCT(sd);

                // First line: struct {Name} {//{Name}
                Assert.StartsWith("struct " + sd.Name, text);
                Assert.Contains("struct " + sd.Name + " {//" + sd.Name, text);

                // One correct C type per FieldType present in the canonical unit layout.
                Assert.Contains("byte    _", text);    // Byte
                Assert.Contains("ushort   _", text);   // Word
                Assert.Contains("void*   _", text);    // Pointer
                Assert.DoesNotContain("dword   _", text); // No synthetic padding field

                // Footer: }; sizeof({DataSize})
                Assert.Contains("}; sizeof(" + sd.DataSize + ")", text);
                Assert.EndsWith("}; sizeof(" + sd.DataSize + ")" + Environment.NewLine, text);

                // Honest-stub banner must never appear.
                Assert.DoesNotContain("Avalonia stub", text);
            });
        }

        [Fact]
        public void FormatSTRUCT_EmitsDecimalOffsetAndFieldNameComment()
        {
            WithSyntheticFE8U(rom =>
            {
                var table = StructExportCore.GetTable("units");
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);

                string text = StructExportCore.FormatSTRUCT(sd);

                // The first field (NameTextID, Word at offset 0) → ushort   _0;//NameTextID
                var first = sd.Fields[0];
                Assert.Equal("NameTextID", first.Name);
                Assert.Contains("ushort   _" + first.Offset + ";//" + first.Name, text);

                // A field at a non-zero offset must use its DECIMAL offset.
                // ClassID is a Byte at offset 0x05 → decimal 5.
                var classId = sd.Fields.Find(f => f.Name == "ClassID");
                Assert.NotNull(classId);
                Assert.Equal(5u, classId.Offset);
                Assert.Contains("byte    _5;//ClassID", text);
            });
        }

        // ===================================================================
        // FormatNMM — No$gba memory map.
        // ===================================================================

        [Fact]
        public void FormatNMM_UnitsTable_HasHeaderAndPerFieldBlock()
        {
            WithSyntheticFE8U(rom =>
            {
                var table = StructExportCore.GetTable("units");
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);

                string text = StructExportCore.FormatNMM(rom, table, sd);
                string[] lines = text.Replace("\r\n", "\n").Split('\n');

                // First line is exactly "1".
                Assert.Equal("1", lines[0]);
                // Title line.
                Assert.Equal(sd.Name + " by FEBuilderGBA", lines[1]);
                // 0x-prefixed base address.
                Assert.StartsWith("0x", lines[2]);
                Assert.Equal(U.To0xHexString(table.GetBaseAddress(rom)), lines[2]);
                // Entry count then block size.
                Assert.Equal(table.GetEntryCount(rom).ToString(), lines[3]);
                Assert.Equal(table.GetDataSize(rom).ToString(), lines[4]);
                // Two NULL lines + blank line round out the header.
                Assert.Equal("NULL", lines[5]);
                Assert.Equal("NULL", lines[6]);
                Assert.Equal("", lines[7]);

                // Per-field block for the first field: Name / decimal offset /
                // size code / NEHU / NULL.
                var first = sd.Fields[0];
                Assert.Equal(first.Name, lines[8]);
                Assert.Equal(first.Offset.ToString(), lines[9]);
                Assert.Equal(first.Size.ToString(), lines[10]); // Word → 2
                Assert.Equal("2", lines[10]);
                Assert.Equal("NEHU", lines[11]);
                Assert.Equal("NULL", lines[12]);

                // The right size code appears for the relevant field widths: a
                // Byte field's block carries Name / offset / "1" / NEHU / NULL.
                var byteField = sd.Fields.Find(f => f.Type == StructMetadata.FieldType.Byte);
                Assert.NotNull(byteField);
                Assert.Contains(byteField.Name + "\n" + byteField.Offset + "\n1\nNEHU\nNULL",
                    text.Replace("\r\n", "\n"));

                // A Pointer/DWord field's block carries size "4".
                var ptrField = sd.Fields.Find(f => f.Type == StructMetadata.FieldType.Pointer);
                Assert.NotNull(ptrField);
                Assert.Contains(ptrField.Name + "\n" + ptrField.Offset + "\n4\nNEHU\nNULL",
                    text.Replace("\r\n", "\n"));

                Assert.DoesNotContain("Avalonia stub", text);
            });
        }

        [Fact]
        public void FormatNMM_IsDeterministic()
        {
            WithSyntheticFE8U(rom =>
            {
                var table = StructExportCore.GetTable("units");
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);

                string a = StructExportCore.FormatNMM(rom, table, sd);
                string b = StructExportCore.FormatNMM(rom, table, sd);
                Assert.Equal(a, b);
            });
        }

        // ===================================================================
        // ExportToSTRUCT / ExportToNMM — file byte-identity.
        // ===================================================================

        [Fact]
        public void ExportToSTRUCT_MatchesFormatSTRUCT_ByteForByte()
        {
            WithSyntheticFE8U(rom =>
            {
                var table = StructExportCore.GetTable("units");
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);

                string path = Path.Combine(Path.GetTempPath(), $"struct_{Guid.NewGuid():N}.h");
                try
                {
                    StructExportCore.ExportToSTRUCT(sd, path);
                    Assert.Equal(StructExportCore.FormatSTRUCT(sd),
                        File.ReadAllText(path, System.Text.Encoding.UTF8));
                    // No UTF-8 BOM (external .h consumers + WF no-BOM parity).
                    AssertNoUtf8Bom(path);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        [Fact]
        public void ExportToNMM_MatchesFormatNMM_ByteForByte()
        {
            WithSyntheticFE8U(rom =>
            {
                var table = StructExportCore.GetTable("units");
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);

                string path = Path.Combine(Path.GetTempPath(), $"map_{Guid.NewGuid():N}.nmm");
                try
                {
                    StructExportCore.ExportToNMM(rom, table, sd, path);
                    Assert.Equal(StructExportCore.FormatNMM(rom, table, sd),
                        File.ReadAllText(path, System.Text.Encoding.UTF8));
                    // No UTF-8 BOM — the strict ".nmm" magic "1" must be byte 0.
                    AssertNoUtf8Bom(path);
                }
                finally
                {
                    File.Delete(path);
                }
            });
        }

        // ===================================================================
        // Divergence guard — the STRUCT footer + NMM block size both come from
        // DataSize, so the table's reported entry size and the struct metadata
        // size MUST agree or the two formatters would silently diverge.
        // ===================================================================

        [Fact]
        public void UnitsTable_DataSize_MatchesStructDefDataSize()
        {
            WithSyntheticFE8U(rom =>
            {
                var table = StructExportCore.GetTable("units");
                var sd = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(sd);
                Assert.Equal(table.GetDataSize(rom), sd.DataSize);
            });
        }
    }
}
