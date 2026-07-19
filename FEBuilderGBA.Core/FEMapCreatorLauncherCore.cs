// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Validates the user-selected FEMapCreator program path and builds a safe, shell-free
    /// launch specification for either a native executable or a managed <c>.dll</c>.
    /// </summary>
    public static class FEMapCreatorLauncherCore
    {
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
                        out string fullHostPath, out string _))
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

                if (string.Equals(normalizedExtension, ".exe", StringComparison.Ordinal)
                    || (!OperatingSystem.IsWindows() && normalizedExtension.Length == 0))
                {
                    spec.Success = true;
                    spec.ProgramPath = fullProgramPath;
                    spec.Command = fullProgramPath;
                    spec.WorkingDirectory = workingDirectory;
                    spec.Arguments.AddRange(arguments);
                    return spec;
                }

                spec.ErrorCategory = RandomMapGeneratorErrorCategory.InvalidPath;
                spec.ErrorMessage = "Unsupported FEMapCreator program type: " + fullProgramPath;
                return spec;
            }
            catch (Exception ex)
            {
                spec.ErrorCategory = RandomMapGeneratorErrorCategory.InvalidPath;
                spec.ErrorMessage = "Unable to validate FEMapCreator path: " + ex.Message;
                return spec;
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
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        /// <summary>
        /// Best-effort check that <paramref name="candidatePath"/> stays under
        /// <paramref name="rootPath"/> after full-path normalization.
        /// </summary>
        internal static bool IsUnderRoot(string candidatePath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
                return false;

            string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
            string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            if (string.Equals(normalizedCandidate, normalizedRoot, PathComparison))
                return true;

            string rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedCandidate.StartsWith(rootWithSeparator, PathComparison);
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
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
            {
                if (!uri.IsFile)
                {
                    error = "Path must be a local absolute file path, not a URL: " + rawPath;
                    return false;
                }
                candidate = uri.LocalPath;
            }

            if (!Path.IsPathRooted(candidate))
            {
                error = "Path must be absolute: " + rawPath;
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
