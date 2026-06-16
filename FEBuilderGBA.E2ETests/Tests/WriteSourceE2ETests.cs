using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box E2E tests for the <c>--write-source</c> CLI command (#1132).
    ///
    /// Tests cover:
    /// - usage faults (missing required args) → non-zero exit
    /// - a not-owned table → exit 2 with a ROM-only/manual/not-owned message
    /// - full success path: a synthetic decomp project (real ROM as the build
    ///   preview + a tiny C source declaring the items table owner) is rewritten,
    ///   exit 0, NeedsRebuild printed, the source file's integer token changed.
    ///
    /// ROM-dependent tests are skipped when no ROM is available. The success path
    /// uses FE8U specifically (the project manifest forces FE8U so LoadProject is
    /// deterministic).
    /// </summary>
    public class WriteSourceE2ETests
    {
        static readonly string CliExe = AppRunner.FindCliExePath();

        static string? FirstRom =>
            RomLocator.FE6 ?? RomLocator.FE7J ?? RomLocator.FE7U ?? RomLocator.FE8J ?? RomLocator.FE8U;

        static (int ExitCode, string Stdout, string Stderr) RunWithRetry(
            string args, int timeoutMs = 60_000, int maxAttempts = 2)
        {
            (int ExitCode, string Stdout, string Stderr) result = default;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result = AppRunner.Run(CliExe, args, timeoutMs);
                if (result.ExitCode >= 0)
                    return result;
            }
            return result;
        }

        static string NewTempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"write_source_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---- usage faults ----

        [Fact]
        public void WriteSource_MissingProject_ExitsNonZero()
        {
            var (code, _, _) = RunWithRetry("--write-source --table=items --id=1 --field=might --value=5");
            Assert.NotEqual(0, code);
        }

        [Fact]
        public void WriteSource_MissingField_ExitsNonZero()
        {
            var (code, _, _) = RunWithRetry("--write-source --project=fake_dir --table=items --id=1 --value=5");
            Assert.NotEqual(0, code);
        }

        // ---- not-owned table → exit 2 ----

        [SkippableFact]
        public void WriteSource_NotOwnedTable_ExitsTwo_WithRomOnlyMessage()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source not-owned test");

            string projectDir = NewTempDir("notowned");
            try
            {
                // Manifest declares NO table owners → every table is ROM-only. The
                // built ROM is whatever variant is available (auto-detect version).
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=items --id=1 --field=might --value=5";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.Contains("ROM-only", stdout + stderr, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- full success path ----

        [SkippableFact]
        public void WriteSource_Success_RewritesSourceFile_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source success test");

            string projectDir = NewTempDir("success");
            try
            {
                // C source declaring the items array the manifest owner points at.
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "item.c");
                string content =
                    "const struct Item gItemData[] = {\n" +
                    "    [0] = { .nameId = 1, .might = 5, .hitRate = 90 },\n" +
                    "    [1] = { .nameId = 2, .might = 8, .hitRate = 75 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                // Manifest: built ROM (whatever variant is available) + items owner.
                string manifest =
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    "  \"builtRom\": \"synth.gba\",\n" +
                    "  \"tables\": [\n" +
                    "    { \"table\": \"items\", \"format\": \"cstruct\", \"writePolicy\": \"source\",\n" +
                    "      \"arrayName\": \"gItemData\", \"sourceFile\": \"src/item.c\",\n" +
                    "      \"fields\": [ { \"name\": \"nameId\" }, { \"name\": \"might\" }, { \"name\": \"hitRate\" } ] }\n" +
                    "  ]\n" +
                    "}\n";
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"), manifest);
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=items --id=1 --field=might --value=0x0A";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--write-source success exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);
                Assert.Contains("Source file:", stdout);

                // The source file's entry-1 might token must have changed to the new
                // value (10). The existing token was decimal, so the writer preserves
                // the decimal radix even though --value was given as 0x0A.
                string after = File.ReadAllText(srcAbs);
                Assert.Contains(".might = 10", after);
                Assert.Contains("[0] = { .nameId = 1, .might = 5, .hitRate = 90 }", after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- no-op (value already matches) → no write, NeedsRebuild=false ----

        [SkippableFact]
        public void WriteSource_NoOp_ValueAlreadyMatches_NoWrite_NeedsRebuildFalse()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source no-op test");

            string projectDir = NewTempDir("noop");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "item.c");
                string content =
                    "const struct Item gItemData[] = {\n" +
                    "    [0] = { .might = 5 },\n" +
                    "    [1] = { .might = 8 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);
                var mtimeBefore = File.GetLastWriteTimeUtc(srcAbs);

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"items\", \"format\": \"cstruct\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"gItemData\", \"sourceFile\": \"src/item.c\"," +
                    "    \"fields\": [ { \"name\": \"might\" } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // Entry 1 .might is already 8 → requesting 8 is a no-op.
                string args = $"--write-source --project=\"{projectDir}\" --table=items --id=1 --field=might --value=8";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=false", stdout);
                // The source file must be byte-identical (no churn) and untouched.
                Assert.Equal(content, File.ReadAllText(srcAbs));
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- --out-diff artifact ----

        [SkippableFact]
        public void WriteSource_OutDiff_WritesDiffArtifact()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source out-diff test");

            string projectDir = NewTempDir("diff");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "item.c");
                File.WriteAllText(srcAbs,
                    "const struct Item gItemData[] = {\n" +
                    "    [0] = { .might = 5 },\n" +
                    "    [1] = { .might = 8 },\n" +
                    "};\n");

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"items\", \"format\": \"cstruct\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"gItemData\", \"sourceFile\": \"src/item.c\"," +
                    "    \"fields\": [ { \"name\": \"might\" } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string diffPath = Path.Combine(projectDir, "change.diff");
                string args = $"--write-source --project=\"{projectDir}\" --table=items --id=1 --field=might --value=10 --out-diff=\"{diffPath}\"";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.True(File.Exists(diffPath), $"Expected diff at {diffPath}");
                string diff = File.ReadAllText(diffPath);
                Assert.Contains("-", diff);
                Assert.Contains("+", diff);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1141: JSON-backed table ----

        [SkippableFact]
        public void WriteSource_JsonTable_RewritesNumber_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source json test");

            string projectDir = NewTempDir("json");
            try
            {
                string dataDir = Path.Combine(projectDir, "data");
                Directory.CreateDirectory(dataDir);
                string srcAbs = Path.Combine(dataDir, "items.json");
                string content =
                    "[\n" +
                    "  { \"nameId\": 1, \"might\": 5, \"hitRate\": 90 },\n" +
                    "  { \"nameId\": 2, \"might\": 8, \"hitRate\": 75 }\n" +
                    "]\n";
                File.WriteAllText(srcAbs, content);

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"items\", \"format\": \"json\", \"writePolicy\": \"source\"," +
                    "    \"sourceFile\": \"data/items.json\"," +
                    "    \"fields\": [ { \"name\": \"might\" }, { \"name\": \"hitRate\" } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=items --id=1 --field=might --value=10";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);
                string after = File.ReadAllText(srcAbs);
                Assert.Equal(content.Replace("\"might\": 8", "\"might\": 10"), after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1141: units (characters) C-array ----

        [SkippableFact]
        public void WriteSource_UnitsCArray_RewritesField_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source units test");

            string projectDir = NewTempDir("units");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "chardata.c");
                string content =
                    "struct CharacterData gCharacterData[] = {\n" +
                    "    [0] = { .hp = 16, .pow = 5 },\n" +
                    "    [1] = { .hp = 18, .pow = 7 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                // Declare the owner under "characters" — the --table=units alias resolves it.
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"characters\", \"format\": \"cstruct\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"gCharacterData\", \"sourceFile\": \"src/chardata.c\"," +
                    "    \"fields\": [ { \"name\": \"hp\", \"signed\": true, \"width\": 1 }," +
                    "                  { \"name\": \"pow\", \"signed\": true, \"width\": 1 } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=units --id=1 --field=pow --value=9";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);
                string after = File.ReadAllText(srcAbs);
                Assert.Contains(".pow = 9", after);
                Assert.Contains("[0] = { .hp = 16, .pow = 5 }", after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1141: multi-field (two field/value pairs) ----

        [SkippableFact]
        public void WriteSource_MultiField_BothApplied_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source multi-field test");

            string projectDir = NewTempDir("multi");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "chardata.c");
                string content =
                    "struct CharacterData gCharacterData[] = {\n" +
                    "    [0] = { .hp = 16, .pow = 5 },\n" +
                    "    [1] = { .hp = 18, .pow = 7 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"characters\", \"format\": \"cstruct\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"gCharacterData\", \"sourceFile\": \"src/chardata.c\"," +
                    "    \"fields\": [ { \"name\": \"hp\", \"signed\": true, \"width\": 1 }," +
                    "                  { \"name\": \"pow\", \"signed\": true, \"width\": 1 } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=units --id=1 " +
                              "--field=hp --value=20 --field=pow --value=9";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);
                string after = File.ReadAllText(srcAbs);
                Assert.Contains("[1] = { .hp = 20, .pow = 9 }", after);
                Assert.Contains("[0] = { .hp = 16, .pow = 5 }", after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1141: manual/romOnly table → advisory exit 2 ----

        [SkippableFact]
        public void WriteSource_RomOnlyTable_ExitsTwo()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source romOnly test");

            string projectDir = NewTempDir("romonly");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                File.WriteAllText(Path.Combine(srcDir, "item.c"),
                    "Item gItemData[] = { [0] = { .might = 5 } };\n");

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"items\", \"writePolicy\": \"romOnly\"," +
                    "    \"arrayName\": \"gItemData\", \"sourceFile\": \"src/item.c\"," +
                    "    \"fields\": [ { \"name\": \"might\" } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=items --id=0 --field=might --value=10";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.Contains("ROM-only", stdout + stderr, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1141: unpaired --field is a usage error ----

        [Fact]
        public void WriteSource_UnpairedField_ExitsNonZero()
        {
            // --field with no following --value → usage error (exit 1), no project load.
            var (code, _, _) = RunWithRetry("--write-source --project=fake_dir --table=items --id=1 --field=might");
            Assert.NotEqual(0, code);
        }

        // ====================================================================
        //  #1148: chapter settings (map_settings) source-backed write
        // ====================================================================

        [SkippableFact]
        public void WriteSource_MapSettingsCArray_RewritesChurnFree_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source map_settings test");

            string projectDir = NewTempDir("mapsettings");
            try
            {
                string srcDir = Path.Combine(projectDir, "src", "data");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "chapters.c");
                string content =
                    "struct ChapterData gMapChapterData[] = {\n" +
                    "    [0] = { .Weather = 0, .FogLevel = 0, .ChapterNumber = 0 },\n" +
                    "    [1] = { .Weather = 1, .FogLevel = 3, .ChapterNumber = 5 }, // ch1\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"map_settings\", \"format\": \"cstruct\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"gMapChapterData\", \"sourceFile\": \"src/data/chapters.c\", \"indexBase\": 0," +
                    "    \"fields\": [ { \"name\": \"Weather\" }, { \"name\": \"FogLevel\" }, { \"name\": \"ChapterNumber\" } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=map_settings --id=1 --field=Weather --value=4";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);

                // Churn-free: ONLY entry 1's Weather token changed; everything else byte-identical.
                string after = File.ReadAllText(srcAbs);
                Assert.Equal(content.Replace(".Weather = 1", ".Weather = 4"), after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        [SkippableFact]
        public void WriteSource_MapSettingsNoOp_ValueAlreadyMatches_NeedsRebuildFalse()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-source map_settings no-op test");

            string projectDir = NewTempDir("mapnoop");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "chapters.c");
                string content =
                    "struct ChapterData gMapChapterData[] = {\n" +
                    "    [0] = { .Weather = 2 },\n" +
                    "    [1] = { .Weather = 1 },\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"map_settings\", \"format\": \"cstruct\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"gMapChapterData\", \"sourceFile\": \"src/chapters.c\"," +
                    "    \"fields\": [ { \"name\": \"Weather\" } ] } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // Entry 1 .Weather is already 1 → requesting 1 is a no-op.
                string args = $"--write-source --project=\"{projectDir}\" --table=map_settings --id=1 --field=Weather --value=1";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=false", stdout);
                Assert.Equal(content, File.ReadAllText(srcAbs)); // byte-identical, untouched
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1149: support_units not-owned → exit 2 ----

        [SkippableFact]
        public void WriteSource_SupportUnits_NotOwned_ExitsTwo()
        {
            Skip.If(FirstRom == null, "No ROM available for support_units not-owned test");

            string projectDir = NewTempDir("su_notowned");
            try
            {
                // Manifest declares NO support_units owner → ROM-only.
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=support_units --id=0 --field=b0 --value=5";
                var (code, _, _) = RunWithRetry(args);

                Assert.Equal(2, code);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1149: support_units success path ----

        [SkippableFact]
        public void WriteSource_SupportUnits_Success_RewritesSourceFile()
        {
            Skip.If(FirstRom == null, "No ROM available for support_units success test");

            string projectDir = NewTempDir("su_success");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "support.c");
                // Minimal 24-field entry (all u8 fields for a support_units row)
                string content =
                    "const SupportData gSupportData[] = {\n" +
                    "    {0x06, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                string manifest =
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    "  \"builtRom\": \"synth.gba\",\n" +
                    "  \"tables\": [\n" +
                    "    { \"table\": \"support_units\", \"format\": \"cstruct\", \"writePolicy\": \"source\",\n" +
                    "      \"arrayName\": \"gSupportData\", \"sourceFile\": \"src/support.c\",\n" +
                    "      \"fields\": [ { \"name\": \"b0\" }, { \"name\": \"b1\" } ] }\n" +
                    "  ]\n" +
                    "}\n";
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"), manifest);
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-source --project=\"{projectDir}\" --table=support_units --id=0 --field=b0 --value=0x09";
                var (code, stdout, stderr) = RunWithRetry(args);

                // The stub source { {0x06, 0x07, ...} } is a valid positional cstruct that
                // the writer locates deterministically — this MUST be a strict success.
                Assert.True(code == 0,
                    $"--write-source support_units success exited {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);
                Assert.Contains("Source file:", stdout);

                // b0 was 0x06 (hex); the writer preserves the hex radix and emits the
                // shortest form (ToString("X")), so 0x06 -> 0x9 (NOT 0x09). Assert the
                // exact churn-free change.
                string after = File.ReadAllText(srcAbs);
                Assert.Contains("{0x9, 0x07, 0x00", after);   // b0 rewritten 0x06->0x9, b1 (0x07) untouched
                Assert.DoesNotContain("{0x06,", after);       // old token gone
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- #1159 (re-review): macro-skip exit codes ----

        // ALL requested fields map to a macro/expression → CLI exits 2 + stderr mentions
        // skipped + NeedsRebuild=false. Nothing is written to source.
        [SkippableFact]
        public void WriteSource_SupportUnits_AllSkipped_ExitsTwo()
        {
            Skip.If(FirstRom == null, "No ROM available for support_units all-skipped test");

            string projectDir = NewTempDir("su_allskip");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "support.c");
                // b0 AND b1 slots are MACRO tokens (unwritable) — a genuine BULK all-skipped scenario.
                string content =
                    "const SupportData gSupportData[] = {\n" +
                    "    {SOME_MACRO, OTHER_MACRO, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                string manifest =
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    "  \"builtRom\": \"synth.gba\",\n" +
                    "  \"tables\": [\n" +
                    "    { \"table\": \"support_units\", \"format\": \"cstruct\", \"writePolicy\": \"source\",\n" +
                    "      \"arrayName\": \"gSupportData\", \"sourceFile\": \"src/support.c\",\n" +
                    "      \"fields\": [ { \"name\": \"b0\" }, { \"name\": \"b1\" } ] }\n" +
                    "  ]\n" +
                    "}\n";
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"), manifest);
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // Edit BOTH macro fields → every requested field maps to a macro → all skipped (bulk).
                // (A single-field macro edit is a HARD UnsupportedField fail by #1159 design; only a
                //  bulk/multi-field edit routes macro fields to the soft "skipped" no-op path, which is
                //  what this test's stderr "skipped" + "NeedsRebuild=false" + byte-identical contract asserts.)
                string args = $"--write-source --project=\"{projectDir}\" --table=support_units --id=0 --field=b0 --value=0x09 --field=b1 --value=0x0A";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.Contains("skipped", stderr, System.StringComparison.OrdinalIgnoreCase);
                Assert.Contains("NeedsRebuild=false", stdout);

                // Source is byte-identical — nothing written.
                Assert.Equal(content, File.ReadAllText(srcAbs));
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // PARTIAL: one numeric + one macro field edited → CLI exits 2, stderr WARNING names
        // the skipped field, and the NUMERIC field WAS written to source.
        [SkippableFact]
        public void WriteSource_SupportUnits_Partial_ExitsTwo_WritesNumericField()
        {
            Skip.If(FirstRom == null, "No ROM available for support_units partial test");

            string projectDir = NewTempDir("su_partial");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "support.c");
                // b0 slot is a MACRO (unwritable); b1 is a plain literal (writable).
                string content =
                    "const SupportData gSupportData[] = {\n" +
                    "    {SOME_MACRO, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00},\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                string manifest =
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    "  \"builtRom\": \"synth.gba\",\n" +
                    "  \"tables\": [\n" +
                    "    { \"table\": \"support_units\", \"format\": \"cstruct\", \"writePolicy\": \"source\",\n" +
                    "      \"arrayName\": \"gSupportData\", \"sourceFile\": \"src/support.c\",\n" +
                    "      \"fields\": [ { \"name\": \"b0\" }, { \"name\": \"b1\" } ] }\n" +
                    "  ]\n" +
                    "}\n";
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"), manifest);
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // Edit BOTH b0 (macro — skipped) and b1 (numeric 0x07 -> 0x22 — written).
                string args = $"--write-source --project=\"{projectDir}\" --table=support_units --id=0 --field=b0 --value=0x09 --field=b1 --value=0x22";
                var (code, stdout, stderr) = RunWithRetry(args);

                // Partial = manual-required → exit 2, with NeedsRebuild=true (source WAS modified).
                Assert.Equal(2, code);
                Assert.Contains("NeedsRebuild=true", stdout);
                Assert.Contains("WARNING", stderr);
                Assert.Contains("b0", stderr);   // the skipped field is named

                // The numeric field WAS written (0x07 -> 0x22); the macro token is preserved.
                string after = File.ReadAllText(srcAbs);
                Assert.Contains("{SOME_MACRO, 0x22, 0x00", after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }
    }
}
