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
        public void Export_EmptyButValidTable_WritesHeaderOnlyFile_LikeCli()
        {
            // #963 review: ExportTableByAddressAsync must NOT throw "No data to
            // export." on a 0-entry table — the CLI --export-data writes a valid
            // HEADER-ONLY file in that case (StructExportCore.FormatTSV always
            // emits the header). This asserts the CLI-parity invariant the helper's
            // post-dialog path relies on: ExportToTSV with an empty entries list
            // still produces a header-only file (the removed guard would have
            // broken this).
            RomTestHelper.WithRom("FE8U", () =>
            {
                var table = StructExportCore.GetTable("units");
                var rom = CoreState.ROM;
                var structDef = StructExportCore.LoadStructDef(rom, table);
                Assert.NotNull(structDef);

                var empty = new List<Dictionary<string, string>>();

                string path = Path.Combine(Path.GetTempPath(),
                    "dumpstruct_emptyexport_" + System.Guid.NewGuid().ToString("N") + ".tsv");
                try
                {
                    StructExportCore.ExportToTSV(empty, structDef!, path);

                    string written = File.ReadAllText(path);
                    // Header line present, NO data rows.
                    Assert.StartsWith("Index\t", written);
                    var lines = written.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
                    Assert.Single(lines); // header only
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
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
                    //    index from the first column, not positional). Mutate ONE
                    //    field of one entry so the write produces a REAL byte delta
                    //    (re-writing identical bytes would record nothing, making
                    //    the undo assertion meaningless).
                    var parsed = StructExportCore.ImportFromTSV(path, structDef!);
                    Assert.NotEmpty(parsed);
                    var firstField = structDef!.Fields[0];
                    var (mutIndex, mutFields) = parsed[0];
                    string origVal = mutFields[firstField.Name];
                    uint origNum = U.atoi0x(origVal);
                    uint newNum = origNum ^ 0x1u; // flip the low bit → guaranteed-different, still in-range
                    // WriteTable parses each field via U.atoi0x, so a plain "0x.."
                    // hex string is sufficient (FormatFieldValue is private in Core).
                    string newVal = "0x" + newNum.ToString("X");
                    mutFields[firstField.Name] = newVal;

                    // Snapshot the ROM bytes BEFORE the undo-scoped import so we can
                    // assert undo restores them byte-for-byte.
                    byte[] before = (byte[])rom.Data.Clone();
                    int undoPosBefore = CoreState.Undo!.Postion;

                    // 3) Write back inside the SAME UndoService Begin/Commit scope the
                    //    import helper uses (the import path).
                    var undo = new UndoService();
                    undo.Begin("Import units (test)");
                    int written = StructExportCore.WriteTable(rom, table!, structDef!, parsed);
                    undo.Commit();
                    Assert.True(written > 0);

                    // 3a) Commit created exactly one undo record (position advanced).
                    Assert.Equal(undoPosBefore + 1, CoreState.Undo.Postion);
                    Assert.True(CoreState.Undo.IsModified);
                    // The mutation actually changed ROM bytes (so undo has work to do).
                    Assert.False(BytesEqual(before, rom.Data),
                        "the forced field mutation must change ROM bytes");
                    // The re-export reflects the mutated value (write took effect).
                    // Compare numerically — ExportTable re-formats via the canonical
                    // (private) formatter, which may differ in width from newVal.
                    var exportMut = StructExportCore.ExportTable(rom, table!, structDef!);
                    Assert.Equal(newNum, U.atoi0x(exportMut[(int)mutIndex][firstField.Name]));

                    // 4) RUN undo and assert the ROM is byte-identical to the
                    //    pre-import snapshot (the write is fully reversible).
                    CoreState.Undo.RunUndo();
                    Assert.Equal(undoPosBefore, CoreState.Undo.Postion);
                    Assert.True(BytesEqual(before, rom.Data),
                        "undo must restore the ROM byte-for-byte");

                    // 5) After undo, a fresh export equals the ORIGINAL (lossless):
                    //    the table is back exactly where it started.
                    var export2 = StructExportCore.ExportTable(rom, table!, structDef!);
                    AssertEntriesEqual(export1, export2, structDef!);
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            });
        }

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
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
