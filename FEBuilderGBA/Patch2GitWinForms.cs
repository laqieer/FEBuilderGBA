using System;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// #1812: WinForms host for the cross-platform <see cref="Patch2GitService"/> — runs an in-app
    /// patch2 Initialize (clone) / Update (fetch+reset) with the standard <see cref="InputFormRef.AutoPleaseWait"/>
    /// progress pump, mirroring <c>ToolUpdateDialogForm.AutoUpdatePatch2Git</c>. Shared by the OptionForm
    /// and PatchForm "Initialize / Update Patch2" buttons so the logic lives in exactly one place and all
    /// WinForms patch2 entry points go through the same single-flight guard inside Patch2GitService.
    /// </summary>
    public static class Patch2GitWinForms
    {
        /// <summary>
        /// Runs the patch2 init/update synchronously on the UI thread (pumping the message loop while the
        /// git op runs on a background Task) and shows the localized result. Returns the
        /// <see cref="Patch2GitResult"/> so callers (e.g. PatchForm) can rescan on success.
        /// <paramref name="urlOverride"/> (nullable) forces a specific remote — OptionForm passes its
        /// Patch2 URL textbox so a just-typed custom fork URL takes effect.
        /// </summary>
        public static Patch2GitResult RunInitUpdate(Form owner, string urlOverride)
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

                pleaseWait.DoEvents("Git: patch2 ...");
                var task = System.Threading.Tasks.Task.Run(
                    () => Patch2GitService.InitializeOrUpdate(Program.BaseDirectory, progress, urlOverride));

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
                    // Patch2GitService is result-based for the git ops themselves, but its backup file-move
                    // (Directory.Move/Delete) can still throw on a real I/O error — convert a faulted Task
                    // into a Failed result so reading task.Result never crashes the UI.
                    result = new Patch2GitResult
                    {
                        Kind = Patch2GitResultKind.Failed,
                        ExitCode = -1,
                        Log = (ex.InnerException ?? ex).ToString(),
                    };
                }
            }

            // Back on the UI thread after the using block — show the localized result.
            switch (result.Kind)
            {
                case Patch2GitResultKind.GitNotFound:
                    R.ShowStopError("Git was not found. Install Git and try again, or set up config/patch2 manually — see the Patch Database Setup wiki page.");
                    break;
                case Patch2GitResultKind.AlreadyRunning:
                    R.ShowStopError("A patch database operation is already running.");
                    break;
                case Patch2GitResultKind.Failed:
                    string logTail = string.IsNullOrEmpty(result.Log) ? "" : "\r\n\r\n" + result.Log.Trim();
                    if (result.ExitCode < 0)
                        // Exception-synthesized fallback (a rare backup file-move I/O failure) — the
                        // clone/update distinction isn't reliably known here, so use neutral wording.
                        R.ShowStopError("Patch database operation failed.{0}", logTail);
                    else if (result.WasClone)
                        R.ShowStopError("Patch database initialization failed (git exit {0}).{1}", result.ExitCode, logTail);
                    else
                        R.ShowStopError("Patch database update failed (git exit {0}).{1}", result.ExitCode, logTail);
                    break;
                case Patch2GitResultKind.Success:
                    R.ShowOK("Patch database updated. Restart recommended for all changes to take full effect.");
                    break;
            }

            return result;
        }
    }
}
