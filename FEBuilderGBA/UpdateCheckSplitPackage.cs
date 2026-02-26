using System;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Extensions to UpdateCheck for split package support.
    /// Handles detection and downloading of Core/Patch2 packages separately.
    /// </summary>
    public static class UpdateCheckSplitPackage
    {
        // ---------------------------------------------------------------------------
        // nightly.link (GitHub Actions artifacts, .zip)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Checks for split package updates via nightly.link (GitHub Actions artifacts).
        /// Looks for FEBuilderGBA_CORE_*.zip, FEBuilderGBA_PATCH2_*.zip,
        /// and FEBuilderGBA_*.zip (full) on the nightly.link page.
        /// </summary>
        public static string CheckSplitPackageUpdateByNightlyLink(out UpdateInfo updateInfo)
        {
            updateInfo = new UpdateInfo();

            string baseUrl = "https://nightly.link/laqieer/FEBuilderGBA/workflows/msbuild/master";
            string contents;
            try
            {
                contents = U.HttpGet(baseUrl);
            }
            catch (Exception e)
            {
#if DEBUG
                R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", baseUrl, e.ToString());
                throw;
#else
                return R.Error("Webサイトにアクセスできません。 URL:{0} Message:{1}", baseUrl, e.ToString());
#endif
            }

            // nightly.link lists artifact download links literally in the HTML, e.g.:
            //   https://nightly.link/.../FEBuilderGBA_20260226.11.zip
            //   https://nightly.link/.../FEBuilderGBA_CORE_20260226.11.zip
            //   https://nightly.link/.../FEBuilderGBA_PATCH2_20260226.11.zip
            string escapedBase  = Regex.Escape(baseUrl);
            string fullPattern   = escapedBase + @"/FEBuilderGBA_(\d{8}\.\d{2})\.zip";
            string corePattern   = escapedBase + @"/FEBuilderGBA_CORE_(\d{8}\.\d{2})\.zip";
            string patch2Pattern = escapedBase + @"/FEBuilderGBA_PATCH2_(\d{8}\.\d{2})\.zip";

            Match fullMatch   = RegexCache.Match(contents, fullPattern);
            Match coreMatch   = RegexCache.Match(contents, corePattern);
            Match patch2Match = RegexCache.Match(contents, patch2Pattern);

            bool hasSplitPackages = coreMatch.Success || patch2Match.Success;
            if (!hasSplitPackages)
            {
                return R._("サイトの結果が期待外でした。\r\n{0}", baseUrl) + "\r\n\r\n"
                    + "split packages (CORE / PATCH2) not found on nightly.link";
            }

            string remoteCoreVersion   = "00000000.00";
            string remotePatch2Version = "00000000.00";

            if (fullMatch.Success)
            {
                string ver = fullMatch.Groups[1].Value;
                updateInfo.URL_FULL    = baseUrl + "/FEBuilderGBA_" + ver + ".zip";
                remoteCoreVersion      = ver;
                remotePatch2Version    = ver;
            }

            if (coreMatch.Success)
            {
                string ver = coreMatch.Groups[1].Value;
                updateInfo.URL_CORE = baseUrl + "/FEBuilderGBA_CORE_" + ver + ".zip";
                if (UpdateInfo.CompareVersions(ver, remoteCoreVersion) > 0)
                    remoteCoreVersion = ver;
            }

            if (patch2Match.Success)
            {
                string ver = patch2Match.Groups[1].Value;
                updateInfo.URL_PATCH2 = baseUrl + "/FEBuilderGBA_PATCH2_" + ver + ".zip";
                if (UpdateInfo.CompareVersions(ver, remotePatch2Version) > 0)
                    remotePatch2Version = ver;
            }

            UpdateInfo.PackageType packageType = updateInfo.DetermineUpdateType(remoteCoreVersion, remotePatch2Version);

            if (packageType == UpdateInfo.PackageType.None)
                return R._("現在のバージョンが最新です。version:{0}", updateInfo.GetVersionDisplay());

            if (!updateInfo.HasUrl(packageType))
                return R._("必要なパッケージが見つかりません。PackageType:{0}", packageType.ToString());

            return ""; // success
        }

        // ---------------------------------------------------------------------------
        // GitHub Releases (.7z or .zip release assets)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Checks for split package updates in the latest GitHub Release.
        /// Looks for FEBuilderGBA_FULL_*, FEBuilderGBA_CORE_*, FEBuilderGBA_PATCH2_*
        /// release assets (supports both .7z and .zip).
        /// Falls back to legacy single-package format if not found.
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

            // Match .7z or .zip release assets
            string fullPattern   = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_FULL_(\d{8}\.\d{2})_(\d{8}\.\d{2})\.(7z|zip))""";
            string corePattern   = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_CORE_(\d{8}\.\d{2})\.(7z|zip))""";
            string patch2Pattern = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_PATCH2_(\d{8}\.\d{2})\.(7z|zip))""";

            Match fullMatch   = RegexCache.Match(contents, fullPattern);
            Match coreMatch   = RegexCache.Match(contents, corePattern);
            Match patch2Match = RegexCache.Match(contents, patch2Pattern);

            bool hasSplitPackages = coreMatch.Success || patch2Match.Success;

            if (hasSplitPackages)
            {
                string remoteCoreVersion   = "00000000.00";
                string remotePatch2Version = "00000000.00";

                if (fullMatch.Success && fullMatch.Groups.Count >= 4)
                {
                    updateInfo.URL_FULL   = fullMatch.Groups[1].Value;
                    remoteCoreVersion     = fullMatch.Groups[2].Value;
                    remotePatch2Version   = fullMatch.Groups[3].Value;
                }

                if (coreMatch.Success && coreMatch.Groups.Count >= 3)
                {
                    updateInfo.URL_CORE = coreMatch.Groups[1].Value;
                    string coreVer      = coreMatch.Groups[2].Value;
                    if (UpdateInfo.CompareVersions(coreVer, remoteCoreVersion) > 0)
                        remoteCoreVersion = coreVer;
                }

                if (patch2Match.Success && patch2Match.Groups.Count >= 3)
                {
                    updateInfo.URL_PATCH2 = patch2Match.Groups[1].Value;
                    string patch2Ver      = patch2Match.Groups[2].Value;
                    if (UpdateInfo.CompareVersions(patch2Ver, remotePatch2Version) > 0)
                        remotePatch2Version = patch2Ver;
                }

                UpdateInfo.PackageType packageType = updateInfo.DetermineUpdateType(remoteCoreVersion, remotePatch2Version);

                if (packageType == UpdateInfo.PackageType.None)
                    return R._("現在のバージョンが最新です。version:{0}", updateInfo.GetVersionDisplay());

                if (!updateInfo.HasUrl(packageType))
                    return R._("必要なパッケージが見つかりません。PackageType:{0}", packageType.ToString());

                return ""; // success
            }
            else
            {
                // Fall back to legacy single-package format (.7z or .zip)
                string legacyPattern = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_(\d{8}\.\d{2})\.(7z|zip))""";
                Match legacyMatch    = RegexCache.Match(contents, legacyPattern);

                if (legacyMatch.Success && legacyMatch.Groups.Count >= 3)
                {
                    updateInfo.URL_FULL = legacyMatch.Groups[1].Value;
                    string version      = legacyMatch.Groups[2].Value;

                    if (UpdateInfo.CompareVersions(updateInfo.VERSION_CORE, version) >= 0)
                        return R._("現在のバージョンが最新です。version:{0}", updateInfo.VERSION_CORE);

                    return ""; // success
                }
                else
                {
                    return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                        + "browser_download_url not found" + "\r\n"
                        + "contents:\r\n" + contents;
                }
            }
        }

        // ---------------------------------------------------------------------------
        // Shared helpers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Gets the appropriate download URL based on what needs updating.
        /// Returns the most efficient package choice.
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

            string remoteCoreVer   = ExtractVersionFromUrl(updateInfo.URL_CORE   ?? updateInfo.URL_FULL, 0);
            string remotePatch2Ver = ExtractVersionFromUrl(updateInfo.URL_PATCH2 ?? updateInfo.URL_FULL, 1);

            packageType = updateInfo.DetermineUpdateType(remoteCoreVer, remotePatch2Ver);

            if (packageType == UpdateInfo.PackageType.CoreOnly && !string.IsNullOrEmpty(updateInfo.URL_CORE))
                return updateInfo.URL_CORE;
            else if (packageType == UpdateInfo.PackageType.Patch2Only && !string.IsNullOrEmpty(updateInfo.URL_PATCH2))
                return updateInfo.URL_PATCH2;
            else if (packageType == UpdateInfo.PackageType.Full && !string.IsNullOrEmpty(updateInfo.URL_FULL))
                return updateInfo.URL_FULL;
            else
            {
                packageType = UpdateInfo.PackageType.Full;
                return updateInfo.URL_FULL;
            }
        }

        /// <summary>
        /// Extracts version from URL filename (handles both .7z and .zip).
        /// For FULL packages: versionIndex=0 → core version, versionIndex=1 → patch2 version.
        /// For single packages: returns the version regardless of index.
        /// </summary>
        public static string ExtractVersionFromUrl(string url, int versionIndex)
        {
            if (string.IsNullOrEmpty(url))
                return "00000000.00";

            // FULL: FEBuilderGBA_FULL_20260226.00_20260226.00.(7z|zip)
            Match fullMatch = RegexCache.Match(url, @"FEBuilderGBA_FULL_(\d{8}\.\d{2})_(\d{8}\.\d{2})\.(7z|zip)");
            if (fullMatch.Success && fullMatch.Groups.Count >= 3)
                return versionIndex == 0 ? fullMatch.Groups[1].Value : fullMatch.Groups[2].Value;

            // CORE: FEBuilderGBA_CORE_20260226.00.(7z|zip)
            Match coreMatch = RegexCache.Match(url, @"FEBuilderGBA_CORE_(\d{8}\.\d{2})\.(7z|zip)");
            if (coreMatch.Success && coreMatch.Groups.Count >= 2)
                return coreMatch.Groups[1].Value;

            // PATCH2: FEBuilderGBA_PATCH2_20260226.00.(7z|zip)
            Match patch2Match = RegexCache.Match(url, @"FEBuilderGBA_PATCH2_(\d{8}\.\d{2})\.(7z|zip)");
            if (patch2Match.Success && patch2Match.Groups.Count >= 2)
                return patch2Match.Groups[1].Value;

            // Legacy: FEBuilderGBA_20260226.00.(7z|zip)
            Match legacyMatch = RegexCache.Match(url, @"FEBuilderGBA_(\d{8}\.\d{2})\.(7z|zip)");
            if (legacyMatch.Success && legacyMatch.Groups.Count >= 2)
                return legacyMatch.Groups[1].Value;

            return "00000000.00";
        }
    }
}
