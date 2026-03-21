using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    public class OptionFormLayoutTests
    {
        [Fact]
        public void GitExplainLabel_DoesNotOverlap_GitPathControls()
        {
            Exception caught = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // OptionForm constructor requires Program.Config to be initialized.
                    // Program.Config has a private setter, so use reflection.
                    if (Program.Config == null)
                    {
                        var prop = typeof(Program).GetProperty("Config",
                            BindingFlags.Public | BindingFlags.Static);
                        prop.SetValue(null, new ConfigWinForms());
                    }

                    var form = new OptionForm();
                    form.PerformLayout();

                    // Access controls via Controls.Find (searches recursively)
                    var gitLabel = GetControl<Label>(form, "X_EXPLAIN_GIT");
                    var gitTextbox = GetControl<Control>(form, "git_path_textbox");
                    var tabPagePath = GetControl<TabPage>(form, "tabPagePath");

                    // Label must be AutoSize to allow text wrapping
                    Assert.True(gitLabel.AutoSize, "X_EXPLAIN_GIT.AutoSize should be true");

                    // Label bottom must not overlap git path textbox top
                    Assert.True(gitLabel.Bottom <= gitTextbox.Top,
                        $"X_EXPLAIN_GIT.Bottom ({gitLabel.Bottom}) should be <= git_path_textbox.Top ({gitTextbox.Top})");

                    // X_EXPLAIN_NECESSARY_PROGRAM must be below git controls, not between label and controls
                    var necessaryProgramLabel = GetControl<Label>(form, "X_EXPLAIN_NECESSARY_PROGRAM");
                    Assert.True(necessaryProgramLabel.Top >= gitTextbox.Bottom,
                        $"X_EXPLAIN_NECESSARY_PROGRAM.Top ({necessaryProgramLabel.Top}) should be >= git_path_textbox.Bottom ({gitTextbox.Bottom})");

                    // Tab page must scroll to handle overflow
                    Assert.True(tabPagePath.AutoScroll, "tabPagePath.AutoScroll should be true");

                    form.Dispose();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (caught != null)
                throw caught;
        }

        private static T GetControl<T>(Control parent, string name) where T : Control
        {
            var controls = parent.Controls.Find(name, true);
            Assert.True(controls.Length > 0, $"Control '{name}' not found");
            return Assert.IsAssignableFrom<T>(controls[0]);
        }
    }
}
