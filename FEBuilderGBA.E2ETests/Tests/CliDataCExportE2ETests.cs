using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E coverage for #1939 Phase B1 — <c>--export-data --format=c</c> (the GNU11
    /// raw-byte-backed struct/array formatter) and the <c>--c-symbol</c> single-table
    /// override on <c>FEBuilderGBA.CLI</c>.
    ///
    /// ROM-gated scenarios use a temporary copy of the relevant ROM(s) via
    /// <see cref="RomLocator"/> and skip via <see cref="SkippableFactAttribute"/> when a
    /// ROM is not available locally — the same pattern as <c>CliDataJsonE2ETests</c> /
    /// <c>RomCliTests</c>. The <c>--format</c>/<c>--c-symbol</c> validation tests run
    /// before any ROM load (proven with a deliberately nonexistent <c>--rom</c> path), so
    /// they are plain <see cref="FactAttribute"/>s that need no ROM at all. No ROM is
    /// bundled or downloaded by this test file itself.
    /// </summary>
    public class CliDataCExportE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempFiles = new();
        private readonly List<string> _tempDirs = new();

        public void Dispose()
        {
            foreach (var f in _tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
            foreach (var d in _tempDirs)
            {
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            }
        }

        /// <summary>Reserve a unique temp file path (not created) and register it for cleanup.</summary>
        private string TempFile(string ext)
        {
            var path = Path.Combine(Path.GetTempPath(), $"febuilder_cexport_{Guid.NewGuid():N}{ext}");
            _tempFiles.Add(path);
            return path;
        }

        /// <summary>Create a fresh, empty temp directory and register it for cleanup.</summary>
        private string TempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"febuilder_cexport_dir_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        /// <summary>Copy a shared ROM so each test mutates/reads its own throwaway file.</summary>
        private string CopyRom(string romPath, string tag)
        {
            string tempRom = Path.Combine(Path.GetTempPath(), $"FEBuilder_cexport_{tag}_{Guid.NewGuid():N}.gba");
            File.Copy(romPath, tempRom);
            _tempFiles.Add(tempRom);
            return tempRom;
        }

        // Pure-BCL little-endian u32 read/write — deliberately NOT a Core/ROM reference
        // (this project is a black-box CLI-subprocess harness). Mirrors exactly what
        // FEBuilderGBA.Core's U.u32/rom.write_u32 do for a little-endian GBA ROM (each
        // byte is cast to uint BEFORE shifting, so there is no signed-int shift/sign-
        // extension ambiguity for the top byte).
        private static uint ReadU32LE(byte[] data, long offset)
        {
            return (uint)data[offset]
                + ((uint)data[offset + 1] << 8)
                + ((uint)data[offset + 2] << 16)
                + ((uint)data[offset + 3] << 24);
        }

        private static void WriteU32LE(byte[] data, long offset, uint value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        // ------------------------------------------------------------------ FE8U items: shape + no BOM

        [SkippableFact]
        public void ExportData_C_FE8UItems_ProducesExpectedSymbolsShapeAndNoBom()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyRom(RomLocator.FE8U!, "fe8u_items");
            string outC = TempFile(".c");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=c --out=\"{outC}\"", timeoutMs: 60_000);

            Assert.True(code == 0, $"--export-data --format=c exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(outC));

            byte[] bytes = File.ReadAllBytes(outC);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "C export must be UTF-8 WITHOUT a BOM");

            string content = File.ReadAllText(outC);
            Assert.Contains("#include <stdint.h>", content);
            Assert.Contains("struct __attribute__((packed)) FEBuilder_", content);
            Assert.Contains("__attribute__((aligned(4)))", content);
            Assert.Matches(new Regex(@"_Static_assert\(sizeof\(struct FEBuilder_\w+\) == 0x[0-9A-F]+"), content);
            Assert.Contains("gFEBuilder_items[", content);
            Assert.Matches(new Regex(@"const uint32_t gFEBuilder_itemsCount = \d+;"), content);
            Assert.Contains("[0x000]", content);
        }

        // ------------------------------------------------------------------ FE6 map_settings: real overlap shape

        [SkippableFact]
        public void ExportData_C_FE6MapSettings_GroupsKnownOverlapIntoUnion()
        {
            Skip.If(RomLocator.FE6 == null, "FE6 ROM not available");
            string rom = CopyRom(RomLocator.FE6!, "fe6_mapsettings");
            string outC = TempFile(".c");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=map_settings --format=c --out=\"{outC}\"", timeoutMs: 60_000);

            Assert.True(code == 0, $"--export-data --format=c exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(outC));

            string content = File.ReadAllText(outC);
            // FE6 map_settings has real, already-shipped byte-level overlaps (BGM1 word
            // aliasing the Field15 byte, etc.) — the C export must group these into an
            // anonymous packed union with one raw arm, never silently drop/misalign the
            // aliased byte.
            Assert.Contains("union {", content);
            Assert.Contains("as_BGM1;", content);
            Assert.Contains("_raw[", content);
            Assert.Contains("_Static_assert(sizeof(struct FEBuilder_MapSetting_FE6)", content);
        }

        // ------------------------------------------------------------------ a real >255-row table, deterministically
        // extended: no stock registered table across the 5 local ROMs actually has
        // >256 rows (measured maxima: FE6 units 247; FE7 event_force_sortie 256; FE8
        // units 255; FE8U portraits 173) — so an "opportunistic, skip if not enough
        // rows" test never proves the [0x100] full-width designator. This test instead
        // builds a CONTROLLED FIXTURE: it patches a per-test COPY of the real,
        // registered "portraits" table (never the shared source ROM) so the table's
        // own scan-visible row count is deterministically 257, then asserts against
        // the ACTUAL CLI-generated C output — same command, same table, same
        // production code path as every other test in this file.

        [SkippableFact]
        public void ExportData_C_PortraitsTable_ControlledFixtureExtendedTo257Rows_DesignatorsDoNotAliasAboveByteRange()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyRom(RomLocator.FE8U!, "fe8u_portraits_fixture");

            // Discover the STOCK row count from the CLI's own export of the untouched
            // copy first — never hardcoded — using the exact same --format=c command
            // exercised by the rest of this file, then clean it up like every other
            // temp file here.
            string baselineC = TempFile(".c");
            var (baseCode, baseOut, baseErr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=portraits --format=c --out=\"{baselineC}\"", timeoutMs: 60_000);
            Assert.True(baseCode == 0, $"baseline --export-data --format=c exited {baseCode}\nStdout: {baseOut}\nStderr: {baseErr}");
            string baselineContent = File.ReadAllText(baselineC);
            var baselineMatch = Regex.Match(baselineContent, @"const uint32_t gFEBuilder_portraitsCount = (\d+);");
            Assert.True(baselineMatch.Success, "expected a gFEBuilder_portraitsCount symbol in the baseline export");
            int stockCount = int.Parse(baselineMatch.Groups[1].Value);

            const long PortraitPointerSlotOffset = 0x5524; // FE8U: file offset holding the stored pointer-to-pointer
            const int PortraitStride = 28;                  // canonical FE8U portrait entry size
            const int TargetRowCount = 257;                 // one past the byte-truncation boundary (0xFF)

            Assert.True(stockCount >= 1 && stockCount <= TargetRowCount,
                $"stock portrait count {stockCount} is outside the range this controlled fixture can extend from " +
                $"(expected 1..{TargetRowCount}); refusing to build a fixture that would truncate real data.");

            byte[] data = File.ReadAllBytes(rom);
            Assert.True(data.Length >= PortraitPointerSlotOffset + 4,
                $"ROM too small to contain the portrait pointer slot at 0x{PortraitPointerSlotOffset:X}");

            // The stored value is itself a GBA pointer (0x08000000+) to the portrait
            // table — not the table's address directly (mirrors
            // StructExportCore.ResolvePointer/U.toOffset, reimplemented here with pure
            // BCL reads since this project intentionally has no Core reference).
            uint pointerValue = ReadU32LE(data, PortraitPointerSlotOffset);
            Assert.True(pointerValue >= 0x08000000 && pointerValue < 0x0A000000,
                $"portrait table pointer 0x{pointerValue:X8} at slot 0x{PortraitPointerSlotOffset:X} is not in the 0x08000000 ROM range");

            long tableOffset = pointerValue - 0x08000000u;
            Assert.True(tableOffset >= 0 && tableOffset < data.Length,
                $"portrait table offset 0x{tableOffset:X} resolved from pointer 0x{pointerValue:X8} is out of the ROM file bounds");

            // Full bounds check BEFORE any mutation: every row through the forced
            // terminator (TargetRowCount, i.e. row 257) must fit inside the file.
            long terminatorRowOffset = tableOffset + (long)TargetRowCount * PortraitStride;
            Assert.True(terminatorRowOffset + 4 <= data.Length,
                $"patched portrait table would need to write through file offset 0x{terminatorRowOffset + 4:X}, " +
                $"past the ROM file length 0x{data.Length:X}");

            // Extend the scan-visible fixture to exactly 257 rows: zero the 3 pointer
            // fields (+0/+4/+8, per StructExportCore's portrait entry-count scan) of
            // every row from the stock count through row index 256 inclusive, so each
            // becomes a valid NULL entry the scan keeps counting.
            for (int i = stockCount; i < TargetRowCount; i++)
            {
                long rowOffset = tableOffset + (long)i * PortraitStride;
                WriteU32LE(data, rowOffset + 0, 0);
                WriteU32LE(data, rowOffset + 4, 0);
                WriteU32LE(data, rowOffset + 8, 0);
            }

            // Force row 257 to be an invalid terminator (neither NULL nor a
            // 0x08000000+ pointer) so the scan stops at EXACTLY 257 rows.
            WriteU32LE(data, terminatorRowOffset, 1);

            // Only the per-test COPY is mutated — RomLocator.FE8U itself is untouched.
            File.WriteAllBytes(rom, data);

            string outC = TempFile(".c");
            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=portraits --format=c --out=\"{outC}\"", timeoutMs: 60_000);
            Assert.True(code == 0, $"--export-data --format=c exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(outC));

            string content = File.ReadAllText(outC);
            Assert.Contains($"const uint32_t gFEBuilder_portraitsCount = {TargetRowCount};", content);

            // Row 256 (0-based) must render as the full-width [0x100], never a
            // byte-truncated alias back onto [0x00].
            Assert.Contains("[0x100]", content);

            var ordinals = new HashSet<string>();
            foreach (Match m in Regex.Matches(content, @"\[0x[0-9A-F]{3,}\]"))
                ordinals.Add(m.Value);
            Assert.Equal(TargetRowCount, ordinals.Count);
        }

        // ------------------------------------------------------------------ --table=all --format=c across all 5 ROMs

        [SkippableFact]
        public void ExportData_C_TableAll_AcrossAllRoms_EveryRegisteredTableExported_AndAtLeastOneIsZeroRow()
        {
            bool anyRomAvailable = false;
            bool allRomsAvailable = true;
            bool foundZeroRow = false;

            foreach (object?[] entry in RomLocator.AllRoms)
            {
                string romName = (string)entry[0]!;
                string? romPath = (string?)entry[1];
                if (romPath == null) { allRomsAvailable = false; continue; }
                anyRomAvailable = true;

                string rom = CopyRom(romPath, romName);
                string dir = TempDir();

                // Discover the ground-truth per-ROM registered-table list from the CLI's
                // own already-working --table=all --format=tsv output, instead of a
                // hardcoded list that could silently drift out of sync with
                // StructExportCore's table registrations.
                string tsvBase = Path.Combine(dir, "all_tsv");
                var (tsvCode, tsvOut, tsvErr) = AppRunner.Run(CliExe,
                    $"--export-data --rom=\"{rom}\" --table=all --format=tsv --out=\"{tsvBase}\"", timeoutMs: 120_000);
                Assert.True(tsvCode == 0, $"{romName}: --table=all --format=tsv exited {tsvCode}\nStdout: {tsvOut}\nStderr: {tsvErr}");

                string tsvPrefix = Path.GetFileName(tsvBase) + ".";
                var expectedTables = new List<string>();
                foreach (string f in Directory.GetFiles(dir, "all_tsv.*.tsv"))
                {
                    string name = Path.GetFileName(f);
                    string table = name.Substring(tsvPrefix.Length, name.Length - tsvPrefix.Length - ".tsv".Length);
                    expectedTables.Add(table);
                }
                Assert.True(expectedTables.Count > 0, $"{romName}: expected at least one --table=all --format=tsv output file");

                string cBase = Path.Combine(dir, "all_c");
                var (cCode, cOut, cErr) = AppRunner.Run(CliExe,
                    $"--export-data --rom=\"{rom}\" --table=all --format=c --out=\"{cBase}\"", timeoutMs: 180_000);
                Assert.True(cCode == 0, $"{romName}: --table=all --format=c exited {cCode}\nStdout: {cOut}\nStderr: {cErr}");

                foreach (string table in expectedTables)
                {
                    string expectedFile = cBase + "." + table + ".c";
                    Assert.True(File.Exists(expectedFile), $"{romName}: expected C export for table '{table}' at {expectedFile}");

                    if (!foundZeroRow)
                    {
                        string content = File.ReadAllText(expectedFile);
                        if (Regex.IsMatch(content, @"\[0\]\s*__attribute__\(\(aligned\(4\)\)\);")
                            && Regex.IsMatch(content, @"Count = 0;"))
                        {
                            foundZeroRow = true;
                        }
                    }
                }
            }

            Skip.If(!anyRomAvailable, "No ROMs available");

            if (!allRomsAvailable)
            {
                // Partial ROM availability: the zero-row contract typically needs an
                // FE6/FE7 ROM (missing FE8-only tables like summon_units) alongside an
                // FE8 ROM to observe in the SAME run. Don't fail the whole test on
                // ROM-availability alone — the per-table existence assertions above
                // already ran for whichever ROMs WERE available.
                Skip.If(!foundZeroRow,
                    "Not all 5 ROMs were available locally, and none of the available ones exposed a zero-row/version-absent table; skipping the zero-row assertion.");
            }
            else
            {
                Assert.True(foundZeroRow,
                    "expected at least one zero-row/version-absent table across all 5 ROMs' --table=all --format=c output");
            }
        }

        // ------------------------------------------------------------------ --c-symbol: valid override

        [SkippableFact]
        public void ExportData_C_ExplicitCSymbol_OverridesDataAndCountSymbols()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyRom(RomLocator.FE8U!, "fe8u_symbol");
            string outC = TempFile(".c");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=c --c-symbol=gMyCustomItems --out=\"{outC}\"", timeoutMs: 60_000);

            Assert.True(code == 0, $"Stdout: {stdout}\nStderr: {stderr}");
            string content = File.ReadAllText(outC);
            Assert.Contains("gMyCustomItems[", content);
            Assert.Matches(new Regex(@"const uint32_t gMyCustomItemsCount = \d+;"), content);
            Assert.DoesNotContain("gFEBuilder_items", content);
        }

        // ------------------------------------------------------------------ --c-symbol / --format validation
        // (no ROM required — validated before RomLoader ever touches --rom; proven with a
        // deliberately nonexistent --rom path and a reserved-but-never-created --out path)

        [Fact]
        public void ExportData_C_InvalidCSymbolIdentifier_RejectsBeforeRomLoadAndOutput()
        {
            string outC = TempFile(".c");
            string nonexistentRom = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.gba");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{nonexistentRom}\" --table=items --format=c --c-symbol=0BadSymbol --out=\"{outC}\"", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--c-symbol", stdout + stderr);
            Assert.Contains("0BadSymbol", stdout + stderr);
            Assert.False(File.Exists(outC), "no output file should be created when --c-symbol validation fails before ROM load");
        }

        [Fact]
        public void ExportData_C_KeywordCSymbol_RejectsBeforeRomLoadAndOutput()
        {
            string outC = TempFile(".c");
            string nonexistentRom = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.gba");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{nonexistentRom}\" --table=items --format=c --c-symbol=struct --out=\"{outC}\"", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--c-symbol", stdout + stderr);
            Assert.False(File.Exists(outC));
        }

        [Fact]
        public void ExportData_CSymbolWithNonCFormat_RejectsExplicitlyBeforeRomLoadAndOutput()
        {
            string outPath = TempFile(".tsv");
            string nonexistentRom = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.gba");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{nonexistentRom}\" --table=items --format=tsv --c-symbol=gFoo --out=\"{outPath}\"", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--c-symbol requires --format=c", stdout + stderr);
            Assert.False(File.Exists(outPath));
        }

        [Fact]
        public void ExportData_CSymbolWithTableAll_RejectsBeforeRomLoadAndOutput()
        {
            string outBase = TempFile("");
            string nonexistentRom = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.gba");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{nonexistentRom}\" --table=all --format=c --c-symbol=gFoo --out=\"{outBase}\"", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--table=all", stdout + stderr);
            Assert.False(File.Exists(outBase + ".items.c"));
        }

        [Fact]
        public void ExportData_UnsupportedFormat_StillRejectsBeforeRomLoad_MentionsC()
        {
            // Regression guard for the --format allowlist update: "c" must now be listed
            // alongside the pre-existing tsv/csv/ea/json, and an actually-unsupported value
            // must still fail before any ROM/output work.
            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                "--export-data --rom=does-not-exist.gba --table=units --format=xml", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--format must be one of tsv, csv, ea, json, c", stdout + stderr);
        }
    }
}
