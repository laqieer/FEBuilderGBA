using System;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    // #1816: the patch2 Git button (fetch config/patch2 via git) must be reachable when the core
    // app is already up-to-date. patch2Only mode hides the Core button, shows the Git button
    // (visible AND enabled even if git is missing, so the AutoUpdatePatch2Git -> TryAutoInstallGit
    // fallback is live), and shows a patch2-focused message instead of the misleading
    // "core -> 00000000.00" update text.
    [Collection("SharedState")]
    public class ToolUpdateDialogPatch2Tests
    {
        static T Get<T>(Control root, string name) where T : Control
        {
            var found = root.Controls.Find(name, true);
            Assert.NotEmpty(found);
            return Assert.IsAssignableFrom<T>(found[0]);
        }

        // Control.Visible's GETTER returns EFFECTIVE visibility (false while the parent form is not
        // shown), so read the control's own STATE_VISIBLE flag (what InitSplitPackage actually set),
        // independent of whether the form has been displayed.
        static bool OwnVisible(Control c)
        {
            var m = typeof(Control).GetMethod("GetState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(m); // guard: GetState(int) must exist on this .NET WinForms build
            return Assert.IsType<bool>(m.Invoke(c, new object[] { 2 })); // STATE_VISIBLE = 0x02
        }

        static void RunSTA(Action body)
        {
            ExceptionDispatchInfo? edi = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { edi = ExceptionDispatchInfo.Capture(ex); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("STA thread did not complete within 30 seconds");
            edi?.Throw();
        }

        [Fact]
        public void InitSplitPackage_Patch2Only_ReachesGitButton_HidesCore_NoMisleadingVersion()
        {
            RunSTA(() =>
            {
                using var f = new ToolUpdateDialogForm();
                f.CreateControl();
                var info = new UpdateInfo(); // URL_CORE null -> no core update

                f.InitSplitPackage(info, patch2Only: true);

                var patch2 = Get<Button>(f, "UpdatePatch2GitButton");
                var core   = Get<Button>(f, "UpdateCoreButton");
                var full   = Get<Button>(f, "AutoUpdateButton");
                var msg    = Get<Control>(f, "Message");

                Assert.True(OwnVisible(patch2), "Git Patch2 button must be reachable when core is up-to-date (#1816)");
                Assert.True(patch2.Enabled, "Git Patch2 button must be enabled even if git is missing (auto-install path)");
                Assert.False(OwnVisible(core), "Core button must be hidden when the core is already up-to-date");
                Assert.False(OwnVisible(full), "Full/Auto button is always hidden in split-package mode");
                var openBrowser = Get<Button>(f, "OpenBrowserButton");
                Assert.False(OwnVisible(openBrowser), "Open-Browser must be hidden in patch2-only mode (it would point at the core release, not patch2)");
                // The patch2-only message must NOT show the bogus "core -> 00000000.00" transition.
                Assert.DoesNotContain("00000000.00", msg.Text);
            });
        }

        [Fact]
        public void InitSplitPackage_WithCoreUpdate_ShowsCoreAndGitButtons()
        {
            RunSTA(() =>
            {
                using var f = new ToolUpdateDialogForm();
                f.CreateControl();
                var info = new UpdateInfo { URL_CORE = "https://example.com/FEBuilderGBA_ver_20260704.04.zip" };

                f.InitSplitPackage(info); // normal split-package (a core update exists)

                var core   = Get<Button>(f, "UpdateCoreButton");
                var patch2 = Get<Button>(f, "UpdatePatch2GitButton");

                Assert.True(OwnVisible(core), "Core button must be visible when a core update exists");
                Assert.True(OwnVisible(patch2), "Git Patch2 button is always visible in split-package mode (#1816)");
                Assert.True(patch2.Enabled);
            });
        }

        [Fact]
        public void LegacyPatch2Handler_DelegatesToSharedHostWithoutInlineTransactionOrGuard()
        {
            string body = LegacyPatch2HandlerBody();

            Assert.Contains("Patch2GitService.IsRunning()", body);
            Assert.Contains("Patch2GitWinForms.RunInitUpdate(this, null)", body);
            Assert.DoesNotContain("Patch2GitService.TryEnter()", body);
            Assert.DoesNotContain("GitUtil.Clone(", body);
            Assert.DoesNotContain("GitUtil.Update(", body);
            Assert.DoesNotContain("PollGitProgress", body);
        }

        // #2036: the legacy dialog must not re-implement any part of the shared content-repo host —
        // no guard acquire/release, no inline clone/backup transaction, no duplicated result strings,
        // and exactly ONE delegation call so a retry can't run two operations.
        [Fact]
        public void LegacyPatch2Handler_HasNoGuardPollOrInlineTransaction_AndDelegatesExactlyOnce()
        {
            string body = LegacyPatch2HandlerBody();

            Assert.Equal(1, Occurrences(body, "Patch2GitWinForms.RunInitUpdate"));
            Assert.DoesNotContain("TryEnter", body);
            Assert.DoesNotContain("ContentRepoGitService.Exit", body);
            Assert.DoesNotContain("Patch2GitService.Exit", body);
            Assert.DoesNotContain("Directory.Move", body);
            Assert.DoesNotContain("Directory.Delete", body);
            Assert.DoesNotContain("GitUtil.Clone", body);
            Assert.DoesNotContain("GitUtil.Update", body);
            Assert.DoesNotContain("PollGitProgress", body);
            Assert.DoesNotContain("AutoPleaseWait", body);
            // Legacy, host-owned result strings must not be duplicated here.
            Assert.DoesNotContain("initialization failed (git exit", body);
            Assert.DoesNotContain("update failed (git exit", body);
            Assert.DoesNotContain("operation failed.", body);
            Assert.DoesNotContain("Restart recommended", body);
        }

        // #2036: the canonical AlreadyRunning key is the one that already exists in ja.txt/zh.txt.
        [Fact]
        public void LegacyPatch2Handler_UsesCanonicalAlreadyRunningKey_ObservedBeforeAnyPrompt()
        {
            string body = LegacyPatch2HandlerBody();

            Assert.Contains("R.ShowStopError(\"Another content repository operation is already running.\");", body);
            Assert.DoesNotContain("A content repository operation is already running.", body);
            Assert.DoesNotContain("patch2 operation is already running", body);

            int isRunning = body.IndexOf("Patch2GitService.IsRunning()", StringComparison.Ordinal);
            int autoInstall = body.IndexOf("TryAutoInstallGit()", StringComparison.Ordinal);
            int savePrompt = body.IndexOf("R.ShowQ(", StringComparison.Ordinal);
            int delegateCall = body.IndexOf("Patch2GitWinForms.RunInitUpdate", StringComparison.Ordinal);
            Assert.True(isRunning >= 0 && autoInstall > isRunning, "IsRunning must be observed before the Git auto-install prompt");
            Assert.True(savePrompt > isRunning, "IsRunning must be observed before the unsaved-ROM prompt");
            Assert.True(delegateCall > savePrompt, "the shared host runs after the preconditions");
        }

        // #2036: Success => OK + close, Failed/GitNotFound => close, AlreadyRunning => dialog stays open.
        [Fact]
        public void LegacyPatch2Handler_CloseMappingMatchesResultKinds()
        {
            string body = LegacyPatch2HandlerBody();

            int success = body.IndexOf("result.Kind == Patch2GitResultKind.Success", StringComparison.Ordinal);
            int okResult = body.IndexOf("DialogResult.OK", success, StringComparison.Ordinal);
            int okClose = body.IndexOf("this.Close();", okResult, StringComparison.Ordinal);
            Assert.True(success >= 0 && okResult > success && okClose > okResult,
                "Success must set DialogResult.OK and then close");

            int failed = body.IndexOf("result.Kind == Patch2GitResultKind.Failed", StringComparison.Ordinal);
            Assert.True(failed > okClose, "the failure branch follows the success branch");
            Assert.Contains("result.Kind == Patch2GitResultKind.GitNotFound", body);
            int failClose = body.IndexOf("this.Close();", failed, StringComparison.Ordinal);
            Assert.True(failClose > failed, "Failed/GitNotFound must close the dialog");

            // AlreadyRunning is handled by the early precondition return only: the dialog stays open,
            // and the result switch never mentions it.
            int alreadyRunning = body.IndexOf("already running", StringComparison.Ordinal);
            Assert.True(alreadyRunning >= 0 && alreadyRunning < success,
                "AlreadyRunning is refused before the operation, not by the close mapping");
            Assert.DoesNotContain("Patch2GitResultKind.AlreadyRunning", body);
            Assert.Equal(2, Occurrences(body, "this.Close();"));   // success + failure only
        }

        static string LegacyPatch2HandlerBody()
        {
            string source = File.ReadAllText(Path.Combine(FindRepoRoot(), "FEBuilderGBA", "ToolUpdateDialogForm.cs"));
            int start = source.IndexOf("private void AutoUpdatePatch2Git", StringComparison.Ordinal);
            int end = source.IndexOf("private string TryAutoInstallGit", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            return source.Substring(start, end - start);
        }

        static int Occurrences(string haystack, string needle)
        {
            int count = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln"))) return dir;
                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent ?? "";
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
