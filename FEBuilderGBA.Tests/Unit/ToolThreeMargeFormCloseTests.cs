using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Regression coverage for issue #746 — scheduled E2E (FE6) crashed with
    /// <see cref="NullReferenceException"/> when the screenshot runner closed
    /// <see cref="FEBuilderGBA.ToolThreeMargeForm"/> without first running a
    /// three-way merge. The form's <c>FormClosing</c> handler iterates
    /// <c>ChangeDataList.Count</c>, but the list is only populated by the real
    /// merge-setup paths — so the smoke-opened form blew up on close.
    ///
    /// These tests exercise the two private helpers and the closing handler on
    /// a freshly-constructed form (no merge init) and confirm that the null
    /// guards make them safe.
    /// </summary>
    [Collection("SharedState")]
    public class ToolThreeMargeFormCloseTests
    {
        [Fact]
        public void IsAllMethodNone_ReturnsTrue_WhenChangeDataListIsNull()
        {
            RunOnStaThread(() =>
            {
                using var form = new ToolThreeMargeForm();
                var method = typeof(ToolThreeMargeForm).GetMethod(
                    "isAllMethodNone",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);

                var result = method!.Invoke(form, Array.Empty<object>());
                Assert.IsType<bool>(result);
                Assert.True((bool)result!,
                    "isAllMethodNone() must return true for a never-initialized form so FormClosing can exit cleanly.");
            });
        }

        [Fact]
        public void IsAllMethodNotNone_ReturnsTrue_WhenChangeDataListIsNull()
        {
            RunOnStaThread(() =>
            {
                using var form = new ToolThreeMargeForm();
                var method = typeof(ToolThreeMargeForm).GetMethod(
                    "isAllMethodNotNone",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(method);

                var result = method!.Invoke(form, Array.Empty<object>());
                Assert.IsType<bool>(result);
                Assert.True((bool)result!,
                    "isAllMethodNotNone() must return true for a never-initialized form so FormClosing can exit cleanly.");
            });
        }

        [Fact]
        public void FormClosing_DoesNotThrow_OnEmptyForm()
        {
            RunOnStaThread(() =>
            {
                using var form = new ToolThreeMargeForm();
                var handler = typeof(ToolThreeMargeForm).GetMethod(
                    "ToolThreeMargeForm_FormClosing",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(handler);

                // Invoke the handler directly with a non-cancelling close event.
                // Pre-#746 this threw NullReferenceException from isAllMethodNone()
                // because ChangeDataList was null.
                var args = new FormClosingEventArgs(CloseReason.UserClosing, cancel: false);
                var ex = Record.Exception(() =>
                    handler!.Invoke(form, new object[] { form, args }));

                Assert.Null(ex);
                // The empty-form path returns early — cancel must remain false
                // so the screenshot runner can actually close the window.
                Assert.False(args.Cancel,
                    "FormClosing on an uninitialized form must not cancel — there is no merge state to protect.");
            });
        }

        /// <summary>
        /// WinForms requires single-threaded apartment. Mirrors the helper
        /// used by <c>OptionFormLayoutTests</c>.
        /// </summary>
        private static void RunOnStaThread(Action body)
        {
            ExceptionDispatchInfo? edi = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { edi = ExceptionDispatchInfo.Capture(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("STA thread did not complete within 30 seconds");

            edi?.Throw();
        }
    }
}
