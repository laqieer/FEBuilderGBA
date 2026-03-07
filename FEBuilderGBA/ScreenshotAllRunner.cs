using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace FEBuilderGBA
{
    /// <summary>
    /// Hidden form that iterates all WinForms editors from ScreenshotFormRegistry,
    /// captures each via DrawToBitmap(), and saves as PNG.
    /// Output filenames match the Avalonia screenshot naming convention:
    ///   WinForms_{ViewName}_{RomVersion}.png
    /// </summary>
    class ScreenshotAllRunner : Form
    {
        private readonly string _outputDir;

        public ScreenshotAllRunner(string outputDir)
        {
            _outputDir = outputDir;

            // Hidden form — only exists to pump the message loop
            Text = "ScreenshotAllRunner";
            Opacity = 0;
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Size = new Size(1, 1);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RunCapture();
        }

        private void RunCapture()
        {
            int captured = 0;
            int failed = 0;
            var failures = new List<string>();

            Directory.CreateDirectory(_outputDir);

            string romVersion = Program.ROM?.RomInfo?.VersionToFilename ?? "Unknown";

            var factories = ScreenshotFormRegistry.GetAllFormFactories();
            U.echo($"SCREENSHOT: Capturing {factories.Count} forms...");

            foreach (var (name, factory) in factories)
            {
                Form? form = null;
                try
                {
                    form = factory();

                    // CreateControl forces handle creation and layout without
                    // making the form visible — avoids triggering side effects
                    // from Show() that go through the message loop.
                    form.CreateControl();

                    // Fire the OnLoad event via reflection so that InputFormRef
                    // populates controls with ROM data (RomToUI).  CreateControl
                    // alone only creates the window handle; Load fires during
                    // SetVisibleCore(true) which we avoid to prevent modal dialogs.
                    FireOnLoad(form);

                    int w = Math.Max(form.Width, 100);
                    int h = Math.Max(form.Height, 100);
                    using var bmp = new Bitmap(w, h);
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));

                    string fileName = $"WinForms_{name}_{romVersion}.png";
                    string filePath = Path.Combine(_outputDir, fileName);
                    bmp.Save(filePath, ImageFormat.Png);

                    captured++;
                    U.echo($"SCREENSHOT: {name} ... OK");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add(name);
                    U.echo($"SCREENSHOT: {name} ... FAIL: {ex.Message}");
                }
                finally
                {
                    try { form?.Close(); form?.Dispose(); } catch { }
                }
            }

            U.echo($"SCREENSHOT: Results: {captured} captured, {failed} failed out of {factories.Count}");
            if (failures.Count > 0)
                U.echo($"SCREENSHOT: Failures: {string.Join(", ", failures)}");

            Environment.ExitCode = failed > 0 ? 1 : 0;
            Close();
        }

        /// <summary>
        /// Invokes the protected OnLoad method on a Form via reflection.
        /// This fires all Load event handlers (which typically call
        /// InputFormRef.RomToUI to populate controls with ROM data)
        /// without making the form visible or entering the message loop.
        /// </summary>
        private static void FireOnLoad(Form form)
        {
            try
            {
                var onLoad = typeof(Form).GetMethod(
                    "OnLoad",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(EventArgs) },
                    null);
                onLoad?.Invoke(form, new object[] { EventArgs.Empty });
            }
            catch (TargetInvocationException ex)
            {
                // Log but don't rethrow — some forms may fail during Load
                // when running headless (missing emulator, etc.)
                U.echo($"SCREENSHOT: OnLoad warning: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
    }
}
