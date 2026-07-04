using System;
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
            return (bool)m.Invoke(c, new object[] { 2 }); // STATE_VISIBLE = 0x02
        }

        static void RunSTA(Action body)
        {
            ExceptionDispatchInfo edi = null;
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
    }
}
