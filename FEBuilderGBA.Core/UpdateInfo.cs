using System;

namespace FEBuilderGBA
{
    /// <summary>
    /// Manages version information for core application updates.
    /// Patch2 data is versioned and updated via git, not via build artifacts.
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// Core application version (from assembly build date)
        /// Format: yyyyMMdd.HH (e.g., "20260226.00")
        /// </summary>
        public string VERSION_CORE { get; private set; }

        /// <summary>
        /// Download URL for core application only
        /// </summary>
        public string URL_CORE { get; set; }

        /// <summary>
        /// Package type enum for identifying what needs to be updated
        /// </summary>
        public enum PackageType
        {
            Unknown,
            CoreOnly,    // Core application needs update
            None         // Already up to date
        }

        /// <summary>
        /// Initializes UpdateInfo with current local version
        /// </summary>
        public UpdateInfo()
        {
            VERSION_CORE = U.getVersion();
        }

        /// <summary>
        /// Compares two version strings and returns which is newer.
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
        /// Determines whether the core application needs updating.
        /// </summary>
        public PackageType DetermineUpdateType(string remoteCore)
        {
            bool coreNeedsUpdate = CompareVersions(VERSION_CORE, remoteCore) < 0;
            return coreNeedsUpdate ? PackageType.CoreOnly : PackageType.None;
        }

        /// <summary>
        /// Gets the appropriate download URL based on package type.
        /// </summary>
        public string GetDownloadUrl(PackageType type)
        {
            switch (type)
            {
                case PackageType.CoreOnly:
                    return URL_CORE;
                default:
                    return "";
            }
        }

        /// <summary>
        /// Checks if a valid URL exists for the specified package type.
        /// </summary>
        public bool HasUrl(PackageType type)
        {
            string url = GetDownloadUrl(type);
            return !string.IsNullOrEmpty(url);
        }

        /// <summary>
        /// Creates a formatted version display string.
        /// </summary>
        public string GetVersionDisplay()
        {
            return $"Core: {VERSION_CORE}";
        }

        /// <summary>
        /// Validates that a version string is in correct format (yyyyMMdd.HH).
        /// </summary>
        public static bool IsValidVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return false;

            return System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d{8}\.\d{2}$");
        }
    }
}
