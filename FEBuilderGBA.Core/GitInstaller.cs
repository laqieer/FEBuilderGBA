using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FEBuilderGBA
{
    /// <summary>
    /// Helpers for automatically downloading and installing Git for Windows.
    /// </summary>
    public static class GitInstaller
    {
        private const string GitReleasesApiUrl =
            "https://api.github.com/repos/git-for-windows/git/releases/latest";

        /// <summary>
        /// Queries the GitHub releases API for the latest Git for Windows installer URL.
        /// Targets the 64-bit installer on a 64-bit OS, 32-bit otherwise.
        /// Returns null on any failure (network error, parse error, etc.).
        /// </summary>
        public static string GetLatestInstallerUrl()
        {
            string suffix = Environment.Is64BitOperatingSystem ? "64-bit.exe" : "32-bit.exe";
            try
            {
                string json = U.HttpGet(GitReleasesApiUrl,
                    referer: "https://github.com/git-for-windows/git/releases");
                return string.IsNullOrEmpty(json) ? null : ExtractDownloadUrl(json, suffix);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Runs the installer at installerPath with standard NSIS silent flags.
        /// UseShellExecute=true so Windows handles UAC elevation automatically.
        /// Returns true when the installer exits with code 0.
        /// </summary>
        public static Task<bool> RunInstallerSilentlyAsync(string installerPath)
        {
            return Task.Run(() =>
            {
                try
                {
                    var p = new Process();
                    p.StartInfo.FileName        = installerPath;
                    p.StartInfo.Arguments       = "/VERYSILENT /NORESTART /NOCANCEL /SP- /SUPPRESSMSGBOXES";
                    p.StartInfo.UseShellExecute = true; // triggers UAC prompt if elevation is required
                    p.Start();
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        // Searches the GitHub releases JSON for the first browser_download_url
        // whose value ends with urlSuffix (e.g. "64-bit.exe").
        private static string ExtractDownloadUrl(string json, string urlSuffix)
        {
            const string marker = "\"browser_download_url\":";
            int idx = 0;
            while (true)
            {
                int pos = json.IndexOf(marker, idx, StringComparison.Ordinal);
                if (pos < 0) return null;

                int urlStart = json.IndexOf('"', pos + marker.Length);
                if (urlStart < 0) return null;
                urlStart++;                         // skip the opening quote

                int urlEnd = json.IndexOf('"', urlStart);
                if (urlEnd < 0) return null;

                string url = json.Substring(urlStart, urlEnd - urlStart);
                if (url.EndsWith(urlSuffix, StringComparison.OrdinalIgnoreCase))
                    return url;

                idx = urlEnd + 1;
            }
        }
    }
}
