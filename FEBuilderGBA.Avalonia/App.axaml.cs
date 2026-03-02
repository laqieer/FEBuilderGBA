using System;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FEBuilderGBA.SkiaSharp;

namespace FEBuilderGBA.Avalonia
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Register code pages for Shift-JIS, etc.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // Set up Core state
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            CoreState.BaseDirectory = baseDir;
            CoreState.Services = new AvaloniaAppServices();
            CoreState.ImageService = new SkiaImageService();

            // Wire headless caches
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.LintCache = new HeadlessEtcCache();
            CoreState.WorkSupportCache = new HeadlessEtcCache();
            CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder();

            // Load config
            string configPath = Path.Combine(baseDir, "config", "config.xml");
            if (File.Exists(configPath))
            {
                var config = new Config();
                config.Load(configPath);
                CoreState.Config = config;
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new Views.MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
