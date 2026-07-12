// SPDX-License-Identifier: GPL-3.0-or-later
// CLI front-ends for the independent schema-v1 buildfile consumer (#1936):
//   --build-buildfile      rebuild a ROM from buildfile.json + data/ and atomically publish it
//   --buildfile-roundtrip  export -> independent rebuild -> byte-compare against --rom
//
// Both verbs use strict per-command allowlists and reuse the exact regular-file / bounded-read
// / parent-traversal / device-namespace ROM-ingestion contract from --export-buildfile. Neither
// verb mutates an input ROM or project file; --build-buildfile only writes a brand-new
// destination via same-parent exclusive staging + native no-replace rename.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;

namespace FEBuilderGBA.CLI
{
    internal delegate bool BuildfileScratchDelete(string path, out string error);

    internal readonly struct BuildfileOutputBoundary
    {
        internal string ProjectPhysical { get; }
        internal FileSystemEntryIdentity ProjectIdentity { get; }
        internal FileSystemEntryIdentity DataIdentity { get; }
        internal FileSystemEntryIdentity OutputParentIdentity { get; }

        internal BuildfileOutputBoundary(
            string projectPhysical,
            FileSystemEntryIdentity projectIdentity,
            FileSystemEntryIdentity dataIdentity,
            FileSystemEntryIdentity outputParentIdentity)
        {
            ProjectPhysical = projectPhysical;
            ProjectIdentity = projectIdentity;
            DataIdentity = dataIdentity;
            OutputParentIdentity = outputParentIdentity;
        }

        internal bool HasSameEntries(BuildfileOutputBoundary other)
            => ProjectIdentity.Equals(other.ProjectIdentity)
            && DataIdentity.Equals(other.DataIdentity)
            && OutputParentIdentity.Equals(other.OutputParentIdentity);
    }

    static partial class Program
    {
        // -------------------------------------------------------------- --build-buildfile

        static readonly HashSet<string> BuildBuildfileAllowedFlags = new(StringComparer.Ordinal)
        {
            "--build-buildfile", "--clean", "--project", "--out",
        };

