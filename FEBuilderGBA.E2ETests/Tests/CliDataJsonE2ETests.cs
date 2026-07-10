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
    /// ROM-gated (uses a temporary copy of <c>roms/FE8U.gba</c> via <see cref="RomLocator"/>)
    /// and skips via <see cref="SkippableFact"/> when the ROM is not available locally — the
    /// same pattern as <c>RomCliTests</c>/<c>AvaloniaDataVerifyTests</c>. No ROM is bundled or
    /// downloaded by this test file itself.
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
    }
}
