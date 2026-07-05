using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FEBuilderGBA
{
    public sealed class ContentRepoDescriptor
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string ConfigKey { get; init; } = "";
        public string DefaultUrl { get; init; } = "";
    }

    /// <summary>
    /// Pure first-run content repository setup policy (#1814). This class intentionally has no UI
    /// dependencies: both WinForms and Avalonia use it to decide whether patch2 / FE-Repo /
    /// FE-Repo-Music need user setup, and tests cover the empty-submodule regression directly.
    /// </summary>
    public static class ContentRepoSetupCore
    {
        public const string OptOutConfigKey = "content_repo_setup_optout";

        static readonly IReadOnlyList<ContentRepoDescriptor> _repos = new[]
        {
            new ContentRepoDescriptor
            {
                Id = "patch2",
                DisplayName = "patch2",
                ConfigKey = "submodule_patch2_url",
                DefaultUrl = GitUtil.Patch2RemoteUrl,
            },
            new ContentRepoDescriptor
            {
                Id = "fe-repo",
                DisplayName = "FE-Repo",
                ConfigKey = "submodule_fe_repo_url",
                DefaultUrl = GitUtil.FERepoDefaultUrl,
            },
            new ContentRepoDescriptor
            {
                Id = "fe-repo-music",
                DisplayName = "FE-Repo-Music",
                ConfigKey = "submodule_fe_repo_music_url",
                DefaultUrl = GitUtil.FERepoMusicDefaultUrl,
            },
        };

        public static IReadOnlyList<ContentRepoDescriptor> Repos => _repos;

        public static string ResolveUrl(ContentRepoDescriptor d, Config cfg)
        {
            if (d == null) return "";
            string configured = cfg?.at(d.ConfigKey, d.DefaultUrl) ?? d.DefaultUrl;
            return string.IsNullOrWhiteSpace(configured) ? d.DefaultUrl : configured;
        }

        public static string ResolveDir(ContentRepoDescriptor d, string baseDir)
        {
            if (d == null) return baseDir ?? "";
            switch (d.Id)
            {
                case "patch2":
                    return Patch2GitService.GetPatch2Dir(baseDir);
                case "fe-repo":
                    return GitUtil.GetFERepoDir(baseDir);
                case "fe-repo-music":
                    return GitUtil.GetFERepoMusicDir(baseDir);
                default:
                    return Path.Combine(baseDir ?? "", d.Id ?? "");
            }
        }

        public static bool IsRepoReady(ContentRepoDescriptor d, string baseDir)
        {
            try
            {
                string dir = ResolveDir(d, baseDir);
                if (d?.Id == "patch2")
                    return !PatchMetadataCore.IsPatchLibraryEmpty(dir);

                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return false;

                return Directory.EnumerateFileSystemEntries(dir).Any();
            }
            catch
            {
                return false;
            }
        }

        public static bool NeedsSetup(string baseDir, Config cfg)
            => Repos.Any(d => !IsRepoReady(d, baseDir));

        public static bool ShouldAutoShow(string baseDir, Config cfg)
            => NeedsSetup(baseDir, cfg) && (cfg?.at(OptOutConfigKey, "0") ?? "0") != "1";

        public static void SetOptOut(Config cfg)
        {
            if (cfg == null) return;
            cfg[OptOutConfigKey] = "1";
            cfg.Save();
        }

        public static bool IsGitAvailable()
            => GitUtil.FindGitExecutable() != null;
    }
}
