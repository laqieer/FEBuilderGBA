using System;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Checks for core application updates via nightly.link or GitHub Releases.
    /// Patch2 data is updated independently via git (see GitUtil / ToolUpdateDialogForm).
    /// </summary>
    public static class UpdateCheckSplitPackage
    {
        // ---------------------------------------------------------------------------
        // nightly.link (GitHub Actions artifacts, .zip)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Checks for a core update via nightly.link (GitHub Actions artifacts).
        /// Looks for FEBuilderGBA_*.zip on the nightly.link page.
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

            string escapedBase = Regex.Escape(baseUrl);
            string corePattern = escapedBase + @"/FEBuilderGBA_(\d{8}\.\d{2})\.zip";

            Match coreMatch = RegexCache.Match(contents, corePattern);

            if (!coreMatch.Success)
            {
                return R._("サイトの結果が期待外でした。\r\n{0}", baseUrl) + "\r\n\r\n"
                    + "Package not found on nightly.link";
            }

            string remoteCoreVersion = coreMatch.Groups[1].Value;
            updateInfo.URL_CORE = baseUrl + "/FEBuilderGBA_" + remoteCoreVersion + ".zip";

            UpdateInfo.PackageType packageType = updateInfo.DetermineUpdateType(remoteCoreVersion);

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
        /// Checks for a core update in the latest GitHub Release.
        /// Looks for FEBuilderGBA_* release assets (.7z or .zip).
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

            string corePattern = @"""browser_download_url"":\s*""([^""]+/FEBuilderGBA_(\d{8}\.\d{2})\.(7z|zip))""";
            Match coreMatch = RegexCache.Match(contents, corePattern);

            if (coreMatch.Success && coreMatch.Groups.Count >= 3)
            {
                updateInfo.URL_CORE      = coreMatch.Groups[1].Value;
                string remoteCoreVersion = coreMatch.Groups[2].Value;

                UpdateInfo.PackageType packageType = updateInfo.DetermineUpdateType(remoteCoreVersion);

                if (packageType == UpdateInfo.PackageType.None)
                    return R._("現在のバージョンが最新です。version:{0}", updateInfo.GetVersionDisplay());

                if (!updateInfo.HasUrl(packageType))
                    return R._("必要なパッケージが見つかりません。PackageType:{0}", packageType.ToString());

                return ""; // success
            }
            else
            {
                return R._("サイトの結果が期待外でした。\r\n{0}", url) + "\r\n\r\n"
                    + "Package not found in release assets";
            }
        }

        // ---------------------------------------------------------------------------
        // Shared helpers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Gets the core download URL and determines the package type.
        /// </summary>
        public static string GetDownloadUrl(UpdateInfo updateInfo, out UpdateInfo.PackageType packageType)
        {
            string remoteCoreVer = ExtractVersionFromUrl(updateInfo.URL_CORE);
            packageType = updateInfo.DetermineUpdateType(remoteCoreVer);
            return updateInfo.URL_CORE;
        }

        /// <summary>
        /// Extracts the core version from a URL filename (handles both .7z and .zip).
        /// </summary>
        public static string ExtractVersionFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return "00000000.00";

            // FEBuilderGBA_20260226.00.(7z|zip)
            Match coreMatch = RegexCache.Match(url, @"FEBuilderGBA_(\d{8}\.\d{2})\.(7z|zip)");
            if (coreMatch.Success && coreMatch.Groups.Count >= 2)
                return coreMatch.Groups[1].Value;

            return "00000000.00";
        }
    }
}
