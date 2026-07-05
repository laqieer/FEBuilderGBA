using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #1812/#1813: verifies the WinForms in-app content-repo Initialize/Update wiring — the shared
    /// <see cref="ContentRepoGitWinForms.RunInitUpdate"/> host + the patch2 facade, the OptionForm patch2 /
    /// FE-Repo / FE-Repo-Music buttons + handlers, and the PatchForm button. Also a BEHAVIORAL test that
    /// the "Submodule Remote URLs" GroupBox actually attaches to a visible tab (regression guard for the
    /// #1813 dead-UI fix — the pre-existing condition only matched a non-existent "Etc" tab). The
    /// clone/update logic itself is covered by the Core Patch2GitService/ContentRepoGitService tests.
    /// </summary>
    [Collection("SharedState")]
    public class Patch2InitUpdateWinFormsTests
    {
        const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;

        [Fact]
        public void ContentRepoGitWinForms_RunInitUpdate_HasExpectedSignature()
        {
            MethodInfo m = typeof(ContentRepoGitWinForms).GetMethod(
                "RunInitUpdate", new[] { typeof(Form), typeof(string), typeof(string), typeof(string) });
            Assert.NotNull(m);
            Assert.True(m.IsStatic);
            Assert.Equal(typeof(Patch2GitResult), m.ReturnType);
        }

        [Fact]
        public void Patch2GitWinForms_RunInitUpdate_StillPresent()
        {
            MethodInfo m = typeof(Patch2GitWinForms).GetMethod(
                "RunInitUpdate", new[] { typeof(Form), typeof(string) });
            Assert.NotNull(m);
            Assert.Equal(typeof(Patch2GitResult), m.ReturnType);
        }

        [Fact]
        public void OptionForm_HasContentRepoButtonHandlers()
        {
            Assert.NotNull(typeof(OptionForm).GetField("_optionPatch2InitUpdateButton", NPI));
            Assert.NotNull(typeof(OptionForm).GetField("_optionFERepoInitUpdateButton", NPI));
            Assert.NotNull(typeof(OptionForm).GetField("_optionFERepoMusicInitUpdateButton", NPI));
            Assert.NotNull(typeof(OptionForm).GetMethod("OptionPatch2InitUpdateButton_Click", NPI));
            Assert.NotNull(typeof(OptionForm).GetMethod("OptionFERepoInitUpdateButton_Click", NPI));
            Assert.NotNull(typeof(OptionForm).GetMethod("OptionFERepoMusicInitUpdateButton_Click", NPI));
        }

        [Fact]
        public void PatchForm_HasPatch2InitUpdateButtonCreatorAndHandler()
        {
            Assert.NotNull(typeof(PatchForm).GetMethod("AddPatch2InitUpdateButton", NPI));
            Assert.NotNull(typeof(PatchForm).GetMethod("Patch2InitUpdateButton_Click", NPI));
        }

        // #1813 regression guard: the Submodule GroupBox (with the 3 URL fields + 3 Init/Update buttons)
        // must actually attach to a TabPage, not vanish because no "Etc" tab exists. Renders the real
        // OptionForm on an STA thread, invokes LoadSubmoduleUrls, and asserts the patch2 button's parent
        // chain reaches a visible TabPage.
        [Fact]
        public void OptionForm_SubmoduleGroupBox_AttachesToAVisibleTab()
        {
            string err = null;
            var t = new Thread(() =>
            {
                try
                {
                    Type prog = typeof(OptionForm).Assembly.GetType("FEBuilderGBA.Program");
                    Type cfgT = typeof(OptionForm).Assembly.GetType("FEBuilderGBA.ConfigWinForms");
                    var cfgProp = prog.GetProperty("Config");
                    object prevCfg = cfgProp.GetValue(null); // restore afterwards so we don't leak shared state
                    object cfg = Activator.CreateInstance(cfgT);
                    cfgProp.GetSetMethod(true).Invoke(null, new[] { cfg });
                    try
                    {
                        using var form = new OptionForm();
                        form.CreateControl();
                        typeof(OptionForm).GetMethod("LoadSubmoduleUrls", NPI).Invoke(form, null);

                        var found = form.Controls.Find("OptionPatch2InitUpdateButton", true);
                        Assert.Single(found);

                        // Walk up to confirm a TabPage ancestor (i.e. it is attached to a real, visible tab).
                        Control cur = found[0];
                        bool onTab = false;
                        while (cur != null)
                        {
                            if (cur is TabPage) { onTab = true; break; }
                            cur = cur.Parent;
                        }
                        Assert.True(onTab, "Submodule GroupBox/patch2 button is not attached to any TabPage (dead UI).");

                        // The FE-Repo + Music buttons must be present too.
                        Assert.Single(form.Controls.Find("OptionFERepoInitUpdateButton", true));
                        Assert.Single(form.Controls.Find("OptionFERepoMusicInitUpdateButton", true));
                    }
                    finally
                    {
                        cfgProp.GetSetMethod(true).Invoke(null, new[] { prevCfg });
                    }
                }
                catch (Exception ex) { err = ex.ToString(); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            bool completed = t.Join(TimeSpan.FromSeconds(30));
            Assert.True(completed, "STA thread did not complete within the timeout.");
            Assert.True(err == null, err);
        }
    }
}
