using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// E2E coverage for #1937 — <c>--export-data --format=json</c> and the matching
    /// <c>--import-data</c> JSON input path on <c>FEBuilderGBA.CLI</c>.
    ///
    /// Most scenarios are ROM-gated (uses a temporary copy of <c>roms/FE8U.gba</c> via
    /// <see cref="RomLocator"/>) and skip via <see cref="SkippableFact"/> when the ROM is
    /// not available locally — the same pattern as <c>RomCliTests</c>/
    /// <c>AvaloniaDataVerifyTests</c>. No ROM is bundled or downloaded by this test file
    /// itself. The <c>--format</c> validation and strict JSON <c>Index</c>-rejection tests
    /// run before any ROM load, so a few are plain <see cref="FactAttribute"/>s that need
    /// no ROM at all.
    /// </summary>
    public class CliDataJsonE2ETests : IDisposable
    {
        private static readonly string CliExe = AppRunner.FindCliExePath();
        private readonly List<string> _tempFiles = new();

        public void Dispose()
        {
            foreach (var f in _tempFiles)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
        }

        private string TempFile(string ext)
        {
            var path = Path.Combine(Path.GetTempPath(), $"febuilder_datajson_{Guid.NewGuid():N}{ext}");
            _tempFiles.Add(path);
            return path;
        }

        /// <summary>Copy the shared FE8U ROM so every test mutates its own throwaway file.</summary>
        private string CopyFE8U()
        {
            string tempRom = Path.Combine(Path.GetTempPath(), $"FEBuilder_datajson_{Guid.NewGuid():N}.gba");
            File.Copy(RomLocator.FE8U!, tempRom);
            _tempFiles.Add(tempRom);
            return tempRom;
        }

        // ------------------------------------------------------------------ export --format=json shape

        [SkippableFact]
        public void ExportData_Json_ExposesPublicIndex_NotInternalUnderscoreIndex()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            string outJson = TempFile(".json");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=json --out=\"{outJson}\"", timeoutMs: 60_000);

            Assert.True(code == 0, $"--export-data --format=json exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(outJson));

            using var doc = JsonDocument.Parse(File.ReadAllText(outJson));
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.True(doc.RootElement.GetArrayLength() > 1, "expected the items table to have more than one row");

            var row0 = doc.RootElement[0];
            Assert.True(row0.TryGetProperty("Index", out _), "row is missing the public 'Index' key");
            Assert.False(row0.TryGetProperty("_Index", out _), "internal '_Index' key must never leak into public JSON");
        }

        [SkippableFact]
        public void ExportData_Json_AllValuesAreJsonStrings()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            string outJson = TempFile(".json");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=json --out=\"{outJson}\"", timeoutMs: 60_000);
            Assert.True(code == 0, $"exited {code}\nStdout: {stdout}\nStderr: {stderr}");

            using var doc = JsonDocument.Parse(File.ReadAllText(outJson));
            foreach (JsonElement row in doc.RootElement.EnumerateArray())
            {
                foreach (JsonProperty prop in row.EnumerateObject())
                {
                    Assert.Equal(JsonValueKind.String, prop.Value.ValueKind);
                }
            }
        }

        // ------------------------------------------------------------------ export -> edit -> import -> re-export

        [SkippableFact]
        public void ExportEditImportReExport_Json_PersistsEditedField()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            string exportJson = TempFile(".json");

            var (code1, stdout1, stderr1) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=json --out=\"{exportJson}\"", timeoutMs: 60_000);
            Assert.True(code1 == 0, $"export failed ({code1})\nStdout: {stdout1}\nStderr: {stderr1}");

            // Edit item index 1's "Weight" field (a plain byte stat, safe to flip — no
            // pointer/table-shifting side effects) to a value guaranteed different from
            // whatever the ROM currently has.
            JsonArray rows = JsonNode.Parse(File.ReadAllText(exportJson))!.AsArray();
            Assert.True(rows.Count > 1, "expected the items table to have more than one row");
            JsonObject row1 = rows[1]!.AsObject();
            string originalWeight = row1["Weight"]!.GetValue<string>();
            string newWeight = originalWeight == "0x05" ? "0x06" : "0x05";
            row1["Weight"] = newWeight;

            string editedJson = TempFile(".json");
            File.WriteAllText(editedJson, rows.ToJsonString());

            var (code2, stdout2, stderr2) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{editedJson}\"", timeoutMs: 60_000);
            Assert.True(code2 == 0, $"import failed ({code2})\nStdout: {stdout2}\nStderr: {stderr2}");
            Assert.Contains("entries from JSON", stdout2);

            string reExportJson = TempFile(".json");
            var (code3, stdout3, stderr3) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=json --out=\"{reExportJson}\"", timeoutMs: 60_000);
            Assert.True(code3 == 0, $"re-export failed ({code3})\nStdout: {stdout3}\nStderr: {stderr3}");

            using var doc = JsonDocument.Parse(File.ReadAllText(reExportJson));
            string persistedWeight = doc.RootElement[1].GetProperty("Weight").GetString()!;
            Assert.Equal(newWeight, persistedWeight);
        }

        // ------------------------------------------------------------------ malformed JSON must not mutate the ROM

        [SkippableFact]
        public void ImportData_Json_NumberValue_FailsBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{\"Index\":\"0x01 Iron Sword\",\"Weight\":5}]"); // number, not a JSON string

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("must be a JSON string", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_BooleanValue_FailsBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{\"Index\":\"0x01 Iron Sword\",\"Weight\":true}]");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("must be a JSON string", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_RootIsObjectNotArray_FailsBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "{\"Index\":\"0x01 Iron Sword\",\"Weight\":\"0x05\"}");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("root element must be an array", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_MalformedSyntax_FailsBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{ this is not valid json");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        // ------------------------------------------------------------------ --format=json explicit override

        [SkippableFact]
        public void ImportData_ExplicitFormatJson_OverridesNonJsonExtension()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            string exportJson = TempFile(".json");
            var (codeExport, _, stderrExport) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=json --out=\"{exportJson}\"", timeoutMs: 60_000);
            Assert.True(codeExport == 0, stderrExport);

            // Save the same JSON content under a non-.json extension; --format=json must
            // still route it through the JSON parser instead of TSV.
            string txtPath = TempFile(".txt");
            File.Copy(exportJson, txtPath, overwrite: true);

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{txtPath}\" --format=json", timeoutMs: 60_000);
            Assert.True(code == 0, $"Stdout: {stdout}\nStderr: {stderr}");
            Assert.Contains("entries from JSON", stdout);
        }

        // ------------------------------------------------------------------ existing TSV/CSV/EA/data-roundtrip compatibility

        [SkippableTheory]
        [InlineData("tsv", ".tsv")]
        [InlineData("csv", ".csv")]
        [InlineData("ea", ".ea")]
        public void ExportData_ExistingFormats_StillWorkUnaffectedByJsonAddition(string format, string ext)
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            string outPath = TempFile(ext);

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format={format} --out=\"{outPath}\"", timeoutMs: 60_000);

            Assert.True(code == 0, $"--format={format} exited {code}\nStdout: {stdout}\nStderr: {stderr}");
            Assert.True(File.Exists(outPath));
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(outPath)));
        }

        [SkippableFact]
        public void ImportData_Tsv_StillWorksUnaffectedByJsonAddition()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            string tsvPath = TempFile(".tsv");

            var (codeExport, _, stderrExport) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=tsv --out=\"{tsvPath}\"", timeoutMs: 60_000);
            Assert.True(codeExport == 0, stderrExport);

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{tsvPath}\"", timeoutMs: 60_000);
            Assert.True(code == 0, $"Stdout: {stdout}\nStderr: {stderr}");
            Assert.Contains("entries from TSV", stdout);
        }

        [SkippableFact]
        public void DataRoundTrip_StillLosslessForItemsTable()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--data-roundtrip --rom=\"{rom}\" --table=items", timeoutMs: 60_000);

            Assert.True(code == 0, $"--data-roundtrip exited {code}\nStdout: {stdout}\nStderr: {stderr}");
        }

        // ------------------------------------------------------------------ --format validation (no ROM required)
        //
        // Format is validated before RomLoader ever touches --rom (export) / before ROM
        // load (import, after the pre-existing --in file-exists check), so these run
        // as plain [Fact]s with a throwaway/non-existent --rom path — no real ROM needed.

        [Fact]
        public void ExportData_UnsupportedFormat_RejectsBeforeRomLoad()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                "--export-data --rom=does-not-exist.gba --table=units --format=xml", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--format must be one of tsv, csv, ea, json", stdout + stderr);
        }

        [Fact]
        public void ImportData_UnsupportedFormat_RejectsBeforeRomLoad()
        {
            string inPath = TempFile(".dat");
            File.WriteAllText(inPath, "irrelevant content");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=does-not-exist.gba --table=units --in=\"{inPath}\" --format=xml", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            Assert.Contains("--format must be tsv or json", stdout + stderr);
        }

        [Fact]
        public void ImportData_MissingIn_ErrorMentionsTsvAndJson()
        {
            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                "--import-data --rom=does-not-exist.gba --table=units", timeoutMs: 15_000);

            Assert.NotEqual(0, code);
            string combined = stdout + stderr;
            Assert.Contains("--in=", combined);
            Assert.Contains("TSV", combined);
            Assert.Contains("JSON", combined);
        }

        // ------------------------------------------------------------------ strict JSON Index parsing (ROM-gated mutation check)

        [SkippableFact]
        public void ImportData_Json_GarbageIndex_RejectedInsteadOfAliasingToRowZero()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            // "banana" is not 0x/$/decimal — U.atoi0x's permissive TSV-style parse would
            // silently alias this to index 0; strict JSON parsing must reject it instead.
            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{\"Index\":\"banana\",\"Weight\":\"0x05\"}]");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("unparsable 'Index'", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_DuplicateIndexProperty_RejectedInsteadOfLastWins()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            // Duplicate "Index" keys in the same row: JsonDocument tolerates this and would
            // otherwise silently keep only the last value on enumeration.
            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{\"Index\":\"0x00 A\",\"Index\":\"0x01 B\",\"Weight\":\"0x05\"}]");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("duplicate property", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        // ------------------------------------------------------------------ #1937 follow-up:
        // struct/count-aware semantic preflight (ValidateJSONEntries). ParseJSON above only
        // validates JSON *kinds*; these prove the additional field-name/value/index checks
        // also reject before any ROM write, and that a valid non-hex (decimal) value still
        // persists correctly.

        [SkippableFact]
        public void ImportData_Json_UnknownFieldName_RejectedBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            // "Wieght" is a typo of the real "Weight" field — must be rejected instead of
            // silently ignored (the old WriteTable behavior for an unknown field name).
            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{\"Index\":\"0x01 Iron Sword\",\"Wieght\":\"0x05\"}]");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("unknown property", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_GarbageFieldValue_RejectedBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            // "banana" is not 0x/$/decimal for the "Weight" field — U.atoi0x's permissive
            // TSV-style parse would silently coerce this to 0 and mutate the ROM.
            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{\"Index\":\"0x01 Iron Sword\",\"Weight\":\"banana\"}]");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("invalid value", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_DuplicateRowIndex_RejectedBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            // Two distinct row objects both targeting Index 1 — distinct from the
            // duplicate-*property*-within-one-row case above.
            string badJson = TempFile(".json");
            File.WriteAllText(badJson,
                "[{\"Index\":\"0x01 A\",\"Weight\":\"0x05\"},{\"Index\":\"0x01 B\",\"Weight\":\"0x06\"}]");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("duplicate Index", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_IndexOutsideResolvedEntryCount_RejectedBeforeAnyRomWrite()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            byte[] romBefore = File.ReadAllBytes(rom);

            // The items table never has anywhere close to 0xFFFFFF entries — WriteTable's
            // silent per-row skip must not be relied on; this has to fail before ROM.Save.
            string badJson = TempFile(".json");
            File.WriteAllText(badJson, "[{\"Index\":\"0xFFFFFF\",\"Weight\":\"0x05\"}]");

            var (code, stdout, stderr) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{badJson}\"", timeoutMs: 60_000);

            Assert.NotEqual(0, code);
            Assert.Contains("outside the valid range", stdout + stderr);
            Assert.Equal(romBefore, File.ReadAllBytes(rom));
        }

        [SkippableFact]
        public void ImportData_Json_ValidPlainDecimalFieldValue_NormalizesAndPersists()
        {
            Skip.If(RomLocator.FE8U == null, "FE8U ROM not available");
            string rom = CopyFE8U();
            string exportJson = TempFile(".json");

            var (code1, stdout1, stderr1) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=json --out=\"{exportJson}\"", timeoutMs: 60_000);
            Assert.True(code1 == 0, $"export failed ({code1})\nStdout: {stdout1}\nStderr: {stderr1}");

            JsonArray rows = JsonNode.Parse(File.ReadAllText(exportJson))!.AsArray();
            Assert.True(rows.Count > 1, "expected the items table to have more than one row");
            JsonObject row1 = rows[1]!.AsObject();
            string originalWeight = row1["Weight"]!.GetValue<string>();
            // Non-hex plain-decimal input — proves ValidateJSONEntries's normalization
            // (not just hex forms) round-trips through U.atoi0x safely.
            string decimalWeight = originalWeight == "0x07" ? "8" : "7";
            row1["Weight"] = decimalWeight;

            string editedJson = TempFile(".json");
            File.WriteAllText(editedJson, rows.ToJsonString());

            var (code2, stdout2, stderr2) = AppRunner.Run(CliExe,
                $"--import-data --rom=\"{rom}\" --table=items --in=\"{editedJson}\"", timeoutMs: 60_000);
            Assert.True(code2 == 0, $"import failed ({code2})\nStdout: {stdout2}\nStderr: {stderr2}");

            string reExportJson = TempFile(".json");
            var (code3, stdout3, stderr3) = AppRunner.Run(CliExe,
                $"--export-data --rom=\"{rom}\" --table=items --format=json --out=\"{reExportJson}\"", timeoutMs: 60_000);
            Assert.True(code3 == 0, $"re-export failed ({code3})\nStdout: {stdout3}\nStderr: {stderr3}");

            using var doc = JsonDocument.Parse(File.ReadAllText(reExportJson));
            string persistedWeight = doc.RootElement[1].GetProperty("Weight").GetString()!;
            string expectedHex = "0x" + int.Parse(decimalWeight).ToString("X02");
            Assert.Equal(expectedHex, persistedWeight);
        }
    }
}
