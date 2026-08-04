using System;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class OptionFormLayoutTests
    {
        [Fact]
        public void GitExplainLabel_DoesNotOverlap_GitPathControls()
        {
            ExceptionDispatchInfo edi = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new OptionForm();
                    form.CreateControl();
                    form.PerformLayout();

                    // Access controls via Controls.Find (searches recursively)
                    var gitLabel = GetControl<Label>(form, "X_EXPLAIN_GIT");
                    var gitTextbox = GetControl<Control>(form, "git_path_textbox");
                    var tabPagePath = GetControl<TabPage>(form, "tabPagePath");

                    // Label must be AutoSize to allow text wrapping
                    Assert.True(gitLabel.AutoSize, "X_EXPLAIN_GIT.AutoSize should be true");

                    // Verify that long text WOULD wrap at the label's constrained width
                    var longText = "If you want to use Git, please set the path to git.exe below. " +
                        "Leave blank or enter \"git\" to use the system PATH. " +
                        "You can download Git from https://git-scm.com/download/win if needed.";
                    var constrainedWidth = gitLabel.MaximumSize.Width > 0 ? gitLabel.MaximumSize.Width : gitLabel.Width;
                    var singleLineSize = TextRenderer.MeasureText(longText, gitLabel.Font);
                    var wrappedSize = TextRenderer.MeasureText(longText, gitLabel.Font,
                        new Size(constrainedWidth, 0), TextFormatFlags.WordBreak);
                    Assert.True(wrappedSize.Height > singleLineSize.Height,
                        $"Long text should wrap: single-line height={singleLineSize.Height}, " +
                        $"wrapped height={wrappedSize.Height} at width={constrainedWidth}");

                    // Label bottom must not overlap git path textbox top
                    Assert.True(gitLabel.Bottom <= gitTextbox.Top,
                        $"X_EXPLAIN_GIT.Bottom ({gitLabel.Bottom}) should be <= git_path_textbox.Top ({gitTextbox.Top})");

                    // Verify there's enough gap for the wrapped text height
                    var gap = gitTextbox.Top - gitLabel.Top;
                    Assert.True(gap >= wrappedSize.Height,
                        $"Gap ({gap}px) should be >= wrapped text height ({wrappedSize.Height}px)");

                    // X_EXPLAIN_NECESSARY_PROGRAM must be below git controls
                    var necessaryProgramLabel = GetControl<Label>(form, "X_EXPLAIN_NECESSARY_PROGRAM");
                    Assert.True(necessaryProgramLabel.Top >= gitTextbox.Bottom,
                        $"X_EXPLAIN_NECESSARY_PROGRAM.Top ({necessaryProgramLabel.Top}) should be >= git_path_textbox.Bottom ({gitTextbox.Bottom})");

                    // Tab page must scroll to handle overflow
                    Assert.True(tabPagePath.AutoScroll, "tabPagePath.AutoScroll should be true");
                }
                catch (Exception ex)
                {
                    edi = ExceptionDispatchInfo.Capture(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("STA thread did not complete within 30 seconds");

            edi?.Throw();
        }

        private static T GetControl<T>(Control parent, string name) where T : Control
        {
            var controls = parent.Controls.Find(name, true);
            Assert.True(controls.Length > 0, $"Control '{name}' not found");
            return Assert.IsAssignableFrom<T>(controls[0]);
        }

        // #2037: regression guard for the retired per-tab "Submodule Remote URLs" GroupBox. That legacy
        // code attached a Dock=Bottom, Height=230 GroupBox to whichever TabPage happened to be LAST in
        // tabControl1 (tabPageFunc3 / "機能3") — added AFTER that tab's own controls, so it z-ordered on
        // top and covered the bottom ~230px of real Function3 controls (func_textextencodingtencoding at
        // y=841, func_lang at y=789, label42 at y=729 all sit inside that band on a 932px-tall tab). This
        // asserts tabPageFunc3 no longer has ANY GroupBox child, its real bottom controls are present,
        // visible, and fully within the tab's client bounds (i.e. not fighting a big Dock=Bottom sibling
        // for the same space), and that the ONE remaining route to the wizard — the clickable
        // "Configure content repositories…" label — lives on tabPagePath, not bolted onto Function3.
        [Fact]
        public void OptionForm_Function3Tab_HasNoLegacySubmoduleGroupBox_AndBottomControlsAreIntact()
        {
            string err = null;
            var t = new Thread(() =>
            {
                try
                {
                    Type prog = typeof(OptionForm).Assembly.GetType("FEBuilderGBA.Program");
                    Type cfgT = typeof(OptionForm).Assembly.GetType("FEBuilderGBA.ConfigWinForms");
                    var cfgProp = prog.GetProperty("Config");
                    object prevCfg = cfgProp.GetValue(null); // restore afterwards — don't leak shared state
                    object cfg = Activator.CreateInstance(cfgT);
                    Config previousCoreConfig = CoreState.Config;
                    cfgProp.GetSetMethod(true).Invoke(null, new[] { cfg });
                    CoreState.Config = (Config)cfg;
                    try
                    {
                        using var form = new OptionForm();
                        form.Show(); // triggers the real OptionForm_Load lifecycle
                        form.PerformLayout();

                        var tabPageFunc3 = GetControl<TabPage>(form, "tabPageFunc3");

                        // No GroupBox was ever a designer-authored child of Function3 — the only GroupBox
                        // that could appear there was the retired programmatic "Submodule Remote URLs" one.
                        foreach (Control c in tabPageFunc3.Controls)
                            Assert.False(c is GroupBox, $"Unexpected GroupBox '{c.Name}' on tabPageFunc3 (legacy Submodule UI leaked back in).");

                        // A non-selected TabPage's children report Visible=false in WinForms even though
                        // they're perfectly real controls — select Function3 so the Visible assertions
                        // below reflect actual runtime visibility, not just tab-switch plumbing.
                        var tabControl1 = GetControl<TabControl>(form, "tabControl1");
                        tabControl1.SelectedTab = tabPageFunc3;
                        Application.DoEvents();
                        tabPageFunc3.PerformLayout();

                        // Direct, designer-authored Function3 controls that sit in the band the legacy
                        // Dock=Bottom GroupBox used to cover — confirm they're present, visible, and fully
                        // inside the tab's own client area (no artificial shrink/clip from a phantom sibling).
                        foreach (string name in new[] { "label42", "func_lang", "explain_func_lang", "func_textextencodingtencoding", "explain_func_textencoding" })
                        {
                            var ctrl = GetControl<Control>(tabPageFunc3, name);
                            Assert.True(ctrl.Visible, $"'{name}' should be visible");
                            Assert.True(ctrl.Bottom <= tabPageFunc3.ClientSize.Height,
                                $"'{name}'.Bottom ({ctrl.Bottom}) should be <= tabPageFunc3.ClientSize.Height ({tabPageFunc3.ClientSize.Height})");
                        }

                        // The single remaining route to the wizard: a clickable label on the Path tab.
                        var tabPagePath = GetControl<TabPage>(form, "tabPagePath");
                        var wizardLink = GetControl<Label>(form, "X_EXPLAIN_CONTENT_REPOSITORIES");
                        Control cur = wizardLink;
                        bool onPathTab = false;
                        while (cur != null)
                        {
                            if (ReferenceEquals(cur, tabPagePath)) { onPathTab = true; break; }
                            cur = cur.Parent;
                        }
                        Assert.True(onPathTab, "X_EXPLAIN_CONTENT_REPOSITORIES should live on tabPagePath.");
                        Assert.Empty(tabPageFunc3.Controls.Find("X_EXPLAIN_CONTENT_REPOSITORIES", true));
                    }
                    finally
                    {
                        cfgProp.GetSetMethod(true).Invoke(null, new[] { prevCfg });
                        CoreState.Config = previousCoreConfig;
                    }
                }
                catch (Exception ex)
                {
                    err = ex.ToString();
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(30)), "STA thread did not complete within the timeout.");
            Assert.True(err == null, err);
        }
    }
}
