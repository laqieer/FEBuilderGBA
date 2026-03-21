using System;
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
                    form.PerformLayout();

                    // Access controls via Controls.Find (searches recursively)
                    var gitLabel = GetControl<Label>(form, "X_EXPLAIN_GIT");
                    var gitTextbox = GetControl<Control>(form, "git_path_textbox");
                    var tabPagePath = GetControl<TabPage>(form, "tabPagePath");

                    // Label must be AutoSize to allow text wrapping
                    Assert.True(gitLabel.AutoSize, "X_EXPLAIN_GIT.AutoSize should be true");

                    // Force label to wrap by setting a long string (simulates translated/long text)
                    gitLabel.Text = "If you want to use Git, please set the path to git.exe below. " +
                        "Leave blank or enter \"git\" to use the system PATH. " +
                        "You can download Git from https://git-scm.com/download/win if needed.";
                    tabPagePath.PerformLayout();

                    // Label bottom must not overlap git path textbox top even with wrapped text
                    Assert.True(gitLabel.Bottom <= gitTextbox.Top,
                        $"X_EXPLAIN_GIT.Bottom ({gitLabel.Bottom}) should be <= git_path_textbox.Top ({gitTextbox.Top})");

                    // X_EXPLAIN_NECESSARY_PROGRAM must be below git controls, not between label and controls
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
    }
}
