using System;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>Outcome of a <see cref="Patch2GitService.InitializeOrUpdate"/> call.</summary>
    public enum Patch2GitResultKind
    {
        Success,
        GitNotFound,
        Failed,
        AlreadyRunning,
    }

    /// <summary>Result of an in-app patch2 initialize/update operation.</summary>
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
    /// Cross-platform, in-app initialize/update of the git-delivered <c>config/patch2</c> database.
    ///
    /// Lifts the clone-or-update-with-backup decision (previously inline and WinForms-only in
    /// <c>ToolUpdateDialogForm.AutoUpdatePatch2Git</c>) into a reusable, unit-testable Core service so
    /// the Avalonia GUI (#1817) can offer real in-app Initialize/Update — the Avalonia half of #1812 —
    /// without duplicating the logic. The proven WinForms flow is mirrored exactly: if the directory is
    /// already a git repo → <c>git fetch + reset</c>; otherwise move any existing directory aside
    /// (including an empty submodule placeholder from a non-recursive clone) and <c>git clone</c>,
    /// restoring the backup on failure.
    /// </summary>
    public static class Patch2GitService
    {
        // Single-flight guard (#1817 review board): Options and the Patch Manager can both trigger this,
        // and they are independently openable windows — the same config/patch2 directory must never be
        // backed-up/cloned by two threads at once. A second concurrent call returns AlreadyRunning.
        static readonly object _gate = new object();
        static bool _running;

        /// <summary>Repo-root patch database directory: <c>&lt;baseDir&gt;/config/patch2</c>.</summary>
        public static string GetPatch2Dir(string baseDir)
            => Path.Combine(baseDir ?? "", "config", "patch2");

        /// <summary>Delegate matching <see cref="GitUtil.Clone"/> so tests can inject a fake.</summary>
        public delegate int CloneOp(string gitExe, string url, string targetPath, Action<string> progress, StringBuilder log);

        /// <summary>Delegate matching <see cref="GitUtil.Update"/> so tests can inject a fake.</summary>
        public delegate int UpdateOp(string gitExe, string repoPath, Action<string> progress, StringBuilder log, string remoteUrl);

        /// <summary>
        /// Public entry: resolves the git executable and remote URL from the environment/config, then
        /// runs the clone-or-update. Synchronous — callers wrap it in <c>Task.Run</c>. <paramref name="progress"/>
        /// (nullable) receives git output lines on a background thread. <paramref name="urlOverride"/>
        /// (nullable) forces a specific remote (used by the Options "Initialize / Update now" button so a
        /// just-typed custom fork URL takes effect for both the clone and the update remote).
        /// Guarded by a single-flight lock: a concurrent second call returns <see cref="Patch2GitResultKind.AlreadyRunning"/>.
        /// </summary>
        public static Patch2GitResult InitializeOrUpdate(string baseDir, Action<string> progress = null, string urlOverride = null)
        {
            if (!TryEnter())
                return new Patch2GitResult { Kind = Patch2GitResultKind.AlreadyRunning };
            try
            {
                string gitExe = GitUtil.FindGitExecutable();
                string url = string.IsNullOrWhiteSpace(urlOverride) ? GitUtil.GetPatch2RemoteUrl() : urlOverride;
                return InitializeOrUpdateCore(baseDir, gitExe, url, GitUtil.IsGitRepo, GitUtil.Clone, GitUtil.Update, progress);
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Testable core with all external dependencies injected — exercises the REAL backup file-move
        /// logic so tests need no network or git. Pure (no static/guard state) so parallel tests using
        /// isolated temp directories never race. <paramref name="gitExe"/> null/empty → GitNotFound.
        /// </summary>
        internal static Patch2GitResult InitializeOrUpdateCore(
            string baseDir, string gitExe, string url,
            Func<string, bool> isGitRepo, CloneOp cloneOp, UpdateOp updateOp,
            Action<string> progress)
        {
            if (string.IsNullOrEmpty(gitExe))
                return new Patch2GitResult { Kind = Patch2GitResultKind.GitNotFound };

            string patchDir = GetPatch2Dir(baseDir);
            var log = new StringBuilder();

            // UPDATE path — the directory is already a real git repo.
            if (isGitRepo(patchDir))
            {
                int code = updateOp(gitExe, patchDir, progress, log, url);
                return new Patch2GitResult
                {
                    Kind = code == 0 ? Patch2GitResultKind.Success : Patch2GitResultKind.Failed,
                    ExitCode = code,
                    Log = log.ToString(),
                    WasClone = false,
                };
            }

            // CLONE path. Move ANY existing directory aside first — including an empty submodule
            // placeholder left by a non-recursive superproject clone (the single most common "needs
            // Initialize" state) — so the clone target does not exist and we can roll back cleanly if
            // the clone fails partway (otherwise partial .git debris would flip the next run onto the
            // wrong IsGitRepo->Update branch).
            string backupPath = null;
            if (Directory.Exists(patchDir))
            {
                backupPath = Path.Combine(
                    Path.GetDirectoryName(patchDir) ?? "",
                    "_patch2_backup_" + DateTime.Now.Ticks.ToString());

                // Guard against a stale backup directory from a previously aborted run so
                // Directory.Move never throws on an existing destination.
                if (Directory.Exists(backupPath))
                    Directory.Delete(backupPath, true);

                Directory.Move(patchDir, backupPath);
            }

            int cloneCode = cloneOp(gitExe, url, patchDir, progress, log);
            if (cloneCode != 0)
            {
                // Restore the backup on failure — best-effort, leaving the tree as we found it.
                try
                {
                    if (Directory.Exists(patchDir))
                        Directory.Delete(patchDir, true);
                    if (backupPath != null && Directory.Exists(backupPath))
                        Directory.Move(backupPath, patchDir);
                }
                catch
                {
                    // Restore is best-effort; the accumulated log still explains the clone failure.
                }

                return new Patch2GitResult
                {
                    Kind = Patch2GitResultKind.Failed,
                    ExitCode = cloneCode,
                    Log = log.ToString(),
                    WasClone = true,
                };
            }

            // Success — drop the backup (a leftover backup is harmless but wastes disk).
            if (backupPath != null)
            {
                try
                {
                    if (Directory.Exists(backupPath))
                        Directory.Delete(backupPath, true);
                }
                catch
                {
                    // A leftover backup directory is non-fatal.
                }
            }

            return new Patch2GitResult
            {
                Kind = Patch2GitResultKind.Success,
                ExitCode = 0,
                Log = log.ToString(),
                WasClone = true,
            };
        }

        /// <summary>Acquire the single-flight guard. Internal for deterministic re-entrancy tests.</summary>
        internal static bool TryEnter()
        {
            lock (_gate)
            {
                if (_running) return false;
                _running = true;
                return true;
            }
        }

        /// <summary>Release the single-flight guard.</summary>
        internal static void Exit()
        {
            lock (_gate) { _running = false; }
        }
    }
}
