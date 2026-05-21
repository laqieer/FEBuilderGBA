using System;
using Avalonia;

namespace FEBuilderGBA.Avalonia
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Gap-sweep flags are pure static-analysis (no ROM, no UI, no
            // Skia / Avalonia runtime required). Detect them BEFORE the
            // AppBuilder boots Skia / the windowing system because on
            // headless Linux CI runners SkiaSharp's native libSkiaSharp can
            // fail to initialize (version-skew between the OS package and
            // the managed NuGet) and would abort the process before
            // OnFrameworkInitializationCompleted's gap-sweep dispatch ever
            // runs. Fast-path the sweep here and exit cleanly.
            //
            // We DON'T call Environment.Exit inside RunGapSweepStandalone
            // because Linux CI's bash with `set -e` already aborts the
            // whole step on a non-zero exit — returning the code as the
            // process exit lets per-sweep failures be visible without
            // throwing off the shell's exit-on-error behaviour the
            // workflow file expects.
            int? gapCode = App.RunGapSweepStandalone(args);
            if (gapCode.HasValue)
            {
                Environment.Exit(gapCode.Value);
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
