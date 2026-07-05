using System;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// #1813: WinForms host for the cross-platform <see cref="ContentRepoGitService"/> — runs an in-app
    /// Initialize (clone) / Update (fetch+reset) of any git-delivered content repo (patch2 / FE-Repo /
    /// FE-Repo-Midi) with the standard <see cref="InputFormRef.AutoPleaseWait"/> progress pump, mirroring
    /// <c>ToolUpdateDialogForm.AutoUpdatePatch2Git</c>. All user-facing messages are parametrized by a
    /// repo <c>displayName</c> so a failure names the correct repo (not always "patch2"). Every WinForms
    /// content-repo entry point goes through the same single-flight guard inside
    /// <see cref="ContentRepoGitService"/>.
    /// </summary>
    public static class ContentRepoGitWinForms
    {
        /// <summary>
        /// Runs the init/update for <paramref name="repoDir"/> (remote <paramref name="url"/>) synchronously
        /// on the UI thread — pumping the message loop while the git op runs on a background Task — and shows
        /// the result named by <paramref name="displayName"/>. Returns the <see cref="Patch2GitResult"/> so
        /// callers (e.g. PatchForm) can rescan on success.
        /// </summary>
        public static Patch2GitResult RunInitUpdate(Form owner, string repoDir, string url, string displayName)
        {
            Patch2GitResult result;

            using (InputFormRef.AutoPleaseWait pleaseWait = new InputFormRef.AutoPleaseWait(owner))
            {
                // lastLine[0] is written by the git output callback on the background thread and consumed
                // by the UI-thread poll loop via Interlocked — the callback never touches any UI control
                // (InputFormRef.DoEvents no-ops on a background thread, so it must not be called there).
                var lastLine = new string[1];
                Action<string> progress = line =>
                {
                    if (!string.IsNullOrEmpty(line))
                        System.Threading.Interlocked.Exchange(ref lastLine[0], line);
                };

                pleaseWait.DoEvents("Git: " + displayName + " ...");
                var task = System.Threading.Tasks.Task.Run(
                    () => ContentRepoGitService.InitializeOrUpdate(repoDir, url, progress));

                // Pump the UI message loop while the background git op runs (mirror PollGitProgress).
                while (!task.IsCompleted)
                {
                    string line = System.Threading.Interlocked.Exchange(ref lastLine[0], null);
                    if (!string.IsNullOrEmpty(line))
                        pleaseWait.DoEvents(line);
                    else
                        System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(80);
                }

                // Flush any final progress line that arrived just before the task completed.
                string lastFlush = System.Threading.Interlocked.Exchange(ref lastLine[0], null);
                if (!string.IsNullOrEmpty(lastFlush))
                    pleaseWait.DoEvents(lastFlush);

                try
                {
                    result = task.Result;
                }
                catch (Exception ex)
                {
                    // ContentRepoGitService is result-based for the git ops themselves, but its backup
                    // file-move (Directory.Move/Delete) can still throw on a real I/O error — convert a
                    // faulted Task into a Failed result so reading task.Result never crashes the UI.
                    result = new Patch2GitResult
                    {
                        Kind = Patch2GitResultKind.Failed,
                        ExitCode = -1,
                        Log = (ex.InnerException ?? ex).ToString(),
                    };
                }
            }

            // Back on the UI thread after the using block — show the result, named by displayName.
            switch (result.Kind)
            {
                case Patch2GitResultKind.GitNotFound:
                    R.ShowStopError("Git was not found. Install Git and try again, or set up {0} manually — see the Patch Database Setup wiki page.", displayName);
                    break;
                case Patch2GitResultKind.AlreadyRunning:
                    R.ShowStopError("A content repository operation is already running.");
                    break;
                case Patch2GitResultKind.Failed:
                    string logTail = string.IsNullOrEmpty(result.Log) ? "" : "\r\n\r\n" + result.Log.Trim();
                    if (result.ExitCode < 0)
                        // Exception-synthesized fallback (a rare backup file-move I/O failure) — the
                        // clone/update distinction isn't reliably known here, so use neutral wording.
                        R.ShowStopError("{0} operation failed.{1}", displayName, logTail);
                    else if (result.WasClone)
                        R.ShowStopError("{0} initialization failed (git exit {1}).{2}", displayName, result.ExitCode, logTail);
                    else
                        R.ShowStopError("{0} update failed (git exit {1}).{2}", displayName, result.ExitCode, logTail);
                    break;
                case Patch2GitResultKind.Success:
                    R.ShowOK("{0} updated. Restart recommended for all changes to take full effect.", displayName);
                    break;
            }

            return result;
        }
    }
}
