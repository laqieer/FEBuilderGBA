using System;
using System.Text.Json;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, never-throwing application update check helpers.
    /// Avalonia uses this only to check availability and open the release page;
    /// the Windows self-replacing updater remains WinForms-only.
    /// </summary>
    public static class UpdateCheckCore
    {
        public const string ReleasesLatestApiUrl = "https://api.github.com/repos/laqieer/FEBuilderGBA/releases/latest";
        public const string ReleasesLatestPageUrl = "https://github.com/laqieer/FEBuilderGBA/releases/latest";

        public sealed class UpdateCheckResult
        {
            public bool CheckSucceeded;
            public bool IsUpdateAvailable;
            public string CurrentVersion = "";
            public string LatestVersion = "";
            public string ReleasePageUrl = "";
            public string Error = "";
        }

        public static string ParseLatestVersionFromReleaseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "";

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return "";
                if (!root.TryGetProperty("tag_name", out JsonElement tag))
                    return "";
                if (tag.ValueKind != JsonValueKind.String)
                    return "";
                return tag.GetString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static UpdateCheckResult BuildResult(string currentVersion, string latestVersionRaw, string releasePageUrl)
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = currentVersion ?? "",
                LatestVersion = latestVersionRaw ?? "",
                ReleasePageUrl = releasePageUrl ?? "",
            };

            if (string.IsNullOrWhiteSpace(latestVersionRaw) || !UpdateInfo.IsValidVersion(latestVersionRaw))
            {
                result.CheckSucceeded = false;
                result.IsUpdateAvailable = false;
                result.Error = "Could not read the latest release version.";
                return result;
            }

            result.CheckSucceeded = true;
            result.IsUpdateAvailable = UpdateInfo.CompareVersions(currentVersion, latestVersionRaw) < 0;
            return result;
        }

        public static UpdateCheckResult CheckLatest(Func<string, string> httpGet = null)
        {
            try
            {
                if (httpGet == null)
                    httpGet = url => U.HttpGet(url);
                string body = httpGet(ReleasesLatestApiUrl);
                if (string.IsNullOrWhiteSpace(body))
                {
                    return new UpdateCheckResult
                    {
                        CheckSucceeded = false,
                        Error = "Could not reach GitHub (offline or rate-limited).",
                        ReleasePageUrl = ReleasesLatestPageUrl,
                        CurrentVersion = U.getAppVersion(),
                    };
                }

                string latest = ParseLatestVersionFromReleaseJson(body);
                return BuildResult(U.getAppVersion(), latest, ReleasesLatestPageUrl);
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    CheckSucceeded = false,
                    Error = ex.Message,
                    ReleasePageUrl = ReleasesLatestPageUrl,
                    CurrentVersion = U.getAppVersion(),
                };
            }
        }

        public static bool ShouldAutoCheck(string funcAutoUpdate, string lastCheckYyyyMmdd, string todayYyyyMmdd)
        {
            if (!int.TryParse(funcAutoUpdate, out int intervalDays) || intervalDays <= 0)
                return false;

            if (!DateTime.TryParseExact(todayYyyyMmdd, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime today))
                today = DateTime.Now.Date;

            string dueDay = today.AddDays(-intervalDays).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            if (!uint.TryParse(dueDay, out uint due))
                return true;
            if (!uint.TryParse(lastCheckYyyyMmdd, out uint last))
                return true;

            return due > last;
        }
    }
}