        static int RunBuildBuildfile(Dictionary<string, string> argsDic)
        {
            // Reject unknown command-specific flags BEFORE any load or write.
            foreach (string key in argsDic.Keys)
            {
                if (!BuildBuildfileAllowedFlags.Contains(key))
                {
                    Console.Error.WriteLine($"Error: --build-buildfile: unknown option '{key}'.");
                    Console.Error.WriteLine("  Supported: --clean, --project, --out");
                    return 1;
                }
            }

            if (!argsDic.TryGetValue("--clean", out string cleanPath) || string.IsNullOrEmpty(cleanPath))
            {
                Console.Error.WriteLine("Error: --build-buildfile requires --clean=<clean.gba>");
                return 1;
            }
            if (!argsDic.TryGetValue("--project", out string projectPath) || string.IsNullOrEmpty(projectPath))
            {
                Console.Error.WriteLine("Error: --build-buildfile requires --project=<recipe-dir>");
                return 1;
            }
            if (!argsDic.TryGetValue("--out", out string outPath) || string.IsNullOrEmpty(outPath))
            {
                Console.Error.WriteLine("Error: --build-buildfile requires --out=<new-rom>");
                return 1;
            }

            // Reject raw parent-directory traversal in the recipe/ROM inputs BEFORE any
            // normalization/existence/load (Path.GetFullPath collapses '..' lexically).
            if (BuildfilePathSafety.ContainsParentTraversal(cleanPath))
            {
                Console.Error.WriteLine("Error: --clean must not contain parent-directory (..) path segments.");
                return 1;
            }
            if (BuildfilePathSafety.ContainsParentTraversal(projectPath))
            {
                Console.Error.WriteLine("Error: --project must not contain parent-directory (..) path segments.");
                return 1;
            }

            string cleanFull, projectFull, outFull, outParent;
            try
            {
                // Reject Windows device namespaces before any filesystem inspection.
                cleanFull = BuildfilePathSafety.NormalizeFullPath(cleanPath);
                projectFull = BuildfilePathSafety.NormalizeFullPath(projectPath);
                outFull = BuildfilePathSafety.NormalizeFullPath(outPath);
                outParent = Path.GetDirectoryName(outFull);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: invalid path: {ex.Message}");
                return 1;
            }

            if (!File.Exists(cleanFull))
            {
                Console.Error.WriteLine($"Error: Clean ROM not found: {cleanPath}");
                return 1;
            }
            if (!Directory.Exists(projectFull))
            {
                Console.Error.WriteLine($"Error: Recipe project directory not found: {projectPath}");
                return 1;
            }
            if (string.IsNullOrEmpty(outParent))
            {
                Console.Error.WriteLine($"Error: Output ROM has no parent directory (cannot write to a filesystem root): {outFull}");
                return 1;
            }
            if (!Directory.Exists(outParent))
            {
                Console.Error.WriteLine($"Error: Output parent directory does not exist: {outParent}");
                return 1;
            }
            if (File.Exists(outFull) || Directory.Exists(outFull))
            {
                Console.Error.WriteLine($"Error: Output path already exists: {outFull}");
                return 1;
            }

            if (!TryValidateBuildfileOutputBoundary(
                projectFull,
                outParent,
                out BuildfileOutputBoundary initialBoundary,
                out string boundaryError))
            {
                Console.Error.WriteLine("Error: " + boundaryError);
                return 1;
            }

            // Load the clean ROM through the exact no-follow regular-file + bounded-read contract.
            string cleanPhysical;
            try
            {
                // Resolve to the physical canonical path once (ancestor links included), matching
                // the --export-buildfile ingestion contract; explicit fault, never broad-caught away.
                cleanPhysical = BuildfilePathSafety.ResolvePhysicalPath(cleanFull);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: invalid path: {ex.Message}");
                return 1;
            }

            byte[] cleanData;
            try
            {
                using FileStream cleanInput =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(cleanPhysical);
                long cleanLength = cleanInput.Length;
                if (cleanLength > BuildfileExportOptions.MaxRomSize)
                {
                    Console.Error.WriteLine(
                        $"Error: Clean ROM ({cleanLength} bytes) exceeds the {BuildfileExportOptions.MaxRomSize}-byte (32 MiB) limit.");
                    return 1;
                }
                cleanData = ReadExactBuildfileRom(cleanInput, cleanLength, "Clean ROM");
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is NotSupportedException)
            {
                Console.Error.WriteLine(
                    "Error: Clean ROM must be a readable plain regular file: " + ex.Message);
                return 1;
            }

            RomLoader.InitEnvironment();

            var cleanRom = new ROM();
            if (!cleanRom.LoadFromBytes(cleanPhysical, cleanData, out string cleanVersion)
                || cleanRom.Data == null
                || cleanRom.Data.Length == 0)
            {
                Console.Error.WriteLine($"Error: Not a recognized GBA Fire Emblem ROM (clean): {cleanPath}");
                return 1;
            }

            BuildfileBuildResult build =
                BuildfileBuildCore.Build(
                    cleanRom,
                    initialBoundary.ProjectPhysical,
                    new BuildfileBuildOptions());
            if (!build.Success)
            {
                Console.Error.WriteLine("Error: " + build.Error);
                return 1;
            }
            if (!build.TargetIdentityMatches)
            {
                // Structural reconstruction succeeded but the rebuilt bytes do not match the
                // recipe's DECLARED target identity; --build-buildfile refuses to publish.
                Console.Error.WriteLine(
                    "Error: reconstructed ROM does not match the recipe's declared target identity; "
                    + "refusing to publish. " + build.TargetIdentityDetail);
                return 1;
            }

            if (!TryValidateBuildfileOutputBoundary(
                projectFull,
                outParent,
                out BuildfileOutputBoundary currentBoundary,
                out boundaryError))
            {
                Console.Error.WriteLine(
                    "Error: project/output boundary changed before publication: "
                    + boundaryError);
                return 1;
            }
            if (!initialBoundary.HasSameEntries(currentBoundary))
            {
                Console.Error.WriteLine(
                    "Error: Recipe project, data directory, or output parent changed "
                    + "before publication; "
                    + "refusing to write output.");
                return 1;
            }

            if (!BuildfileBuildCore.PublishBytesNoReplace(build.TargetBytes, outFull, out string publishError))
            {
                Console.Error.WriteLine("Error: " + publishError);
                return 1;
            }

            BuildfileManifest m = build.Manifest;
            Console.WriteLine($"Rebuilt ROM written to: {outFull}");
            Console.WriteLine($"  Version: {m.Version}");
            Console.WriteLine($"  Clean:  {m.Clean.Size} bytes, crc32={m.Clean.Crc32}, sha256={m.Clean.Sha256}" +
                (m.Clean.IsCanonicalOriginal ? " (canonical original)" : " (non-canonical baseline)"));
            Console.WriteLine($"  Target: {m.Target.Size} bytes, crc32={build.TargetCrc32}, sha256={build.TargetSha256}");
            if (m.Extension != null)
                Console.WriteLine($"  Extension: {m.Extension.Length} bytes from 0x{m.Extension.Start:X} filled with {m.Extension.FillByte}");
            Console.WriteLine($"  Ranges: {m.TotalRanges} ({m.TotalChangedBytes} changed bytes)");
            return 0;
        }

