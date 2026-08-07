using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class RichTextBoxExClipboardTests
    {
        static void RunSTA(Action body)
        {
            ExceptionDispatchInfo? edi = null;
            var thread = new Thread(() =>
            {
                IDataObject? original = null;
                try
                {
                    original = Clipboard.GetDataObject();
                    body();
                }
                catch (Exception ex)
                {
                    edi = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    try
                    {
                        if (original != null)
                        {
                            Clipboard.SetDataObject(original, true);
                        }
                        else
                        {
                            Clipboard.Clear();
                        }
                    }
                    catch
                    {
                        // Best-effort restore only; the assertion result is what matters.
                    }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!thread.Join(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("STA thread did not complete within 30 seconds");

            edi?.Throw();
        }

        [Fact]
        public void HasUsableClipboardText_ReturnsFalseOnlyForNullOrEmpty()
        {
            Assert.False(RichTextBoxEx.HasUsableClipboardText(null!));
            Assert.False(RichTextBoxEx.HasUsableClipboardText(string.Empty));
            Assert.True(RichTextBoxEx.HasUsableClipboardText(" "));
            Assert.True(RichTextBoxEx.HasUsableClipboardText("clipboard text"));
        }

        [Fact]
        public void TryGetClipboardText_ReturnsFalseForEmptyClipboard()
        {
            RunSTA(() =>
            {
                Clipboard.Clear();

                Assert.False(RichTextBoxEx.TryGetClipboardText(out string text));
                Assert.Null(text);
            });
        }

        [Fact]
        public void TryGetClipboardText_ReturnsTrueForText_WithoutMutatingClipboard()
        {
            RunSTA(() =>
            {
                Clipboard.SetText("clipboard text");

                Assert.True(RichTextBoxEx.TryGetClipboardText(out string text));
                Assert.Equal("clipboard text", text);
                Assert.Equal("clipboard text", Clipboard.GetText());
            });
        }
    }
}
