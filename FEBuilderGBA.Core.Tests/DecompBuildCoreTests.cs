using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class DecompBuildCoreTests
    {
        // ---- helpers ----

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                $"decomp_build_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteManifest(string dir, string json)
        {
            File.WriteAllText(Path.Combine(dir, "febuilder.project.json"), json);
        }

        private static byte[] TinyGba()
        {
            // Minimal 512-byte buffer — just enough for ROM detection not to crash.
            var data = new byte[512];
            // "AGBJ" in game-code area
            data[0xAC] = (byte)'A'; data[0xAD] = (byte)'G';
            data[0xAE] = (byte)'B'; data[0xAF] = (byte)'J';
            return data;
        }

        private static string CopyTinyGba(string dir, string name = "out.gba")
        {
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, TinyGba());
            return path;
        }

        // Per-OS build command that copies src.gba → out.gba using relative paths
        // (ProcessRunnerCore sets the working dir to ProjectRoot, so relative paths work)
        private static string CopyCommandJson(string dir)
        {
            // Use relative paths since ProjectRoot is the working directory
            if (OperatingSystem.IsWindows())
            {
                // cmd /c copy /y src.gba out.gba — no quoting needed for simple names
                return "{\"command\":\"cmd\",\"args\":[\"/c\",\"copy /y src.gba out.gba\"]}";
            }
            return "{\"command\":\"cp\",\"args\":[\"src.gba\",\"out.gba\"]}";
        }

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ---- manifest getter tests ----

        [Fact]
        public void BuildEnabled_NoBuildSection_ReturnsFalse()
        {
            var m = new DecompManifest();
            Assert.False(m.BuildEnabled);
        }

        [Fact]
        public void BuildEnabled_EmptyObject_ReturnsTrue()
        {
            var m = JsonSerializer.Deserialize<DecompManifest>("{\"build\":{}}");
            Assert.True(m!.BuildEnabled);
        }

        [Fact]
        public void BuildEnabled_StringBuild_ReturnsTrue()
        {
            var m = JsonSerializer.Deserialize<DecompManifest>("{\"build\":\"make\"}");
            Assert.True(m!.BuildEnabled);
            Assert.Equal("make", m.BuildCommand);
        }

        [Fact]
        public void BuildEnabled_ExplicitEnabledFalse_OptsOutEvenWithCommand()
        {
            // Security: an explicit "enabled": false is a deliberate opt-OUT even
            // when other build keys (command/args) are present.
            var m = JsonSerializer.Deserialize<DecompManifest>(
                "{\"build\":{\"enabled\":false,\"command\":\"make\"}}");
            Assert.False(m!.BuildEnabled);
        }

        [Fact]
        public void Build_ExplicitEnabledFalse_ReturnsNotOptedIn()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir,
                    "{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"build\":{\"enabled\":false,\"command\":\"make\"}}");
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);

                var result = DecompBuildCore.Build(project!, 30_000);
                Assert.Equal(DecompBuildStatus.NotOptedIn, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void BuildArgs_ObjectWithArgs_ReturnsArray()
        {
            var m = JsonSerializer.Deserialize<DecompManifest>("{\"build\":{\"command\":\"make\",\"args\":[\"all\",\"-j4\"]}}");
            Assert.NotNull(m!.BuildArgs);
            Assert.Equal(new[] { "all", "-j4" }, m.BuildArgs);
        }

        [Fact]
        public void BuildArgs_NoBuildSection_ReturnsEmpty()
        {
            var m = new DecompManifest();
            Assert.Empty(m.BuildArgs);
        }

        [Fact]
        public void CompareTarget_ObjectWithCompareTarget_ReturnsString()
        {
            var m = JsonSerializer.Deserialize<DecompManifest>("{\"build\":{\"compareTarget\":\"baserom.gba\"}}");
            Assert.Equal("baserom.gba", m!.CompareTarget);
        }

        [Fact]
        public void CompareTarget_NoBuildSection_ReturnsNull()
        {
            var m = new DecompManifest();
            Assert.Null(m.CompareTarget);
        }

        // ---- Build() tests ----

        [Fact]
        public void Build_NullProject_ReturnsNotStarted()
        {
            var result = DecompBuildCore.Build(null, 30_000);
            Assert.Equal(DecompBuildStatus.NotStarted, result.Status);
        }

        [Fact]
        public void Build_ProjectWithNoBuildSection_ReturnsNotOptedIn()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir, "{\"schemaVersion\":1,\"builtRom\":\"out.gba\"}");
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);

                var result = DecompBuildCore.Build(project!, 30_000);
                Assert.Equal(DecompBuildStatus.NotOptedIn, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_SuccessfulBuild_CopiesSrcToOut()
        {
            string dir = NewTempDir();
            try
            {
                // Write src.gba (tiny fixture)
                File.WriteAllBytes(Path.Combine(dir, "src.gba"), TinyGba());

                string buildJson = CopyCommandJson(dir);
                WriteManifest(dir, $"{{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"build\":{buildJson}}}");

                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);

                var result = DecompBuildCore.Build(project!, 30_000);
                Assert.Equal(DecompBuildStatus.Success, result.Status);
                Assert.True(File.Exists(Path.Combine(dir, "out.gba")));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Build_FailingCommand_ReturnsFailed()
        {
            string dir = NewTempDir();
            try
            {
                string exitScript = OperatingSystem.IsWindows()
                    ? "{\"command\":\"cmd\",\"args\":[\"/c\",\"exit 1\"]}"
                    : "{\"command\":\"/bin/sh\",\"args\":[\"-c\",\"exit 1\"]}";
                WriteManifest(dir, $"{{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"build\":{exitScript}}}");

                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);

                var result = DecompBuildCore.Build(project!, 30_000);
                Assert.Equal(DecompBuildStatus.Failed, result.Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- GetEffectiveCommandLine() tests ----

        [Fact]
        public void GetEffectiveCommandLine_NotOptedIn_ReturnsEmpty()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir, "{\"schemaVersion\":1,\"builtRom\":\"out.gba\"}");
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                Assert.Equal("", DecompBuildCore.GetEffectiveCommandLine(project!));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void GetEffectiveCommandLine_WithArgs_IncludesArgs()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir, "{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"build\":{\"command\":\"make\",\"args\":[\"all\"]}}");
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                string cl = DecompBuildCore.GetEffectiveCommandLine(project!);
                Assert.Contains("make", cl);
                Assert.Contains("all", cl);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- IsStale() tests ----

        [Fact]
        public void IsStale_NullProject_ReturnsFalse()
        {
            Assert.False(DecompBuildCore.IsStale(null));
        }

        [Fact]
        public void IsStale_NeedsRebuildTrue_ReturnsTrue()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir, "{\"schemaVersion\":1,\"builtRom\":\"out.gba\"}");
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                CopyTinyGba(dir); // create out.gba
                project!.BuiltRomPath = Path.Combine(dir, "out.gba");
                project.NeedsRebuild = true;
                Assert.True(DecompBuildCore.IsStale(project));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void IsStale_NewerSourceFile_ReturnsTrue()
        {
            string dir = NewTempDir();
            try
            {
                // Create out.gba with old mtime
                string outGba = Path.Combine(dir, "out.gba");
                File.WriteAllBytes(outGba, TinyGba());
                File.SetLastWriteTimeUtc(outGba, DateTime.UtcNow.AddHours(-1));

                // Create source file with newer mtime
                string srcDir = Path.Combine(dir, "src");
                Directory.CreateDirectory(srcDir);
                string srcFile = Path.Combine(srcDir, "data.c");
                File.WriteAllText(srcFile, "// source");
                // Ensure it's "newer" than out.gba
                File.SetLastWriteTimeUtc(srcFile, DateTime.UtcNow.AddMinutes(1));

                string tables = "[{\"table\":\"units\",\"sourceFile\":\"src/data.c\"}]";
                WriteManifest(dir, $"{{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"tables\":{tables}}}");

                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                project!.BuiltRomPath = outGba;
                project.NeedsRebuild = false;

                Assert.True(DecompBuildCore.IsStale(project));
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---- ReloadBuiltRom() tests ----

        [Fact]
        public void ReloadBuiltRom_NullProject_ReturnsNotProject()
        {
            var status = DecompBuildCore.ReloadBuiltRom(null, (p, fv) => true);
            Assert.Equal(DecompResolveStatus.NotProject, status);
        }

        [Fact]
        public void ReloadBuiltRom_NullLoadSeam_ReturnsNotProject()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir, "{\"schemaVersion\":1,\"builtRom\":\"out.gba\"}");
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                var status = DecompBuildCore.ReloadBuiltRom(project!, null);
                Assert.Equal(DecompResolveStatus.NotProject, status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ReloadBuiltRom_LoadSeamSucceeds_ClearsNeedsRebuild()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir, "{\"schemaVersion\":1,\"builtRom\":\"out.gba\"}");
                CopyTinyGba(dir); // create out.gba
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                // Start with NO built ROM path so we can prove the success
                // path SETS it (only after the load seam returns true).
                project!.BuiltRomPath = null;
                project.NeedsRebuild = true;

                // Stub loadSeam that always returns true
                var status = DecompBuildCore.ReloadBuiltRom(project, (p, fv) => true);

                Assert.Equal(DecompResolveStatus.Ok, status);
                Assert.False(project.NeedsRebuild);
                // BuiltRomPath is set to the resolved built ROM after success.
                Assert.False(string.IsNullOrEmpty(project.BuiltRomPath));
                Assert.Equal(Path.Combine(dir, "out.gba"), project.BuiltRomPath);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ReloadBuiltRom_LoadSeamFails_KeepsNeedsRebuild_AndBuiltRomPath()
        {
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir, "{\"schemaVersion\":1,\"builtRom\":\"out.gba\"}");
                CopyTinyGba(dir); // create out.gba
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                // Seed a DISTINCT stale BuiltRomPath; on load-seam failure it
                // must stay EXACTLY as-is (finding 4: no premature mutation).
                const string staleBuiltRom = "/some/stale/previous.gba";
                project!.BuiltRomPath = staleBuiltRom;
                project.NeedsRebuild = true;

                // Stub loadSeam that always returns false
                var status = DecompBuildCore.ReloadBuiltRom(project, (p, fv) => false);

                Assert.NotEqual(DecompResolveStatus.Ok, status);
                Assert.True(project.NeedsRebuild); // NOT cleared on failure
                // BuiltRomPath is UNCHANGED on failure — never advertise a
                // freshly-resolved built ROM that did not actually load.
                Assert.Equal(staleBuiltRom, project.BuiltRomPath);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ReloadBuiltRom_PassesForceVersionToLoadSeam()
        {
            // Finding 2 parity: the seam receives project.ForceVersion so the
            // Avalonia/CLI loaders can honour a manifest-forced version.
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir,
                    "{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"forceVersion\":\"FE8U\"}");
                CopyTinyGba(dir); // create out.gba
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                project!.NeedsRebuild = true;

                string seenForceVersion = null;
                var status = DecompBuildCore.ReloadBuiltRom(project, (p, fv) =>
                {
                    seenForceVersion = fv;
                    return true;
                });

                Assert.Equal(DecompResolveStatus.Ok, status);
                Assert.Equal(project.ForceVersion, seenForceVersion);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ReloadBuiltRom_PassesForceVersionFE8J_ToLoadSeam()
        {
            // #1777: the JP (FE8J) manifest forceVersion must reach the reload seam so
            // the Avalonia/CLI loaders pin the FE8J variant (parity with the FE8U test).
            string dir = NewTempDir();
            try
            {
                WriteManifest(dir,
                    "{\"schemaVersion\":1,\"builtRom\":\"out.gba\",\"forceVersion\":\"FE8J\"}");
                CopyTinyGba(dir); // create out.gba
                var project = DecompProjectDetector.Detect(dir);
                Assert.NotNull(project);
                Assert.Equal("FE8J", project!.ForceVersion);
                project.NeedsRebuild = true;

                string? seenForceVersion = null;
                var status = DecompBuildCore.ReloadBuiltRom(project, (p, fv) =>
                {
                    seenForceVersion = fv;
                    return true;
                });

                Assert.Equal(DecompResolveStatus.Ok, status);
                Assert.Equal("FE8J", seenForceVersion);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
