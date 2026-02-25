using System;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Extensions to UpdateCheck for split package support
    /// Handles detection and downloading of Core/Patch2 packages separately
    /// </summary>
    public static class UpdateCheckSplitPackage
    {
        /// <summary>
        /// Checks for split package updates on GitHub
        /// Looks for three possible packages: FULL, CORE, PATCH2
        /// Falls back to legacy single package if split packages not found
        /// </summary>
        public static string CheckSplitPackageUpdateByGitHub(out UpdateInfo updateInfo)
        {
            updateInfo = new UpdateInfo();

            string url = "https://api.github.com/repos/laqieer/FEBuilderGBA/releases/latest";
            string contents;
            try
            {
                contents = U.HttpGet(url);
            }
            catch (Exception e)
            {
#if DEBUG
                R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
                throw;
#else
                return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", url, e.ToString());
#endif
            }

            // Try to parse split package format first
            // Expected asset names:
            // - FEBuilderGBA_FULL_20260226.00_20260226.00.7z  (core_version_patch2_version)
            // - FEBuilderGBA_CORE_20260226.00.7z              (core_version)
            // - FEBuilderGBA_PATCH2_20260226.00.7z            (patch2_version)

            string fullPattern = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_FULL_(\d{8}\.\d{2})_(\d{8}\.\d{2})\.7z)""";
            string corePattern = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_CORE_(\d{8}\.\d{2})\.7z)""";
            string patch2Pattern = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_PATCH2_(\d{8}\.\d{2})\.7z)""";

            Match fullMatch = RegexCache.Match(contents, fullPattern);
            Match coreMatch = RegexCache.Match(contents, corePattern);
            Match patch2Match = RegexCache.Match(contents, patch2Pattern);

            bool hasSplitPackages = (coreMatch.Success || patch2Match.Success);

            if (hasSplitPackages)
            {
                // Parse split package versions
                string remoteCoreVersion = "00000000.00";
                string remotePatch2Version = "00000000.00";

                if (fullMatch.Success && fullMatch.Groups.Count >= 4)
                {
                    updateInfo.URL_FULL = fullMatch.Groups[1].Value;
                    remoteCoreVersion = fullMatch.Groups[2].Value;
                    remotePatch2Version = fullMatch.Groups[3].Value;
                }

                if (coreMatch.Success && coreMatch.Groups.Count >= 3)
                {
                    updateInfo.URL_CORE = coreMatch.Groups[1].Value;
                    string coreVer = coreMatch.Groups[2].Value;
                    if (UpdateInfo.CompareVersions(coreVer, remoteCoreVersion) > 0)
                    {
                        remoteCoreVersion = coreVer;
                    }
                }

                if (patch2Match.Success && patch2Match.Groups.Count >= 3)
                {
                    updateInfo.URL_PATCH2 = patch2Match.Groups[1].Value;
                    string patch2Ver = patch2Match.Groups[2].Value;
                    if (UpdateInfo.CompareVersions(patch2Ver, remotePatch2Version) > 0)
                    {
                        remotePatch2Version = patch2Ver;
                    }
                }

                // Determine what needs updating
                UpdateInfo.PackageType packageType = updateInfo.DetermineUpdateType(remoteCoreVersion, remotePatch2Version);

                if (packageType == UpdateInfo.PackageType.None)
                {
                    return R._("現在のバージョンが最新です。version:{0}", updateInfo.GetVersionDisplay());
                }

                // Check if we have the appropriate download URL
                if (!updateInfo.HasUrl(packageType))
                {
                    return R._("必要なパッケージが見つかりません。PackageType:{0}", packageType.ToString());
                }

                return ""; // Success
            }
            else
            {
                // Fall back to legacy single package format
                // Expected: FEBuilderGBA_20260226.00.7z (includes both core and patch2)
                string legacyPattern = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_(\d{8}\.\d{2})\.7z)""";
                Match legacyMatch = RegexCache.Match(contents, legacyPattern);

                if (legacyMatch.Success && legacyMatch.Groups.Count >= 3)
                {
                    string downloadUrl = legacyMatch.Groups[1].Value;
                    string version = legacyMatch.Groups[2].Value;

                    // In legacy format, one package updates everything
                    updateInfo.URL_FULL = downloadUrl;

                    // Compare with current core version (legacy format doesn't distinguish)
                    if (UpdateInfo.CompareVersions(updateInfo.VERSION_CORE, version) >= 0)
                    {
                        return R._("現在のバージョンが最新です。version:{0}", updateInfo.VERSION_CORE);
                    }

                    return ""; // Success
                }
                else
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "browser_download_url not found" + "\r\n"
                        + "contents:\r\n" + contents;
                }
            }
        }

        /// <summary>
        /// Gets the appropriate download URL based on what needs updating
        /// Returns the most efficient package choice
        /// </summary>
        public static string GetDownloadUrl(UpdateInfo updateInfo, out UpdateInfo.PackageType packageType)
        {
            // If URL_FULL is the only option, use it regardless of what changed
            if (!string.IsNullOrEmpty(updateInfo.URL_FULL) &&
                string.IsNullOrEmpty(updateInfo.URL_CORE) &&
                string.IsNullOrEmpty(updateInfo.URL_PATCH2))
            {
                packageType = UpdateInfo.PackageType.Full;
                return updateInfo.URL_FULL;
            }

            // Determine optimal package based on what's available
            string remoteCoreVer = ExtractVersionFromUrl(updateInfo.URL_CORE ?? updateInfo.URL_FULL, 0);
            string remotePatch2Ver = ExtractVersionFromUrl(updateInfo.URL_PATCH2 ?? updateInfo.URL_FULL, 1);

            packageType = updateInfo.DetermineUpdateType(remoteCoreVer, remotePatch2Ver);

            // Prefer split packages over full package when only one component needs updating
            if (packageType == UpdateInfo.PackageType.CoreOnly && !string.IsNullOrEmpty(updateInfo.URL_CORE))
            {
                return updateInfo.URL_CORE;
            }
            else if (packageType == UpdateInfo.PackageType.Patch2Only && !string.IsNullOrEmpty(updateInfo.URL_PATCH2))
            {
                return updateInfo.URL_PATCH2;
            }
            else if (packageType == UpdateInfo.PackageType.Full && !string.IsNullOrEmpty(updateInfo.URL_FULL))
            {
                return updateInfo.URL_FULL;
            }
            else
            {
                // Fall back to full package if specific package not available
                packageType = UpdateInfo.PackageType.Full;
                return updateInfo.URL_FULL;
            }
        }

        /// <summary>
        /// Extracts version from URL filename
        /// For FULL packages: versionIndex=0 returns core version, versionIndex=1 returns patch2 version
        /// For single packages: returns the version regardless of index
        /// </summary>
        private static string ExtractVersionFromUrl(string url, int versionIndex)
        {
            if (string.IsNullOrEmpty(url))
                return "00000000.00";

            // Try FULL package format: FEBuilderGBA_FULL_20260226.00_20260226.00.7z
            Match fullMatch = RegexCache.Match(url, @"FEBuilderGBA_FULL_(\d{8}\.\d{2})_(\d{8}\.\d{2})\.7z");
            if (fullMatch.Success && fullMatch.Groups.Count >= 3)
            {
                if (versionIndex == 0)
                    return fullMatch.Groups[1].Value; // Core version
                else
                    return fullMatch.Groups[2].Value; // Patch2 version
            }

            // Try CORE package format: FEBuilderGBA_CORE_20260226.00.7z
            Match coreMatch = RegexCache.Match(url, @"FEBuilderGBA_CORE_(\d{8}\.\d{2})\.7z");
            if (coreMatch.Success && coreMatch.Groups.Count >= 2)
            {
                return coreMatch.Groups[1].Value;
            }

            // Try PATCH2 package format: FEBuilderGBA_PATCH2_20260226.00.7z
            Match patch2Match = RegexCache.Match(url, @"FEBuilderGBA_PATCH2_(\d{8}\.\d{2})\.7z");
            if (patch2Match.Success && patch2Match.Groups.Count >= 2)
            {
                return patch2Match.Groups[1].Value;
            }

            // Try legacy format: FEBuilderGBA_20260226.00.7z
            Match legacyMatch = RegexCache.Match(url, @"FEBuilderGBA_(\d{8}\.\d{2})\.7z");
            if (legacyMatch.Success && legacyMatch.Groups.Count >= 2)
            {
                return legacyMatch.Groups[1].Value;
            }

            return "00000000.00";
        }
    }
}
