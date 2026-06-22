using System;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests
{
    /// <summary>
    /// Black-box E2E tests for the <c>--write-shop</c> CLI command (#1347): the in-place
    /// source-backed variable-length u16 ITEM_NONE-terminated shop-list writer.
    ///
    /// Tests cover:
    /// - usage faults (missing required args) → non-zero exit;
    /// - the full success path: a synthetic decomp project (real ROM as the build preview +
    ///   a tiny C source declaring a raw-hex shop list) is rewritten via <c>--symbol</c>,
    ///   exit 0, NeedsRebuild printed, the .c file's hex values + ITEM_NONE terminator updated;
    /// - a path-escaping sourceFile is rejected;
    /// - classic mode (no manifest owner) → not owned, exit 2 (the ROM is never written where
    ///   source-only).
    ///
    /// ROM-dependent tests are skipped when no ROM is available. This NEVER asserts a
    /// byte-identical ROM-to-source round-trip (that is DEFERRED, Slice 5).
    /// </summary>
    public class WriteShopE2ETests
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
            string dir = Path.Combine(Path.GetTempPath(), $"write_shop_{tag}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---- usage faults ----

        [Fact]
        public void WriteShop_MissingProject_ExitsNonZero()
        {
            var (code, _, _) = RunWithRetry("--write-shop --symbol=ItemList_Foo --items=0x01:5");
            Assert.NotEqual(0, code);
        }

        [Fact]
        public void WriteShop_MissingItems_ExitsNonZero()
        {
            var (code, _, _) = RunWithRetry("--write-shop --project=fake_dir --symbol=ItemList_Foo");
            Assert.NotEqual(0, code);
        }

        [Fact]
        public void WriteShop_NoOwnerSelector_ExitsNonZero()
        {
            // Neither --symbol nor --shop-addr.
            var (code, _, _) = RunWithRetry("--write-shop --project=fake_dir --items=0x01:5");
            Assert.NotEqual(0, code);
        }

        // ---- full success path (by --symbol) ----

        [SkippableFact]
        public void WriteShop_BySymbol_RewritesSourceFile_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-shop success test");

            string projectDir = NewTempDir("success");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "shop.c");
                // RAW-HEX list (the export format) the manifest list-owner points at.
                string content =
                    "const u16 ItemList_Foo[] = {\n" +
                    "    0x0501,\n" +
                    "    0x0000,\n" +
                    "};\n";
                File.WriteAllText(srcAbs, content);

                // Manifest: built ROM (whatever variant) + a u16-list owner for ItemList_Foo.
                string manifest =
                    "{\n" +
                    "  \"schemaVersion\": 1,\n" +
                    "  \"builtRom\": \"synth.gba\",\n" +
                    "  \"tables\": [\n" +
                    "    { \"table\": \"shop_foo\", \"format\": \"u16-list\", \"writePolicy\": \"source\",\n" +
                    "      \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" }\n" +
                    "  ]\n" +
                    "}\n";
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"), manifest);
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-shop --project=\"{projectDir}\" --symbol=ItemList_Foo --items=0x01:5,0x02:3";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0,
                    $"--write-shop success exited with {code}\nStdout: {stdout}\nStderr: {stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);
                Assert.Contains("Source file:", stdout);

                // The .c list now holds the packed values: id:qty → (qty<<8)|id, so
                // 0x01:5 -> 0x0501 and 0x02:3 -> 0x0302, then the ITEM_NONE terminator.
                string after = File.ReadAllText(srcAbs);
                Assert.Contains("0x0501,", after);
                Assert.Contains("0x0302,", after);
                Assert.Contains("0x0000,  // ITEM_NONE (terminator)", after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- symbolic ITEM_* source list (#1354) ----

        [SkippableFact]
        public void WriteShop_SymbolicList_PreservesMacroNames_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-shop symbolic test");

            string projectDir = NewTempDir("symbolic");
            try
            {
                // FE8U-style constants header at the conventional default path.
                string hdrDir = Path.Combine(projectDir, "include", "constants");
                Directory.CreateDirectory(hdrDir);
                File.WriteAllText(Path.Combine(hdrDir, "items.h"),
                    "enum {\n" +
                    "    ITEM_NONE = 0x00,\n" +
                    "    ITEM_SWORD_IRON = 0x01,\n" +
                    "    ITEM_LANCE_IRON = 0x14,\n" +
                    "    ITEM_AXE_IRON = 0x1F,\n" +
                    "};\n");

                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "shop.c");
                // Canonical FE8U item-id-only SYMBOLIC list.
                File.WriteAllText(srcAbs,
                    "CONST_DATA u16 ItemList_Foo[] = {\n" +
                    "    ITEM_SWORD_IRON,\n" +
                    "    ITEM_NONE,\n" +
                    "};\n");

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"shop_foo\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // item-id-only ⇒ quantity must be 0. Add LANCE_IRON + AXE_IRON.
                string args = $"--write-shop --project=\"{projectDir}\" --symbol=ItemList_Foo --items=0x01:0,0x14:0,0x1F:0";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);

                string after = File.ReadAllText(srcAbs);
                Assert.Contains("ITEM_SWORD_IRON,", after);
                Assert.Contains("ITEM_LANCE_IRON,", after);
                Assert.Contains("ITEM_AXE_IRON,", after);
                Assert.Contains("ITEM_NONE,", after);
                // No raw hex leaked into a symbolic list.
                Assert.DoesNotContain("0x00", after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        [SkippableFact]
        public void WriteShop_SymbolicNonzeroQuantity_Refused_ExitsTwo()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-shop symbolic refusal test");

            string projectDir = NewTempDir("symrefuse");
            try
            {
                string hdrDir = Path.Combine(projectDir, "include", "constants");
                Directory.CreateDirectory(hdrDir);
                File.WriteAllText(Path.Combine(hdrDir, "items.h"),
                    "enum { ITEM_NONE = 0x00, ITEM_SWORD_IRON = 0x01 };\n");

                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "shop.c");
                string original =
                    "CONST_DATA u16 ItemList_Foo[] = {\n    ITEM_SWORD_IRON,\n    ITEM_NONE,\n};\n";
                File.WriteAllText(srcAbs, original);

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"shop_foo\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // 0x01:5 carries a non-zero quantity ⇒ item-id-only symbolic can't encode it → exit 2.
                string args = $"--write-shop --project=\"{projectDir}\" --symbol=ItemList_Foo --items=0x01:5";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.Contains("item-id-only", (stdout + stderr).ToLowerInvariant());
                // Source untouched (no clobber).
                Assert.Equal(original, File.ReadAllText(srcAbs));
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- emptied shop (empty --items=) ----

        [SkippableFact]
        public void WriteShop_EmptyItems_EmptiesShop_ExitsZero()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-shop empty test");

            string projectDir = NewTempDir("empty");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                string srcAbs = Path.Combine(srcDir, "shop.c");
                File.WriteAllText(srcAbs,
                    "const u16 ItemList_Foo[] = {\n    0x0501,\n    0x0203,\n    0x0000,\n};\n");

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"s\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                // Empty --items= empties the shop (just the terminator remains).
                string args = $"--write-shop --project=\"{projectDir}\" --symbol=ItemList_Foo --items=";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                Assert.Contains("NeedsRebuild=true", stdout);
                string after = File.ReadAllText(srcAbs);
                Assert.DoesNotContain("0x0501", after);
                Assert.DoesNotContain("0x0203", after);
                Assert.Contains("0x0000,  // ITEM_NONE (terminator)", after);
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- path-escape sourceFile rejected ----

        [SkippableFact]
        public void WriteShop_PathEscapeSourceFile_Rejected_ExitsTwo()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-shop path-escape test");

            string projectDir = NewTempDir("escape");
            try
            {
                // sourceFile escapes the project root via "..".
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"s\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"../escape.c\" } ] }");
                File.Copy(FirstRom!, Path.Combine(projectDir, "synth.gba"), overwrite: true);

                string args = $"--write-shop --project=\"{projectDir}\" --symbol=ItemList_Foo --items=0x01:5";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.Contains("reject", (stdout + stderr).ToLowerInvariant());
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- classic / no-owner → not owned, exit 2 (ROM never written) ----

        [SkippableFact]
        public void WriteShop_NoManifestOwner_NotOwned_ExitsTwo()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-shop not-owned test");

            string projectDir = NewTempDir("notowned");
            try
            {
                // Manifest declares NO list owners → not owned for any symbol.
                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\" }");
                string romPath = Path.Combine(projectDir, "synth.gba");
                File.Copy(FirstRom!, romPath, overwrite: true);
                byte[] romBefore = File.ReadAllBytes(romPath);

                string args = $"--write-shop --project=\"{projectDir}\" --symbol=ItemList_Foo --items=0x01:5";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.Equal(2, code);
                Assert.Contains("not owned", (stdout + stderr).ToLowerInvariant());

                // The ROM must be byte-identical (this is a source-only writer; it never
                // mutates the preview ROM).
                Assert.Equal(romBefore, File.ReadAllBytes(romPath));
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }

        // ---- ROM untouched on a successful source write ----

        [SkippableFact]
        public void WriteShop_Success_DoesNotMutateRom()
        {
            Skip.If(FirstRom == null, "No ROM available for --write-shop ROM-untouched test");

            string projectDir = NewTempDir("romsafe");
            try
            {
                string srcDir = Path.Combine(projectDir, "src");
                Directory.CreateDirectory(srcDir);
                File.WriteAllText(Path.Combine(srcDir, "shop.c"),
                    "const u16 ItemList_Foo[] = {\n    0x0501,\n    0x0000,\n};\n");

                File.WriteAllText(Path.Combine(projectDir, "febuilder.project.json"),
                    "{ \"schemaVersion\": 1, \"builtRom\": \"synth.gba\"," +
                    "  \"tables\": [ { \"table\": \"s\", \"format\": \"u16-list\", \"writePolicy\": \"source\"," +
                    "    \"arrayName\": \"ItemList_Foo\", \"sourceFile\": \"src/shop.c\" } ] }");
                string romPath = Path.Combine(projectDir, "synth.gba");
                File.Copy(FirstRom!, romPath, overwrite: true);
                byte[] romBefore = File.ReadAllBytes(romPath);

                string args = $"--write-shop --project=\"{projectDir}\" --symbol=ItemList_Foo --items=0x02:3";
                var (code, stdout, stderr) = RunWithRetry(args);

                Assert.True(code == 0, $"exit {code}\nStdout:{stdout}\nStderr:{stderr}");
                // The preview ROM is never written by a source-only writer.
                Assert.Equal(romBefore, File.ReadAllBytes(romPath));
            }
            finally
            {
                try { Directory.Delete(projectDir, true); } catch { }
            }
        }
    }
}
