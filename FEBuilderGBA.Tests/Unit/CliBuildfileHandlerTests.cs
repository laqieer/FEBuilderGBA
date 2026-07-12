// SPDX-License-Identifier: GPL-3.0-or-later
// CLI-handler tests for the #1936 --buildfile-roundtrip drift classification and its pure
// byte comparer. These exercise the ACTUAL CLI handler overload through the internal
// production-default operations seam (never a hidden flag/env bypass): a deterministic
// injected reconstructed-byte drift and a declared-target-only drift both map to exit 2 with
// diagnostics, an exact rebuild maps to exit 0, and structural/export faults map to exit 1.
using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.CLI;
using CliProgram = FEBuilderGBA.CLI.Program;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class CliBuildfileHandlerTests
    {
        static BuildfileRoundTripOperations Ops(
            Func<ROM, string, BuildfileBuildResult> reconstruct)
            => new BuildfileRoundTripOperations
            {
                Export = (clean, target, outDir) =>
                    new BuildfileExportResult { Success = true, PublishedPath = outDir },
                Reconstruct = reconstruct,
            };

        static int RunRoundTrip(
            byte[] expected,
            Func<ROM, string, BuildfileBuildResult> reconstruct,
            out string stdout,
            out string stderr)
        {
            var outWriter = new StringWriter();
            var errWriter = new StringWriter();
            int code = CliProgram.RunBuildfileRoundTrip(
                new ROM(), new ROM(), expected, Ops(reconstruct), outWriter, errWriter);
            stdout = outWriter.ToString();
            stderr = errWriter.ToString();
            return code;
        }

        [Fact]
        public void RoundTrip_ByteDrift_MapsToExit2_WithFirstDifferenceDiagnostics()
        {
            byte[] expected = { 1, 2, 3, 4 };
            byte[] drifted = { 1, 2, 9, 4 }; // first difference at offset 0x2

            int code = RunRoundTrip(
                expected,
                (clean, projectDir) => new BuildfileBuildResult
                {
                    Success = true,
                    TargetBytes = drifted,
                    TargetIdentityMatches = true,
                },
                out _, out string stderr);

            Assert.Equal(2, code);
            Assert.Contains("drift", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("offset 0x2", stderr);
        }

        [Fact]
        public void RoundTrip_LengthDrift_MapsToExit2()
        {
            byte[] expected = { 1, 2, 3, 4 };
            byte[] drifted = { 1, 2, 3 }; // identical prefix but shorter

            int code = RunRoundTrip(
                expected,
                (clean, projectDir) => new BuildfileBuildResult
                {
                    Success = true,
                    TargetBytes = drifted,
                    TargetIdentityMatches = true,
                },
                out _, out string stderr);

            Assert.Equal(2, code);
            Assert.Contains("Length mismatch", stderr, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoundTrip_DeclaredTargetOnlyDrift_MapsToExit2_NotUsageError()
        {
            byte[] expected = { 1, 2, 3, 4 };

            int code = RunRoundTrip(
                expected,
                // Bytes MATCH the oracle exactly, but the recipe's declared target identity drifts.
                (clean, projectDir) => new BuildfileBuildResult
                {
                    Success = true,
                    TargetBytes = new byte[] { 1, 2, 3, 4 },
                    TargetIdentityMatches = false,
                    TargetIdentityDetail = "declared crc32=0x00000000/sha256=deadbeef, actual crc32=0x11111111/sha256=cafef00d",
                },
                out _, out string stderr);

            Assert.Equal(2, code);
            Assert.Contains("declared target identity", stderr, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoundTrip_ExactRebuild_MapsToExit0()
        {
            byte[] expected = { 10, 20, 30, 40 };

            int code = RunRoundTrip(
                expected,
                (clean, projectDir) => new BuildfileBuildResult
                {
                    Success = true,
                    TargetBytes = new byte[] { 10, 20, 30, 40 },
                    TargetIdentityMatches = true,
                    TargetCrc32 = "0x12345678",
                    TargetSha256 = "00",
                },
                out string stdout, out _);

            Assert.Equal(0, code);
            Assert.Contains("Round-trip OK", stdout, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoundTrip_StructuralReconstructionFailure_MapsToExit1()
        {
            byte[] expected = { 1, 2, 3, 4 };

            int code = RunRoundTrip(
                expected,
                (clean, projectDir) => BuildfileBuildResult.Fail("boom: structural failure"),
                out _, out string stderr);

            Assert.Equal(1, code);
            Assert.Contains("reconstruction failed", stderr, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoundTrip_ExportFailure_MapsToExit1()
        {
            var ops = new BuildfileRoundTripOperations
            {
                Export = (clean, target, outDir) => BuildfileExportResult.Fail("export blew up"),
                Reconstruct = (clean, projectDir) => throw new InvalidOperationException("must not be reached"),
            };

            var outWriter = new StringWriter();
            var errWriter = new StringWriter();
            int code = CliProgram.RunBuildfileRoundTrip(
                new ROM(), new ROM(), new byte[] { 1 }, ops, outWriter, errWriter);

            Assert.Equal(1, code);
            Assert.Contains("export failed", errWriter.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoundTrip_ScratchCleanupFailure_MapsToExit1()
        {
            string capturedScratchPath = null;
            var ops = new BuildfileRoundTripOperations
            {
                Export = (clean, target, outDir) =>
                    new BuildfileExportResult { Success = true, PublishedPath = outDir },
                Reconstruct = (clean, projectDir) => new BuildfileBuildResult
                {
                    Success = true,
                    TargetBytes = new byte[] { 1, 2, 3 },
                    TargetIdentityMatches = true,
                },
                DeleteScratch = (string path, out string error) =>
                {
                    capturedScratchPath = path;
                    BuildfileBuildCore.DeleteTreeAndVerifyGone(path, out _);
                    error = "injected cleanup verification failure";
                    return false;
                },
            };

            var errWriter = new StringWriter();
            int code = CliProgram.RunBuildfileRoundTrip(
                new ROM(),
                new ROM(),
                new byte[] { 1, 2, 3 },
                ops,
                new StringWriter(),
                errWriter);

            string stderr = errWriter.ToString();
            Assert.Equal(1, code);
            Assert.Contains("cleanup incomplete", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(capturedScratchPath);
            Assert.Contains(capturedScratchPath, stderr, StringComparison.Ordinal);
            Assert.Equal(
                1,
                stderr.Split(capturedScratchPath, StringSplitOptions.None).Length - 1);
        }

        [Fact]
        public void RoundTrip_ScratchCleanupFailure_WithPath_DoesNotDuplicateResidualPath()
        {
            string capturedScratchPath = null;
            var ops = new BuildfileRoundTripOperations
            {
                Export = (clean, target, outDir) =>
                    new BuildfileExportResult { Success = true, PublishedPath = outDir },
                Reconstruct = (clean, projectDir) => new BuildfileBuildResult
                {
                    Success = true,
                    TargetBytes = new byte[] { 1, 2, 3 },
                    TargetIdentityMatches = true,
                },
                DeleteScratch = (string path, out string error) =>
                {
                    capturedScratchPath = path;
                    BuildfileBuildCore.DeleteTreeAndVerifyGone(path, out _);
                    error = "injected cleanup verification failure at '" + path + "'";
                    return false;
                },
            };

            var errWriter = new StringWriter();
            int code = CliProgram.RunBuildfileRoundTrip(
                new ROM(),
                new ROM(),
                new byte[] { 1, 2, 3 },
                ops,
                new StringWriter(),
                errWriter);

            string stderr = errWriter.ToString();
            Assert.Equal(1, code);
            Assert.Contains("cleanup incomplete", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(capturedScratchPath);
            Assert.Equal(
                1,
                stderr.Split(capturedScratchPath, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain(
                "residual scratch path:",
                stderr,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RoundTrip_NullOperations_MapsToExit1()
        {
            var errWriter = new StringWriter();
            int code = CliProgram.RunBuildfileRoundTrip(
                new ROM(), new ROM(), new byte[] { 1 }, null, new StringWriter(), errWriter);
            Assert.Equal(1, code);
        }

        // ------------------------------------------------------------- pure byte comparer

        [Fact]
        public void ByteComparer_IdenticalArrays_ReturnsTrue()
        {
            bool equal = BuildfileByteComparer.Equal(
                new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, out long offset, out string detail);
            Assert.True(equal);
            Assert.Equal(-1, offset);
            Assert.Empty(detail);
        }

        [Fact]
        public void ByteComparer_FirstDifference_ReportsOffsetAndBytes()
        {
            bool equal = BuildfileByteComparer.Equal(
                new byte[] { 1, 2, 0x33, 4 }, new byte[] { 1, 2, 0xAA, 4 }, out long offset, out string detail);
            Assert.False(equal);
            Assert.Equal(2, offset);
            Assert.Contains("offset 0x2", detail);
            Assert.Contains("rebuilt=0x33", detail);
            Assert.Contains("expected=0xAA", detail);
        }

        [Fact]
        public void ByteComparer_LengthMismatch_ReportsIdenticalPrefix()
        {
            bool equal = BuildfileByteComparer.Equal(
                new byte[] { 1, 2, 3, 4, 5 }, new byte[] { 1, 2, 3 }, out long offset, out string detail);
            Assert.False(equal);
            Assert.Equal(3, offset);
            Assert.Contains("Length mismatch", detail);
            Assert.Contains("identical prefix length=3 bytes", detail);
            Assert.Contains("first length difference at offset 0x3", detail);
            Assert.DoesNotContain("through offset", detail);
        }

        [Fact]
        public void OutputBoundary_SamePathDirectoryReplacement_IsDetected()
        {
            string parent = Path.Combine(
                Path.GetTempPath(),
                "bfb_boundary_" + Guid.NewGuid().ToString("N"));
            string project = Path.Combine(parent, "project");
            string data = Path.Combine(project, "data");
            string outputParent = Path.Combine(parent, "output");
            string movedProject = Path.Combine(parent, "moved-project");
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(outputParent);
            try
            {
                Assert.True(CliProgram.TryValidateBuildfileOutputBoundary(
                    project,
                    outputParent,
                    out BuildfileOutputBoundary initial,
                    out string initialError),
                    initialError);

                Directory.Move(project, movedProject);
                Directory.CreateDirectory(Path.Combine(project, "data"));

                Assert.True(CliProgram.TryValidateBuildfileOutputBoundary(
                    project,
                    outputParent,
                    out BuildfileOutputBoundary current,
                    out string currentError),
                    currentError);
                Assert.False(initial.HasSameEntries(current));
            }
            finally
            {
                if (Directory.Exists(parent))
                    Directory.Delete(parent, recursive: true);
            }
        }
    }
}
