using System;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Manages version information for split package updates.
    /// Supports independent versioning for core application and patch2 data.
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// Core application version (from assembly build date)
        /// Format: yyyyMMdd.HH (e.g., "20260226.00")
        /// </summary>
        public string VERSION_CORE { get; private set; }

        /// <summary>
        /// Patch2 version (from config/patch2/version.txt)
        /// Format: yyyyMMdd.HH (e.g., "20260226.00")
        /// </summary>
        public string VERSION_PATCH2 { get; private set; }

        /// <summary>
        /// Download URL for full package (core + patch2)
        /// </summary>
        public string URL_FULL { get; set; }

        /// <summary>
        /// Download URL for core application only
        /// </summary>
        public string URL_CORE { get; set; }

        /// <summary>
        /// Download URL for patch2 data only
        /// </summary>
        public string URL_PATCH2 { get; set; }

        /// <summary>
        /// Package type enum for identifying what needs to be updated
        /// </summary>
        public enum PackageType
        {
            Unknown,
            Full,        // Both core and patch2 need updates
            CoreOnly,    // Only core application needs update
            Patch2Only,  // Only patch2 data needs update
            None         // Already up to date
        }

        /// <summary>
        /// Initializes UpdateInfo with current local versions
        /// </summary>
        public UpdateInfo()
        {
            VERSION_CORE = U.getVersion();
            VERSION_PATCH2 = ReadPatch2Version();
        }

        /// <summary>
        /// Reads the patch2 version from config/patch2/version.txt
        /// Returns "00000000.00" if file doesn't exist or is invalid
        /// </summary>
        public static string ReadPatch2Version()
        {
            try
            {
                // Handle unit test environment where Program.BaseDirectory may be null
                if (string.IsNullOrEmpty(Program.BaseDirectory))
                {
                    return "00000000.00";
                }

                string versionFile = Path.Combine(Program.BaseDirectory, "config", "patch2", "version.txt");
                if (!File.Exists(versionFile))
                {
                    // Only log if we're in a real environment, not unit tests
                    if (!string.IsNullOrEmpty(Program.BaseDirectory))
                    {
                        Log.Error($"Patch2 version file not found: {versionFile}");
                    }
                    return "00000000.00";
                }

                string content = File.ReadAllText(versionFile).Trim();

                // Validate format: yyyyMMdd.HH
                if (System.Text.RegularExpressions.Regex.IsMatch(content, @"^\d{8}\.\d{2}$"))
                {
                    return content;
                }

                if (!string.IsNullOrEmpty(Program.BaseDirectory))
                {
                    Log.Error($"Invalid patch2 version format: {content}");
                }
                return "00000000.00";
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(Program.BaseDirectory))
                {
                    Log.Error($"Error reading patch2 version: {e}");
                }
                return "00000000.00";
            }
        }

        /// <summary>
        /// Writes the patch2 version to config/patch2/version.txt
        /// </summary>
        public static void WritePatch2Version(string version)
        {
            try
            {
                // Handle unit test environment where Program.BaseDirectory may be null
                if (string.IsNullOrEmpty(Program.BaseDirectory))
                {
                    throw new InvalidOperationException("Program.BaseDirectory is not initialized");
                }

                string versionFile = Path.Combine(Program.BaseDirectory, "config", "patch2", "version.txt");
                string versionDir = Path.GetDirectoryName(versionFile);

                if (!Directory.Exists(versionDir))
                {
                    Directory.CreateDirectory(versionDir);
                }

                File.WriteAllText(versionFile, version + Environment.NewLine);
                if (!string.IsNullOrEmpty(Program.BaseDirectory))
                {
                    Log.Debug($"Updated patch2 version to: {version}");
                }
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(Program.BaseDirectory))
                {
                    Log.Error($"Error writing patch2 version: {e}");
                }
                throw;
            }
        }

        /// <summary>
        /// Compares two version strings and returns which is newer
        /// Returns: &lt;0 if v1 &lt; v2, 0 if equal, &gt;0 if v1 &gt; v2
        /// </summary>
        public static int CompareVersions(string v1, string v2)
        {
            double version1 = U.atof(v1);
            double version2 = U.atof(v2);

            if (version1 < version2) return -1;
            if (version1 > version2) return 1;
            return 0;
        }

        /// <summary>
        /// Determines what package type needs to be downloaded based on remote versions
        /// </summary>
        /// <param name="remoteCore">Remote core application version</param>
        /// <param name="remotePatch2">Remote patch2 version</param>
        /// <returns>PackageType indicating what needs updating</returns>
        public PackageType DetermineUpdateType(string remoteCore, string remotePatch2)
        {
            bool coreNeedsUpdate = CompareVersions(VERSION_CORE, remoteCore) < 0;
            bool patch2NeedsUpdate = CompareVersions(VERSION_PATCH2, remotePatch2) < 0;

            if (coreNeedsUpdate && patch2NeedsUpdate)
            {
                return PackageType.Full;
            }
            else if (coreNeedsUpdate)
            {
                return PackageType.CoreOnly;
            }
            else if (patch2NeedsUpdate)
            {
                return PackageType.Patch2Only;
            }
            else
            {
                return PackageType.None;
            }
        }

        /// <summary>
        /// Gets the appropriate download URL based on package type
        /// </summary>
        public string GetDownloadUrl(PackageType type)
        {
            switch (type)
            {
                case PackageType.Full:
                    return URL_FULL;
                case PackageType.CoreOnly:
                    return URL_CORE;
                case PackageType.Patch2Only:
                    return URL_PATCH2;
                default:
                    return "";
            }
        }

        /// <summary>
        /// Checks if the specified URL is valid and non-empty
        /// </summary>
        public bool HasUrl(PackageType type)
        {
            string url = GetDownloadUrl(type);
            return !string.IsNullOrEmpty(url);
        }

        /// <summary>
        /// Creates a formatted version display string
        /// </summary>
        public string GetVersionDisplay()
        {
            return $"Core: {VERSION_CORE}, Patch2: {VERSION_PATCH2}";
        }

        /// <summary>
        /// Validates that version strings are in correct format
        /// </summary>
        public static bool IsValidVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return false;

            // Format: yyyyMMdd.HH (e.g., "20260226.00")
            return System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d{8}\.\d{2}$");
        }
    }
}
