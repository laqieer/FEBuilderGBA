using System;
using Avalonia;
using Avalonia.Media;

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
                .With(CreateFontManagerOptions())
                .LogToTrace();

        /// <summary>
        /// Builds the app-level <see cref="FontManagerOptions"/> used to register
        /// cross-platform CJK / glyph font fallbacks (issue #1692).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This ONLY populates <see cref="FontManagerOptions.FontFallbacks"/>, which Avalonia's
        /// <c>FontManager</c> consults <em>per codepoint</em> — and only when the primary font
        /// lacks a glyph for that codepoint. It does NOT change which font is used by default for
        /// text the primary font can already render.
        /// </para>
        /// <para>
        /// <see cref="FontManagerOptions.DefaultFamilyName"/> is intentionally left null. Setting a
        /// default family would override the primary look on every platform (especially Windows,
        /// which already substitutes CJK glyphs well) and risks the known Avalonia startup crash
        /// when the configured family is empty or not installed (Avalonia issues #10614 / #12140).
        /// </para>
        /// <para>
        /// Each platform lists its native CJK families first. Families that are not installed on a
        /// given OS are silently skipped by Avalonia's <c>FontManager.TryMatchCharacter</c> — it
        /// calls <c>TryGetGlyphTypeface</c>, which fails-and-continues to the next candidate, and
        /// finally falls through to the platform default font. The list is needed most on macOS,
        /// where the OS does NOT auto-substitute a CJK font when the primary UI font lacks the
        /// glyph, causing ROM-decoded names and Japanese labels to render as "tofu" boxes.
        /// </para>
        /// </remarks>
        internal static FontManagerOptions CreateFontManagerOptions()
        {
            // DefaultFamilyName is intentionally NOT set — see the remarks above.
            return new FontManagerOptions
            {
                FontFallbacks = new[]
                {
                    // macOS — the OS does not auto-substitute CJK; these cover SC/JP/KR + Arial Unicode.
                    new FontFallback { FontFamily = new FontFamily("PingFang SC") },
                    new FontFallback { FontFamily = new FontFamily("Hiragino Sans") },
                    new FontFallback { FontFamily = new FontFamily("Apple SD Gothic Neo") },
                    new FontFallback { FontFamily = new FontFamily("Arial Unicode MS") },

                    // Windows — kept after macOS; Windows already substitutes well, these are harmless.
                    new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
                    new FontFallback { FontFamily = new FontFamily("Yu Gothic") },
                    new FontFallback { FontFamily = new FontFamily("Malgun Gothic") },

                    // Linux — Noto / WenQuanYi CJK families.
                    new FontFallback { FontFamily = new FontFamily("Noto Sans CJK SC") },
                    new FontFallback { FontFamily = new FontFamily("Noto Sans CJK JP") },
                    new FontFallback { FontFamily = new FontFamily("WenQuanYi Micro Hei") },
                },
            };
        }
    }
}
