// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace FEBuilderGBA
{
    /// <summary>
    /// Validates the user-selected FEMapCreator program path and builds a safe, shell-free
    /// launch specification for either a native executable or a managed <c>.dll</c>.
    /// </summary>
    public static class FEMapCreatorLauncherCore
    {
        const int UnixExecuteAccessMode = 1;

        [DllImport("libc", EntryPoint = "access", SetLastError = true)]
        static extern int UnixAccess(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int mode);

        internal sealed class FEMapCreatorLaunchSpec
        {
            public bool Success;
            public RandomMapGeneratorErrorCategory ErrorCategory;
            public string ErrorMessage = "";
            public string ProgramPath = "";
            public string Command = "";
            public string WorkingDirectory = "";
            public List<string> Arguments = new List<string>();
            public bool UsesManagedHost;
        }

        /// <summary>
        /// Build a launch specification for FEMapCreator using only structural arguments.
        /// Returns a typed validation failure and never throws.
        /// </summary>
        internal static FEMapCreatorLaunchSpec CreateLaunchSpec(
            string feMapCreatorPath,
            IEnumerable<string> toolArguments)
        {
            var spec = new FEMapCreatorLaunchSpec();
            try
            {
                if (!TryNormalizeAbsoluteLocalFile(rawPath: feMapCreatorPath, requireDirectoryComponent: true,
                    out string fullProgramPath, out string error))
                {
                    spec.ErrorCategory = RandomMapGeneratorErrorCategory.InvalidPath;
                    spec.ErrorMessage = error;
                    return spec;
                }

                if (Directory.Exists(fullProgramPath))
                {
                    spec.ErrorCategory = RandomMapGeneratorErrorCategory.InvalidPath;
                    spec.ErrorMessage = "FEMapCreator path points to a directory, not a file: " + fullProgramPath;
                    return spec;
                }
                if (!File.Exists(fullProgramPath))
                {
                    spec.ErrorCategory = RandomMapGeneratorErrorCategory.InvalidPath;
                    spec.ErrorMessage = "FEMapCreator path does not exist: " + fullProgramPath;
                    return spec;
                }

                string extension = Path.GetExtension(fullProgramPath);
                string normalizedExtension = extension == null ? "" : extension.ToLowerInvariant();
                string workingDirectory = Path.GetDirectoryName(fullProgramPath) ?? "";
                var arguments = new List<string>();
                if (toolArguments != null)
                    arguments.AddRange(toolArguments);

                if (string.Equals(normalizedExtension, ".dll", StringComparison.Ordinal))
                {
                    spec.Success = true;
                    spec.UsesManagedHost = true;
                    spec.ProgramPath = fullProgramPath;
                    spec.WorkingDirectory = workingDirectory;

                    string hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                    if (TryNormalizeAbsoluteLocalFile(hostPath, requireDirectoryComponent: false,
                        out string fullHostPath, out string _)
                        && File.Exists(fullHostPath)
                        && IsUsableManagedHost(fullHostPath))
                    {
                        spec.Command = fullHostPath;
                    }
                    else
                    {
                        spec.Command = "dotnet";
                    }

                    spec.Arguments.Add(fullProgramPath);
                    spec.Arguments.AddRange(arguments);
                    return spec;
                }

                bool isNativeExecutable = OperatingSystem.IsWindows()
                    ? string.Equals(normalizedExtension, ".exe", StringComparison.Ordinal)
                    : HasUnixExecuteAccess(fullProgramPath);
                if (isNativeExecutable)
                {
                    spec.Success = true;
                    spec.ProgramPath = fullProgramPath;
                    spec.Command = fullProgramPath;
                    spec.WorkingDirectory = workingDirectory;
                    spec.Arguments.AddRange(arguments);
                    return spec;
                }

                spec.ErrorCategory = RandomMapGeneratorErrorCategory.InvalidPath;
                spec.ErrorMessage = OperatingSystem.IsWindows()
                    ? "Unsupported FEMapCreator program type: " + fullProgramPath
                    : "FEMapCreator native program is not executable: " + fullProgramPath;
                return spec;
            }
            catch (Exception ex)
            {
                spec.ErrorCategory = RandomMapGeneratorErrorCategory.InvalidPath;
                spec.ErrorMessage = "Unable to validate FEMapCreator path: " + ex.Message;
                return spec;
            }
        }

        static bool HasUnixExecuteAccess(string path)
        {
            if (OperatingSystem.IsWindows())
                return false;
            return UnixAccess(path, UnixExecuteAccessMode) == 0;
        }

        static bool IsUsableManagedHost(string path)
        {
            if (OperatingSystem.IsWindows())
                return true;

            try
            {
                return HasUnixExecuteAccess(path);
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Validate an optional absolute assets directory override. Blank values mean
        /// "not supplied" and return true with <paramref name="normalizedAssetsDir"/> empty.
        /// Never throws.
        /// </summary>
        internal static bool TryNormalizeAssetsDirectory(
            string assetsDir,
            out string normalizedAssetsDir,
            out string error)
        {
            normalizedAssetsDir = "";
            error = "";

            if (string.IsNullOrWhiteSpace(assetsDir))
                return true;

            if (!TryNormalizeAbsoluteLocalPath(assetsDir, requireDirectoryComponent: true,
                out string fullPath, out error))
            {
                return false;
            }

            if (!Directory.Exists(fullPath))
            {
                error = "Assets directory does not exist: " + fullPath;
                return false;
            }

            normalizedAssetsDir = fullPath;
            return true;
        }

        /// <summary>OS-appropriate path comparison for containment checks.</summary>
        internal static StringComparison PathComparison =>
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>
        /// Resolve both paths physically (including ancestor symlinks/junctions)
        /// and check that <paramref name="candidatePath"/> stays under
        /// <paramref name="rootPath"/>.
        /// </summary>
        internal static bool IsUnderRoot(string candidatePath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
                return false;

            try
            {
                string physicalCandidate = BuildfilePathSafety.ResolvePhysicalPath(candidatePath);
                string physicalRoot = BuildfilePathSafety.ResolvePhysicalPath(rootPath);
                if (string.Equals(physicalCandidate, physicalRoot, PathComparison))
                    return true;

                string rootWithSeparator =
                    Path.TrimEndingDirectorySeparator(physicalRoot)
                    + Path.DirectorySeparatorChar;
                return physicalCandidate.StartsWith(rootWithSeparator, PathComparison);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        internal static bool TryNormalizeAbsoluteLocalFile(
            string rawPath,
            bool requireDirectoryComponent,
            out string fullPath,
            out string error)
        {
            fullPath = "";
            error = "";

            if (!TryNormalizeAbsoluteLocalPath(rawPath, requireDirectoryComponent, out fullPath, out error))
                return false;

            if (Directory.Exists(fullPath))
            {
                error = "Path points to a directory, not a file: " + fullPath;
                return false;
            }

            return true;
        }

        static bool TryNormalizeAbsoluteLocalPath(
            string rawPath,
            bool requireDirectoryComponent,
            out string fullPath,
            out string error)
        {
            fullPath = "";
            error = "";

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                error = "Path is empty.";
                return false;
            }

            string candidate = rawPath.Trim();
            if (candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains("://", StringComparison.Ordinal))
            {
                error = "Path must be a local absolute file path, not a URL: " + rawPath;
                return false;
            }

            if (!Path.IsPathFullyQualified(candidate))
            {
                error = "Path must be fully qualified and absolute: " + rawPath;
                return false;
            }

            if (requireDirectoryComponent)
            {
                string directoryComponent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(directoryComponent))
                {
                    error = "Path must include an explicit directory component and must not rely on PATH lookup: " + rawPath;
                    return false;
                }
            }

            try
            {
                fullPath = Path.GetFullPath(candidate);
                return true;
            }
            catch (Exception ex)
            {
                error = "Path could not be normalized: " + ex.Message;
                return false;
            }
        }
    }
}
