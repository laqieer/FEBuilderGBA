using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform path handling utilities.
    /// </summary>
    public static class PathUtil
    {
        /// <summary>True if running on Windows.</summary>
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>True if running on macOS.</summary>
        public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>True if running on Linux.</summary>
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        /// <summary>
        /// Normalize path separators for the current platform.
        /// On Windows: no change. On Unix: convert backslashes to forward slashes.
        /// </summary>
        [return: NotNullIfNotNull(nameof(path))]
        public static string? Normalize(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            if (!IsWindows)
                return path.Replace('\\', '/');
            return path;
        }

        /// <summary>
        /// Build a path under the config directory.
        /// </summary>
        public static string ConfigPath(string subpath)
        {
            return Path.Combine(CoreState.BaseDirectory ?? "", "config", subpath);
        }

        /// <summary>
        /// Resolve path for an external tool, considering platform differences.
        /// On Windows, tries .exe extension. On Unix, searches PATH.
        /// </summary>
        public static string GetExternalToolPath(string toolName)
        {
            if (IsWindows && !toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string withExe = toolName + ".exe";
                if (File.Exists(withExe))
                    return withExe;
            }
            return toolName;
        }

        /// <summary>
        /// Resolve a configured external-tool path to a launchable file. On macOS,
        /// an application bundle is resolved to a safe direct executable under
        /// <c>Contents/MacOS</c>; other platforms and ordinary files are unchanged.
        /// </summary>
        public static bool TryResolveExternalToolExecutable(
            string? configuredPath,
            out string executablePath)
        {
            return TryResolveExternalToolExecutable(
                configuredPath,
                IsMacOS,
                IsMacExecutableFile,
                out executablePath);
        }

        internal static bool TryResolveExternalToolExecutable(
            string? configuredPath,
            bool isMacOS,
            Func<string, bool> executablePredicate,
            out string executablePath)
        {
            executablePath = configuredPath ?? "";
            if (string.IsNullOrWhiteSpace(configuredPath))
                return false;

            if (File.Exists(configuredPath))
                return true;

            string bundlePath = configuredPath.TrimEnd('/', '\\');
            executablePath = bundlePath;
            if (!isMacOS
                || !bundlePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(bundlePath))
            {
                return false;
            }

            try
            {
                string physicalBundle = ResolvePhysicalPath(new DirectoryInfo(bundlePath));
                string physicalContents = Path.Combine(physicalBundle, "Contents");
                if (!IsNonSymlinkDirectory(physicalContents))
                    return false;
                string physicalMacOS = Path.Combine(physicalContents, "MacOS");
                if (!IsNonSymlinkDirectory(physicalMacOS))
                    return false;

                string bundleName = Path.GetFileNameWithoutExtension(bundlePath);
                string namedCandidate = Path.Combine(physicalMacOS, bundleName);
                if (TryAcceptBundleExecutable(
                        physicalMacOS,
                        namedCandidate,
                        executablePredicate,
                        out string namedExecutable))
                {
                    executablePath = namedExecutable;
                    return true;
                }

                var safeExecutables = new List<string>(2);
                foreach (string candidate in Directory.EnumerateFiles(
                             physicalMacOS,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (TryAcceptBundleExecutable(
                            physicalMacOS,
                            candidate,
                            executablePredicate,
                            out string safeCandidate))
                    {
                        safeExecutables.Add(safeCandidate);
                        if (safeExecutables.Count > 1)
                        {
                            executablePath = bundlePath;
                            return false;
                        }
                    }
                }

                if (safeExecutables.Count == 1)
                {
                    executablePath = safeExecutables[0];
                    return true;
                }
                executablePath = bundlePath;
                return false;
            }
            catch (IOException)
            {
                executablePath = bundlePath;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                executablePath = bundlePath;
                return false;
            }
        }

        static bool TryAcceptBundleExecutable(
            string physicalMacOS,
            string candidate,
            Func<string, bool> executablePredicate,
            out string executablePath)
        {
            executablePath = "";
            var info = new FileInfo(candidate);
            if (!info.Exists)
                return false;

            string physicalCandidate = ResolvePhysicalPath(info);
            string? parent = Path.GetDirectoryName(physicalCandidate);
            if (!string.Equals(
                    Path.GetFullPath(physicalMacOS),
                    parent == null ? "" : Path.GetFullPath(parent),
                    StringComparison.Ordinal))
            {
                return false;
            }
            if (!executablePredicate(physicalCandidate))
                return false;

            executablePath = physicalCandidate;
            return true;
        }

        static string ResolvePhysicalPath(FileSystemInfo info)
        {
            FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
            return Path.GetFullPath(target?.FullName ?? info.FullName);
        }

        static bool IsNonSymlinkDirectory(string path)
        {
            var info = new DirectoryInfo(path);
            if (!info.Exists)
                return false;
            return string.Equals(
                Path.GetFullPath(path),
                ResolvePhysicalPath(info),
                StringComparison.Ordinal);
        }

        static bool IsMacExecutableFile(string path)
        {
            if (!File.Exists(path))
                return false;
            try
            {
                UnixFileMode mode = File.GetUnixFileMode(path);
                const UnixFileMode execute =
                    UnixFileMode.UserExecute
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherExecute;
                return (mode & execute) != 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
