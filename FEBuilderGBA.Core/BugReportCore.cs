using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform helpers for building pre-filled GitHub bug-report URLs.
    /// No WinForms or Avalonia dependencies — safe to call from any project.
    /// </summary>
    public static class BugReportCore
    {
        public const string Owner = "laqieer";
        public const string Repo = "FEBuilderGBA";
        public const string GuiBugTemplate = "gui_bug.yml";

        /// <summary>
        /// Builds a GitHub new-issue URL pre-filled with the given fields. Empty/null values are
        /// skipped. Query-parameter order follows the dictionary's enumeration and is unspecified —
        /// it is not significant for GitHub's issue-form prefill.
        /// </summary>
        public static string BuildIssueUrl(
            string owner,
            string repo,
            string template,
            IReadOnlyDictionary<string, string> fields)
        {
            var sb = new StringBuilder();
            sb.Append("https://github.com/");
            sb.Append(Uri.EscapeDataString(owner));
            sb.Append('/');
            sb.Append(Uri.EscapeDataString(repo));
            sb.Append("/issues/new?template=");
            sb.Append(Uri.EscapeDataString(template));
            foreach (var kv in fields)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    sb.Append('&');
                    sb.Append(Uri.EscapeDataString(kv.Key));
                    sb.Append('=');
                    sb.Append(Uri.EscapeDataString(kv.Value));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns a platform label exactly matching the gui_bug.yml platform dropdown options.
        /// </summary>
        /// <remarks>
        /// Windows uses <see cref="RuntimeInformation.ProcessArchitecture"/> (not OSArchitecture)
        /// so the 32-bit WinForms build is correctly tagged "Windows x86 (WinForms)" even when it
        /// runs as a 32-bit process on a 64-bit OS (the common case). The Avalonia build ships only
        /// as x64, so it falls into "Windows x64".
        /// </remarks>
        public static string DetectPlatformLabel()
        {
            if (OperatingSystem.IsBrowser()) return "Web (WebAssembly)";
            if (OperatingSystem.IsAndroid()) return "Android";
            if (OperatingSystem.IsIOS()) return "iOS / iPadOS";
            var arch = RuntimeInformation.ProcessArchitecture;
            if (OperatingSystem.IsWindows())
                return arch switch
                {
                    Architecture.X86 => "Windows x86 (WinForms)",
                    Architecture.X64 => "Windows x64",
                    _ => "Other"
                };
            if (OperatingSystem.IsLinux())
                return arch == Architecture.X64 ? "Linux x64" : "Other";
            if (OperatingSystem.IsMacOS())
                return arch switch
                {
                    Architecture.Arm64 => "macOS Apple Silicon (arm64)",
                    Architecture.X64 => "macOS Intel (x64)",
                    _ => "Other"
                };
            return "Other";
        }

        /// <summary>
        /// Maps a ROM VersionToFilename tag to the label used in the gui_bug.yml rom dropdown.
        /// </summary>
        public static string RomTagToLabel(string? versionToFilename) =>
            versionToFilename switch
            {
                "FE6"  => "FE6 (JP)",
                "FE7J" => "FE7 (JP)",
                "FE7U" => "FE7 (US)",
                "FE8J" => "FE8 (JP)",
                "FE8U" => "FE8 (US)",
                _      => "Other / not sure"
            };

        /// <summary>
        /// Normalizes the app version string to ensure it starts with "ver_".
        /// </summary>
        public static string NormalizeVersion(string? appVersion)
        {
            if (string.IsNullOrEmpty(appVersion)) return "";
            return appVersion.StartsWith("ver_", StringComparison.Ordinal)
                ? appVersion
                : "ver_" + appVersion;
        }

        /// <summary>
        /// Release-tag pattern the app is versioned by: <c>ver_YYYYMMDD.NN</c>.
        /// </summary>
        static readonly Regex ReleaseTagPattern = new(@"^ver_\d{8}\.\d+$", RegexOptions.Compiled);

        /// <summary>
        /// Picks the version string to show in a bug report. When the entry assembly's
        /// <c>AssemblyInformationalVersion</c> carries the exact release tag the user
        /// knows (<c>ver_YYYYMMDD.NN</c>, as stamped by <c>release.yml</c>), that is
        /// returned; otherwise it falls back to <paramref name="fallback"/> (the
        /// per-build <see cref="FEBuilderGBA.U.getVersion"/> value used for local/dev
        /// builds). Any SemVer build-metadata suffix (e.g. <c>+&lt;sha&gt;</c> added by
        /// SourceLink) is stripped before matching, so a future SourceLink change can't
        /// silently revert this to the wrong computed version.
        /// </summary>
        public static string SelectVersion(string? informationalVersion, string fallback)
        {
            if (!string.IsNullOrEmpty(informationalVersion))
            {
                // Strip SemVer build metadata: "ver_20260629.04+9df0c7b" -> "ver_20260629.04".
                string clean = informationalVersion.Split('+')[0].Trim();
                if (ReleaseTagPattern.IsMatch(clean))
                {
                    return clean;
                }
            }
            return fallback;
        }

        /// <summary>
        /// Builds a prefill dictionary for BuildIssueUrl. Keys match the gui_bug.yml field ids:
        /// version, rom, editor, platform, app. <c>rom</c> and <c>platform</c> are always present
        /// (they fall back to "Other / not sure" / "Other"); <c>version</c>, <c>editor</c>, and
        /// <c>app</c> are omitted when null/empty. screenshot/report7z are NOT prefilled.
        /// </summary>
        public static Dictionary<string, string> BuildPrefill(
            string? appVersion,
            string? romVersionToFilename,
            string? editorTitle,
            string? appLabel)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var normalizedVersion = NormalizeVersion(appVersion);
            if (!string.IsNullOrEmpty(normalizedVersion)) d["version"] = normalizedVersion;
            var romLabel = RomTagToLabel(romVersionToFilename);
            d["rom"] = romLabel;
            if (!string.IsNullOrEmpty(editorTitle)) d["editor"]   = editorTitle!;
            d["platform"] = DetectPlatformLabel();
            if (!string.IsNullOrEmpty(appLabel))    d["app"]      = appLabel!;
            return d;
        }
    }
}
