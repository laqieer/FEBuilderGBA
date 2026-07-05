using System;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform, in-app initialize (clone) / update (fetch+reset) of a git-delivered content
    /// repository. Generalizes the patch2 workflow (#1812/#1817) so the three git-delivered repos —
    /// <c>config/patch2</c>, <c>resources/FE-Repo</c>, <c>resources/FE-Repo-Music-No-Preview</c> — share
    /// ONE implementation (#1813). The proven flow: if the directory is already a git repo → <c>git
    /// fetch + reset</c>; otherwise move any existing directory aside (including an empty submodule
    /// placeholder) and <c>git clone</c>, restoring the backup on failure.
    ///
    /// <para><see cref="Patch2GitService"/> is a thin patch2-specific facade that delegates here; its
    /// public result types (<see cref="Patch2GitResult"/>/<see cref="Patch2GitResultKind"/>) are the
    /// shared result types returned by this service.</para>
    /// </summary>
    public static class ContentRepoGitService
    {
        // The ONE canonical single-flight guard shared by every content-repo entry point (both GUIs'
        // Options/Patch-Manager buttons AND the legacy WinForms ToolUpdateDialogForm.AutoUpdatePatch2Git,
        // which reaches it via Patch2GitService.TryEnter/Exit pass-throughs). Global (one op at a time)
        // — the ops are rare and foreground, so a concurrent trigger simply returns AlreadyRunning.
        static readonly object _gate = new object();
        static bool _running;

        /// <summary>
        /// Public entry: resolves the git executable, then runs the clone-or-update against
        /// <paramref name="repoDir"/> using remote <paramref name="url"/>. Synchronous — callers wrap it
        /// in <c>Task.Run</c>. <paramref name="progress"/> (nullable) receives git output lines on a
        /// background thread. Guarded by the single-flight lock: a concurrent second call returns
        /// <see cref="Patch2GitResultKind.AlreadyRunning"/>.
        /// </summary>
        public static Patch2GitResult InitializeOrUpdate(string repoDir, string url, Action<string> progress = null)
        {
            if (!TryEnter())
                return new Patch2GitResult { Kind = Patch2GitResultKind.AlreadyRunning };
            try
            {
                string gitExe = GitUtil.FindGitExecutable();
                return InitializeOrUpdateCore(repoDir, gitExe, url, GitUtil.IsGitRepo, GitUtil.Clone, GitUtil.Update, progress);
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
        /// A directory that <paramref name="isGitRepo"/> reports as a repo (incl. a submodule <c>.git</c>
        /// link, however large) goes straight to the update path and is never backed up.
        /// </summary>
        internal static Patch2GitResult InitializeOrUpdateCore(
            string repoDir, string gitExe, string url,
            Func<string, bool> isGitRepo, Patch2GitService.CloneOp cloneOp, Patch2GitService.UpdateOp updateOp,
            Action<string> progress)
        {
            if (string.IsNullOrEmpty(gitExe))
                return new Patch2GitResult { Kind = Patch2GitResultKind.GitNotFound };

            var log = new StringBuilder();

            // UPDATE path — the directory is already a real git repo (or a valid submodule link).
            if (isGitRepo(repoDir))
            {
                int code;
                try
                {
                    code = updateOp(gitExe, repoDir, progress, log, url);
                }
                catch (Exception ex)
                {
                    // Report as Failed (with the exception in the log) rather than letting it escape.
                    // Nothing was moved, so there is no backup to restore here.
                    log.AppendLine(ex.ToString());
                    return new Patch2GitResult
                    {
                        Kind = Patch2GitResultKind.Failed,
                        ExitCode = -1,
                        Log = log.ToString(),
                        WasClone = false,
                    };
                }
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
            // the clone fails partway. The backup is a fast same-volume Directory.Move (rename, not a
            // deep copy), so it is cheap even for a large non-repo directory.
            string backupPath = null;
            if (Directory.Exists(repoDir))
            {
                backupPath = Path.Combine(
                    Path.GetDirectoryName(repoDir) ?? "",
                    "_" + (Path.GetFileName(repoDir) ?? "repo") + "_backup_" + DateTime.Now.Ticks.ToString());

                // Guard against a stale backup directory from a previously aborted run so
                // Directory.Move never throws on an existing destination.
                if (Directory.Exists(backupPath))
                    Directory.Delete(backupPath, true);

                Directory.Move(repoDir, backupPath);
            }

            int cloneCode;
            try
            {
                cloneCode = cloneOp(gitExe, url, repoDir, progress, log);
            }
            catch (Exception ex)
            {
                // The clone op threw AFTER we moved the existing directory aside — restore the backup so
                // the repository is never left stranded under _<name>_backup_*, capture the exception in
                // the log, and report Failed (no exception escapes to strand state).
                log.AppendLine(ex.ToString());
                RestoreBackup(repoDir, backupPath);
                return new Patch2GitResult
                {
                    Kind = Patch2GitResultKind.Failed,
                    ExitCode = -1,
                    Log = log.ToString(),
                    WasClone = true,
                };
            }

            if (cloneCode != 0)
            {
                // Restore the backup on failure — best-effort, leaving the tree as we found it.
                RestoreBackup(repoDir, backupPath);

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

        /// <summary>Best-effort restore of a backed-up repository directory after a failed/throwing clone.</summary>
        static void RestoreBackup(string repoDir, string backupPath)
        {
            try
            {
                if (Directory.Exists(repoDir))
                    Directory.Delete(repoDir, true);
                if (backupPath != null && Directory.Exists(backupPath))
                    Directory.Move(backupPath, repoDir);
            }
            catch
            {
                // Restore is best-effort; the accumulated log / thrown exception still explains the failure.
            }
        }

        /// <summary>Acquire the shared single-flight guard. Internal for deterministic re-entrancy tests.</summary>
        internal static bool TryEnter()
        {
            lock (_gate)
            {
                if (_running) return false;
                _running = true;
                return true;
            }
        }

        /// <summary>Release the shared single-flight guard.</summary>
        internal static void Exit()
        {
            lock (_gate) { _running = false; }
        }
    }
}
