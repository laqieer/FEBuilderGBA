using System;
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
        public static string Normalize(string path)
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
    }
}
