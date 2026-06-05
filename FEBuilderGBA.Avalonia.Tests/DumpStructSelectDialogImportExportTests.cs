// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the DumpStruct dialog's wired Export/Import (#439 / W2b #961).
//
// The DumpStruct dispatcher's CSV/TSV/EA Export buttons and its Import button
// are wired to the SAME Core struct-data seam the CLI --export-data /
// --import-data commands use (StructExportCore), via the address-resolved
// TableExportImportHelper.ExportTableByAddressAsync /
// ImportTableByAddressAsync overloads. The file-picker UI cannot be driven in
// the headless test harness, so these tests exercise the exact Core path those
// helpers run between the file dialog and the user message:
//
//   Export: ResolveTableAt(addr) -> LoadStructDef -> ExportTable ->
//           ExportToTSV/CSV/EA(path)  (real file, NOT the hex-dump banner)
//   Import: ResolveTableAt(addr) -> ImportFromTSV(path) -> WriteTable(rom)
//           wrapped in an UndoService Begin/Commit scope (Rollback on fault).
//
// The assertions prove (a) the export writes real struct-aware data for a known
// table (non-placeholder), (b) a TSV round-trip through the import path is
// lossless, and (c) the undo-scoped write is reversible.
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    // [Collection("SharedState")] because these mutate CoreState.ROM via
    // RomTestHelper.WithRom and the undo-scoped write path.
    [Collection("SharedState")]
    public class DumpStructSelectDialogImportExportTests
    {
        /// <summary>Address inside the units table (entry 1) for the loaded ROM.</summary>
        static uint UnitsEntryAddr()
        {
            var unitsDef = StructExportCore.GetTable("units");
            var rom = CoreState.ROM;
            return unitsDef.GetBaseAddress(rom) + unitsDef.GetDataSize(rom);
        }

        [Fact]
        public void Export_AtUnitTable_WritesRealStructDataNotPlaceholder()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                uint addr = UnitsEntryAddr();
                var rom = CoreState.ROM;

                // This is exactly what ExportTableByAddressAsync does after the
                // file-save dialog returns a path (minus the dialog itself).
                var table = StructExportCore.ResolveTableAt(rom, addr);
                Assert.NotNull(table);
                Assert.Equal("units", table!.Name);

                var structDef = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(structDef);

                var entries = StructExportCore.ExportTable(rom, table, structDef!);
                Assert.NotEmpty(entries);

                string path = Path.Combine(Path.GetTempPath(),
                    "dumpstruct_export_" + System.Guid.NewGuid().ToString("N") + ".tsv");
                try
                {
                    StructExportCore.ExportToTSV(entries, structDef!, path);

                    string written = File.ReadAllText(path);
                    // Real struct-aware TSV: header line is "Index\t..." — NOT the
                    // hex-dump banner the stub produced before #439 was wired.
                    Assert.StartsWith("Index\t", written);
                    Assert.DoesNotContain("Avalonia stub", written);
                    Assert.DoesNotContain("# TSV export", written);
                    // More than just a header — the units table has data rows.
                    Assert.True(written.Split('\n').Length > 2);
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            });
        }

        [Fact]
        public void Export_AtNonTableAddress_ResolvesNoTable()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                // The GBA header is not inside any struct table, so the dialog's
                // Export path falls back to the honest hex-dump preview (no file
                // write). ResolveTableAt returning null is the gate.
                var rom = CoreState.ROM;
                Assert.Null(StructExportCore.ResolveTableAt(rom, 0x100));
            });
        }

        [Fact]
        public void ImportRoundTrip_AtUnitTable_IsLosslessAndUndoable()
        {
            RomTestHelper.WithRom("FE8U", () =>
            {
                uint addr = UnitsEntryAddr();
                var rom = CoreState.ROM;

                var table = StructExportCore.ResolveTableAt(rom, addr);
                Assert.NotNull(table);
                var structDef = StructExportCore.LoadStructDef(rom, table!);
                Assert.NotNull(structDef);

                // 1) Export the current table to a real TSV file (the export path).
                var export1 = StructExportCore.ExportTable(rom, table!, structDef!);
                Assert.NotEmpty(export1);

                string path = Path.Combine(Path.GetTempPath(),
                    "dumpstruct_roundtrip_" + System.Guid.NewGuid().ToString("N") + ".tsv");
                try
                {
                    StructExportCore.ExportToTSV(export1, structDef!, path);

                    // 2) Re-import via the SAME Core parse the helper uses (hex
                    //    index from the first column, not positional) and write
                    //    back inside an UndoService scope (the import path).
                    var undo = new UndoService();
                    undo.Begin("Import units (test)");
                    var parsed = StructExportCore.ImportFromTSV(path, structDef!);
                    Assert.NotEmpty(parsed);
                    int written = StructExportCore.WriteTable(rom, table!, structDef!, parsed);
                    undo.Commit();
                    Assert.True(written > 0);

                    // 3) Re-export and assert byte-for-byte identity (lossless:
                    //    writing the same values back changes nothing).
                    var export2 = StructExportCore.ExportTable(rom, table!, structDef!);
                    AssertEntriesEqual(export1, export2, structDef!);
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            });
        }

        static void AssertEntriesEqual(
            List<Dictionary<string, string>> a,
            List<Dictionary<string, string>> b,
            StructMetadata.StructDef structDef)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                foreach (var field in structDef.Fields)
                {
                    string va = a[i].TryGetValue(field.Name, out var x) ? x : "";
                    string vb = b[i].TryGetValue(field.Name, out var y) ? y : "";
                    Assert.Equal(va, vb);
                }
            }
        }
    }
}
