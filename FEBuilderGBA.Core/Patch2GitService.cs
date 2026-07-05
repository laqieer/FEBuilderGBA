using System;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>Outcome of a content-repo initialize/update call (shared result type; patch2-named for #1812 history).</summary>
    public enum Patch2GitResultKind
    {
        Success,
        GitNotFound,
        Failed,
        AlreadyRunning,
    }

    /// <summary>Result of an in-app content-repo initialize/update operation (shared across patch2 / FE-Repo / FE-Repo-Music).</summary>
    public sealed class Patch2GitResult
    {
        public Patch2GitResultKind Kind { get; init; }
        public int ExitCode { get; init; }
        public string Log { get; init; } = "";
        /// <summary>True when the operation took the clone path (fresh init), false for update.</summary>
        public bool WasClone { get; init; }
        public bool Success => Kind == Patch2GitResultKind.Success;
    }

    /// <summary>
    /// Thin patch2-specific facade over the generic <see cref="ContentRepoGitService"/> (#1813). Keeps
    /// the patch2 public surface (<see cref="GetPatch2Dir"/>, <see cref="InitializeOrUpdate"/>,
    /// <see cref="Patch2GitResult"/>) unchanged for the merged #1812/#1817 callers and the 12
    /// <c>Patch2GitServiceTests</c>, resolving the patch2 directory + remote URL and delegating the
    /// actual clone-or-update (and the shared single-flight guard) to <see cref="ContentRepoGitService"/>.
    /// </summary>
    public static class Patch2GitService
    {
        /// <summary>Repo-root patch database directory: <c>&lt;baseDir&gt;/config/patch2</c>.</summary>
        public static string GetPatch2Dir(string baseDir)
            => System.IO.Path.Combine(baseDir ?? "", "config", "patch2");

        /// <summary>Delegate matching <see cref="GitUtil.Clone"/> so tests can inject a fake.</summary>
        public delegate int CloneOp(string gitExe, string url, string targetPath, Action<string> progress, StringBuilder log);

        /// <summary>Delegate matching <see cref="GitUtil.Update"/> so tests can inject a fake.</summary>
        public delegate int UpdateOp(string gitExe, string repoPath, Action<string> progress, StringBuilder log, string remoteUrl);

        /// <summary>
        /// Resolves the git executable and patch2 remote URL (custom fork override or default), then
        /// delegates to <see cref="ContentRepoGitService.InitializeOrUpdate"/> against
        /// <c>&lt;baseDir&gt;/config/patch2</c>. Guarded by the shared single-flight lock.
        /// </summary>
        public static Patch2GitResult InitializeOrUpdate(string baseDir, Action<string> progress = null, string urlOverride = null)
        {
            string url = string.IsNullOrWhiteSpace(urlOverride) ? GitUtil.GetPatch2RemoteUrl() : urlOverride;
            return ContentRepoGitService.InitializeOrUpdate(GetPatch2Dir(baseDir), url, progress);
        }

        /// <summary>
        /// Patch2 shim over the generic core (keeps the merged <c>Patch2GitServiceTests</c> unchanged):
        /// maps <paramref name="baseDir"/> → <c>config/patch2</c> and forwards to
        /// <see cref="ContentRepoGitService.InitializeOrUpdateCore"/>. Unguarded (like the generic core).
        /// </summary>
        internal static Patch2GitResult InitializeOrUpdateCore(
            string baseDir, string gitExe, string url,
            Func<string, bool> isGitRepo, CloneOp cloneOp, UpdateOp updateOp,
            Action<string> progress)
            => ContentRepoGitService.InitializeOrUpdateCore(GetPatch2Dir(baseDir), gitExe, url, isGitRepo, cloneOp, updateOp, progress);

        /// <summary>Acquire the shared single-flight guard (pass-through to <see cref="ContentRepoGitService"/>).</summary>
        internal static bool TryEnter() => ContentRepoGitService.TryEnter();

        /// <summary>Release the shared single-flight guard (pass-through to <see cref="ContentRepoGitService"/>).</summary>
        internal static void Exit() => ContentRepoGitService.Exit();
    }
}