        internal static bool TryValidateBuildfileOutputBoundary(
            string projectPath,
            string outParent,
            out BuildfileOutputBoundary boundary,
            out string error)
        {
            boundary = default;
            error = "";
            try
            {
                string projectPhysical =
                    BuildfilePathSafety.ResolvePhysicalPath(projectPath);
                string dataPath = Path.Combine(projectPhysical, "data");
                if (!Directory.Exists(dataPath))
                {
                    error = "Recipe data directory not found: " + dataPath;
                    return false;
                }

                string dataPhysical = BuildfilePathSafety.ResolvePhysicalPath(dataPath);
                string outParentPhysical =
                    BuildfilePathSafety.ResolvePhysicalPath(outParent);
                if (BuildfilePathSafety.IsSameOrDescendantPath(
                    outParentPhysical,
                    dataPhysical))
                {
                    error = "Output parent must remain outside the authoritative "
                        + "project data directory: " + dataPhysical;
                    return false;
                }

                boundary = new BuildfileOutputBoundary(
                    projectPhysical,
                    ProjectionFileSystemSafety.CaptureExistingFileSystemEntryIdentity(
                        projectPhysical),
                    ProjectionFileSystemSafety.CaptureExistingFileSystemEntryIdentity(
                        dataPhysical),
                    ProjectionFileSystemSafety.CaptureExistingFileSystemEntryIdentity(
                        outParentPhysical));
                return true;
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is NotSupportedException
                || ex is System.Security.SecurityException
                || ex is ArgumentException)
            {
                error = "Invalid project/output path: " + ex.Message;
                return false;
            }
        }

        // ----------------------------------------------------------- --buildfile-roundtrip

        static readonly HashSet<string> BuildfileRoundTripAllowedFlags = new(StringComparer.Ordinal)
        {
            "--buildfile-roundtrip", "--rom", "--clean", "--force-version",
        };

