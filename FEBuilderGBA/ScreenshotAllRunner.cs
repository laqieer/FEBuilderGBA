using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
            // Use BeginInvoke so the message loop is pumping before we start
            BeginInvoke(new Action(RunCapture));
        }

        private void RunCapture()
        {
            int captured = 0;
            int failed = 0;
            var failures = new List<string>();

            Directory.CreateDirectory(_outputDir);

            string romVersion = Program.ROM?.RomInfo?.VersionToFilename ?? "Unknown";

            var factories = ScreenshotFormRegistry.GetAllFormFactories();
            Console.WriteLine($"SCREENSHOT: Capturing {factories.Count} forms...");

            foreach (var (name, factory) in factories)
            {
                Form? form = null;
                try
                {
                    form = factory();
                    form.Show();
                    // Force layout so DrawToBitmap renders content
                    form.Refresh();
                    Application.DoEvents();

                    int w = Math.Max(form.Width, 100);
                    int h = Math.Max(form.Height, 100);
                    using var bmp = new Bitmap(w, h);
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));

                    string fileName = $"WinForms_{name}_{romVersion}.png";
                    string filePath = Path.Combine(_outputDir, fileName);
                    bmp.Save(filePath, ImageFormat.Png);

                    captured++;
                    Console.WriteLine($"SCREENSHOT: {name} ... OK ({filePath})");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add(name);
                    Console.WriteLine($"SCREENSHOT: {name} ... FAIL: {ex.Message}");
                }
                finally
                {
                    try { form?.Close(); form?.Dispose(); } catch { }
                }
            }

            Console.WriteLine($"SCREENSHOT: Results: {captured} captured, {failed} failed out of {factories.Count}");
            if (failures.Count > 0)
                Console.WriteLine($"SCREENSHOT: Failures: {string.Join(", ", failures)}");

            Environment.ExitCode = failed > 0 ? 1 : 0;
            Close();
        }
    }
}
