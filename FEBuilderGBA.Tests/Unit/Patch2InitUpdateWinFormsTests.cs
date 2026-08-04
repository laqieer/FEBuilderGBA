using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #1812/#1813/#2037: verifies the WinForms in-app content-repo Initialize/Update wiring — the shared
    /// <see cref="ContentRepoGitWinForms.RunInitUpdate"/> host + the patch2 facade, the PatchForm button,
    /// and (post-#2037) that OptionForm's per-tab "Submodule Remote URLs" GroupBox/fields/handlers are
    /// fully retired in favor of routing through the shared ContentRepoSetupWizardForm via the clickable
    /// X_EXPLAIN_CONTENT_REPOSITORIES label. The clone/update logic itself is covered by the Core
    /// Patch2GitService/ContentRepoGitService tests; the wizard's own row/persistence behavior is covered
    /// by ContentRepoSetupWizardWinFormsTests.
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
        public void PatchForm_HasPatch2InitUpdateButtonCreatorAndHandler()
        {
            Assert.NotNull(typeof(PatchForm).GetMethod("AddPatch2InitUpdateButton", NPI));
            Assert.NotNull(typeof(PatchForm).GetMethod("Patch2InitUpdateButton_Click", NPI));
        }

        // #2037: the per-tab, per-URL-field "Submodule Remote URLs" GroupBox (patch2 / FE-Repo /
        // FE-Repo-Music fields + buttons bolted onto whichever tab happened to be last) is retired in
        // favor of the single shared ContentRepoSetupWizardForm, reachable from OptionForm only via the
        // clickable "Configure content repositories…" label (X_EXPLAIN_CONTENT_REPOSITORIES) on the Path
        // tab. Assert the wizard route still exists and every legacy field/method is gone — a stronger,
        // less brittle replacement for the old GroupBox-attaches-to-a-tab behavioral test (that control
        // tree/GroupBox no longer exists at all, by design).
        [Fact]
        public void OptionForm_ExposesWizardRoute_AndLegacySubmoduleUiIsFullyRemoved()
        {
            Assert.NotNull(typeof(OptionForm).GetMethod("X_EXPLAIN_CONTENT_REPOSITORIES_Click", NPI));

            Assert.Null(typeof(OptionForm).GetField("_submodulePatch2Url", NPI));
            Assert.Null(typeof(OptionForm).GetField("_submoduleFERepoUrl", NPI));
            Assert.Null(typeof(OptionForm).GetField("_submoduleFERepoMusicUrl", NPI));
            Assert.Null(typeof(OptionForm).GetField("_optionPatch2InitUpdateButton", NPI));
            Assert.Null(typeof(OptionForm).GetField("_optionFERepoInitUpdateButton", NPI));
            Assert.Null(typeof(OptionForm).GetField("_optionFERepoMusicInitUpdateButton", NPI));

            Assert.Null(typeof(OptionForm).GetMethod("LoadSubmoduleUrls", NPI));
            Assert.Null(typeof(OptionForm).GetMethod("SaveSubmoduleUrls", NPI));
            Assert.Null(typeof(OptionForm).GetMethod("OptionPatch2InitUpdateButton_Click", NPI));
            Assert.Null(typeof(OptionForm).GetMethod("OptionFERepoInitUpdateButton_Click", NPI));
            Assert.Null(typeof(OptionForm).GetMethod("OptionFERepoMusicInitUpdateButton_Click", NPI));
            Assert.Null(typeof(OptionForm).GetMethod("RunSubmoduleInitUpdate", NPI));

            // Behavioral half: constructing the real form must not create any of the old dead-UI
            // controls (by their legacy Name) anywhere in its control tree.
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

                        Assert.Empty(form.Controls.Find("OptionPatch2InitUpdateButton", true));
                        Assert.Empty(form.Controls.Find("OptionFERepoInitUpdateButton", true));
                        Assert.Empty(form.Controls.Find("OptionFERepoMusicInitUpdateButton", true));

                        var explainLabel = form.Controls.Find("X_EXPLAIN_CONTENT_REPOSITORIES", true);
                        Assert.Single(explainLabel);
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
            Assert.True(t.Join(TimeSpan.FromSeconds(30)), "STA thread did not complete within the timeout.");
            Assert.True(err == null, err);
        }
    }
}