        static int RunBuildfileRoundTrip(Dictionary<string, string> argsDic)
        {
            foreach (string key in argsDic.Keys)
            {
                if (!BuildfileRoundTripAllowedFlags.Contains(key))
                {
                    Console.Error.WriteLine($"Error: --buildfile-roundtrip: unknown option '{key}'.");
                    Console.Error.WriteLine("  Supported: --rom, --clean, --force-version");
                    return 1;
                }
            }

            if (!argsDic.TryGetValue("--rom", out string romPath) || string.IsNullOrEmpty(romPath))
            {
                Console.Error.WriteLine("Error: --buildfile-roundtrip requires --rom=<modified.gba>");
                return 1;
            }
            if (!argsDic.TryGetValue("--clean", out string cleanPath) || string.IsNullOrEmpty(cleanPath))
            {
                Console.Error.WriteLine("Error: --buildfile-roundtrip requires --clean=<clean.gba>");
                return 1;
            }

            if (BuildfilePathSafety.ContainsParentTraversal(romPath))
            {
                Console.Error.WriteLine("Error: --rom must not contain parent-directory (..) path segments.");
                return 1;
            }
            if (BuildfilePathSafety.ContainsParentTraversal(cleanPath))
            {
                Console.Error.WriteLine("Error: --clean must not contain parent-directory (..) path segments.");
                return 1;
            }

            string romFull, cleanFull;
            try
            {
                romFull = BuildfilePathSafety.NormalizeFullPath(romPath);
                cleanFull = BuildfilePathSafety.NormalizeFullPath(cleanPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: invalid path: {ex.Message}");
                return 1;
            }

            if (!File.Exists(romFull))
            {
                Console.Error.WriteLine($"Error: Modified ROM not found: {romPath}");
                return 1;
            }
            if (!File.Exists(cleanFull))
            {
                Console.Error.WriteLine($"Error: Clean ROM not found: {cleanPath}");
                return 1;
            }

            string romPhysical, cleanPhysical;
            try
            {
                romPhysical = BuildfilePathSafety.ResolvePhysicalPath(romFull);
                cleanPhysical = BuildfilePathSafety.ResolvePhysicalPath(cleanFull);
                if (BuildfilePathSafety.SameResolvedPhysicalFile(romPhysical, cleanPhysical))
                {
                    Console.Error.WriteLine("Error: --rom (modified) and --clean resolve to the same file; they must be different ROMs.");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: invalid path: {ex.Message}");
                return 1;
            }

            byte[] romData;
            byte[] cleanData;
            try
            {
                using FileStream romInput =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(romPhysical);
                using FileStream cleanInput =
                    ProjectionFileSystemSafety.OpenRegularFileForRead(cleanPhysical);

                if (ProjectionFileSystemSafety.SameOpenedFile(romInput, cleanInput))
                {
                    Console.Error.WriteLine(
                        "Error: --rom (modified) and --clean opened the same file; they must be different ROMs.");
                    return 1;
                }

                long romLength = romInput.Length;
                long cleanLength = cleanInput.Length;
                if (!ValidateBuildfileRomLengths(romLength, cleanLength, out string lengthError))
                {
                    Console.Error.WriteLine("Error: " + lengthError);
                    return 1;
                }

                romData = ReadExactBuildfileRom(romInput, romLength, "Modified ROM");
                cleanData = ReadExactBuildfileRom(cleanInput, cleanLength, "Clean ROM");
            }
            catch (Exception ex) when (ex is IOException
                || ex is UnauthorizedAccessException
                || ex is NotSupportedException)
            {
                Console.Error.WriteLine(
                    "Error: ROM inputs must be readable plain regular files: " + ex.Message);
                return 1;
            }

            RomLoader.InitEnvironment();

            // The MODIFIED ROM is the export target; --force-version applies only to it.
            string forceVersion = argsDic.TryGetValue("--force-version", out string fv) ? fv : null;
            if (!RomLoader.LoadRomFromBytes(romPhysical, romData, forceVersion))
                return 1;
            RomLoader.InitFull();

            var cleanRom = new ROM();
            if (!cleanRom.LoadFromBytes(cleanPhysical, cleanData, out string cleanVersion)
                || cleanRom.Data == null
                || cleanRom.Data.Length == 0)
            {
                Console.Error.WriteLine($"Error: Not a recognized GBA Fire Emblem ROM (clean): {cleanPath}");
                return 1;
            }

            string version = CoreState.ROM.RomInfo?.VersionToFilename ?? "";
            string patchBaseDir = ResolvePatchBaseDir(version);
            BuildfileRoundTripOperations operations =
                BuildfileRoundTripOperations.CreateProductionDefault(patchBaseDir, CoreState.Language ?? "en");

            // The exact already-read modified-ROM bytes are the SOLE drift oracle.
            return RunBuildfileRoundTrip(cleanRom, CoreState.ROM, romData, operations, Console.Out, Console.Error);
        }

        /// <summary>
        /// Deterministic drift-classification core for <c>--buildfile-roundtrip</c>. Exports
        /// (source projection OFF) into a private atomically-reserved scratch tree, independently
        /// reconstructs, verifies scratch cleanup, and compares the rebuilt bytes to the exact
        /// expected target bytes. Returns 0 (exact), 2 (byte OR declared-target drift), or 1
        /// (export/reconstruction/cleanup error). The operations seam defaults to the production
        /// exporter/reconstructor; tests inject deterministic drift with no hidden bypass.
        /// </summary>
        internal static int RunBuildfileRoundTrip(
            ROM cleanRom,
            ROM targetRom,
            byte[] expectedTargetBytes,
            BuildfileRoundTripOperations operations,
            TextWriter stdout,
            TextWriter stderr)
        {
            if (operations == null)
            {
                stderr.WriteLine("Error: round-trip operations are required.");
                return 1;
            }

            string scratchParent;
            try
            {
                // Private, bounded-name, atomically-reserved scratch PARENT; the child project
                // path passed to the exporter does not exist yet (the exporter requires that).
                scratchParent = BuildfileBuildCore.ReserveScratchDirectory(
                    Path.GetTempPath(), ".febuild-roundtrip-");
            }
            catch (Exception ex)
            {
                stderr.WriteLine("Error: could not reserve a round-trip scratch directory: " + ex.Message);
                return 1;
            }

            string projectPath = Path.Combine(scratchParent, "project");
            bool cleanupDone = false;
            try
            {
                BuildfileExportResult export;
                try
                {
                    export = operations.Export(cleanRom, targetRom, projectPath);
                }
                catch (Exception ex)
                {
                    CleanupRoundTripScratch(scratchParent, operations, stderr);
                    cleanupDone = true;
                    stderr.WriteLine("Error: round-trip export failed: " + ex.Message);
                    return 1;
                }
                if (export == null || !export.Success)
                {
                    CleanupRoundTripScratch(scratchParent, operations, stderr);
                    cleanupDone = true;
                    stderr.WriteLine("Error: round-trip export failed: " + (export?.Error ?? "no result"));
                    return 1;
                }

                BuildfileBuildResult built;
                try
                {
                    built = operations.Reconstruct(cleanRom, export.PublishedPath);
                }
                catch (Exception ex)
                {
                    CleanupRoundTripScratch(scratchParent, operations, stderr);
                    cleanupDone = true;
                    stderr.WriteLine("Error: round-trip reconstruction failed: " + ex.Message);
                    return 1;
                }

                // Always remove and verify the private scratch tree.
                bool cleanupOk = DeleteRoundTripScratch(
                    scratchParent,
                    operations,
                    out string cleanupError);
                cleanupDone = true;

                if (built == null || !built.Success)
                {
                    stderr.WriteLine("Error: round-trip reconstruction failed: " + (built?.Error ?? "no result"));
                    if (!cleanupOk)
                        stderr.WriteLine("  (round-trip scratch cleanup incomplete: " + cleanupError + ")");
                    return 1;
                }

                bool equal = BuildfileByteComparer.Equal(
                    built.TargetBytes, expectedTargetBytes, out _, out string diffDetail);

                // Cleanup failure is a hard error (exit 1), even after an otherwise exact compare.
                if (!cleanupOk)
                {
                    stderr.WriteLine("Error: round-trip scratch cleanup incomplete: " + cleanupError);
                    return 1;
                }

                if (!equal)
                {
                    stderr.WriteLine(
                        "Round-trip drift: the independently rebuilt ROM differs from --rom. " + diffDetail);
                    return 2;
                }
                if (!built.TargetIdentityMatches)
                {
                    stderr.WriteLine(
                        "Round-trip drift: the rebuilt bytes match --rom but the recipe's declared target identity does not match: "
                        + built.TargetIdentityDetail);
                    return 2;
                }

                stdout.WriteLine("Round-trip OK: the independently rebuilt ROM is byte-for-byte identical to --rom.");
                stdout.WriteLine($"  Target: {built.TargetBytes.Length} bytes, crc32={built.TargetCrc32}, sha256={built.TargetSha256}");
                return 0;
            }
            finally
            {
                if (!cleanupDone)
                    CleanupRoundTripScratch(scratchParent, operations, stderr);
            }
        }

        static bool DeleteRoundTripScratch(
            string scratchParent,
            BuildfileRoundTripOperations operations,
            out string error)
        {
            bool ok = operations?.DeleteScratch != null
                ? operations.DeleteScratch(scratchParent, out error)
                : BuildfileBuildCore.DeleteTreeAndVerifyGone(scratchParent, out error);

            // On cleanup failure, guarantee the residual scratch path is present in `error` for
            // the CLI's exit-1 diagnostic — but never duplicate it if the underlying delegate
            // (e.g. BuildfileBuildCore.DeleteTreeAndVerifyGone -> BuildfileExportCore's
            // DeleteAndVerifyGone/VerifyPathAbsent) already embedded it.
            if (!ok && !string.IsNullOrEmpty(scratchParent)
                && (string.IsNullOrEmpty(error)
                    || !error.Contains(scratchParent, StringComparison.Ordinal)))
            {
                error = string.IsNullOrEmpty(error)
                    ? "residual scratch path: " + scratchParent
                    : error + " (residual scratch path: " + scratchParent + ")";
            }
            return ok;
        }

        static void CleanupRoundTripScratch(
            string scratchParent,
            BuildfileRoundTripOperations operations,
            TextWriter stderr)
        {
            if (!DeleteRoundTripScratch(scratchParent, operations, out string error))
                stderr.WriteLine("Warning: round-trip scratch cleanup incomplete: " + error);
        }
    }

    /// <summary>
    /// Production-default operations seam for <c>--buildfile-roundtrip</c>. Defaults to the real
    /// exporter (source projection OFF) and the real structural reconstructor; test code injects
    /// deterministic drift. This is an internal test seam only — never a command-line flag or a
    /// production environment bypass.
    /// </summary>
    internal sealed class BuildfileRoundTripOperations
    {
        internal Func<ROM, ROM, string, BuildfileExportResult> Export { get; init; }
        internal Func<ROM, string, BuildfileBuildResult> Reconstruct { get; init; }
        internal BuildfileScratchDelete DeleteScratch { get; init; }

        internal static BuildfileRoundTripOperations CreateProductionDefault(
            string patchBaseDirectory,
            string language)
            => new BuildfileRoundTripOperations
            {
                Export = (clean, target, outDir) => BuildfileExportCore.Export(
                    clean,
                    target,
                    new BuildfileExportOptions
                    {
                        OutputDirectory = outDir,
                        IncludeSourceProjection = false,
                        PatchBaseDirectory = patchBaseDirectory,
                        Language = language,
                    }),
                Reconstruct = (clean, projectDir) =>
                    BuildfileBuildCore.Build(clean, projectDir, new BuildfileBuildOptions()),
                DeleteScratch = BuildfileBuildCore.DeleteTreeAndVerifyGone,
            };
    }

    /// <summary>Pure exact/first-difference byte comparison used as the round-trip oracle.</summary>
    internal static class BuildfileByteComparer
    {
        internal static bool Equal(
            byte[] rebuilt,
            byte[] expected,
            out long firstDifferenceOffset,
            out string detail)
        {
            firstDifferenceOffset = -1;
            detail = "";
            if (rebuilt == null || expected == null)
            {
                firstDifferenceOffset = 0;
                detail = "One side of the comparison is null.";
                return false;
            }

            int min = Math.Min(rebuilt.Length, expected.Length);
            for (int i = 0; i < min; i++)
            {
                if (rebuilt[i] != expected[i])
                {
                    firstDifferenceOffset = i;
                    detail = $"First difference at offset 0x{i:X} (rebuilt=0x{rebuilt[i]:X2}, expected=0x{expected[i]:X2}).";
                    return false;
                }
            }
            if (rebuilt.Length != expected.Length)
            {
                firstDifferenceOffset = min;
                detail = $"Length mismatch: rebuilt={rebuilt.Length} bytes, "
                    + $"expected={expected.Length} bytes; identical prefix length={min} bytes; "
                    + $"first length difference at offset 0x{min:X}.";
                return false;
            }
            return true;
        }
    }
}
