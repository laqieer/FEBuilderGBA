using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using FEBuilderGBA;

// WebAssembly/Browser entry point for the FEBuilderGBA Avalonia GUI (#1864) — the browser
// counterpart of the desktop Program.Main / android MainActivity / iOS AppDelegate. It boots the
// shared FEBuilderGBA.Avalonia App under the browser single-view lifetime, after loading config.
internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Fetch + extract config/ BEFORE the Avalonia app boots (StartBrowserAppAsync), because
        // App.OnFrameworkInitializationCompleted reads CoreState.BaseDirectory synchronously. This
        // is the browser analog of the android/iOS heads extracting config before base.OnCreate /
        // base.FinishedLaunching. Non-fatal on failure — the shell still renders (Config.LoadOrCreate
        // creates defaults; translations are File.Exists-guarded).
        await TryLoadConfigAsync(args);

        // #1867: boot the shared Avalonia app under the browser single-view lifetime. The site
        // originally hung on the loading splash because Avalonia's runtime 404'd on avalonia.js — which
        // turned out to be a symptom of an INCOMPLETE wasm build, not an Avalonia bug. Without
        // WasmBuildNative=true (+ the WebAssembly workload) the SkiaSharp/HarfBuzz natives were
        // never linked into dotnet.native.wasm (so the first Skia call crashed) AND Avalonia's JS
        // modules were misplaced at the wwwroot root instead of _framework/. A proper native build
        // (see FEBuilderGBA.Browser.csproj + pages.yml) puts avalonia.js / storage.js in _framework/
        // next to dotnet.js, where Avalonia's DEFAULT FrameworkAssetPathResolver (./avalonia.js,
        // resolved by JSHost relative to _framework/) finds them — so NO resolver override is needed.
        // Verified end-to-end by the headless boot smoke test in pages.yml.
        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<global::FEBuilderGBA.Avalonia.App>()
            // wasm has NO system fonts — embed Inter (Latin) so text renders (HarfBuzz shapes it),
            // and register an embedded Noto Sans CJK SC subset as a per-codepoint FALLBACK so
            // Japanese game text and the ja/zh UI don't render as tofu (#1890). Desktop/Android/iOS
            // fall back to OS CJK fonts; wasm has none.
            .WithInterFont()
            .With(CreateBrowserFontManagerOptions());

    /// <summary>
    /// <see cref="FontManagerOptions"/> for the wasm head: register the embedded Noto Sans CJK SC
    /// subset (<c>Assets/Fonts/NotoSansCJKsc-Subset.otf</c>) as a per-codepoint font FALLBACK for the
    /// CJK glyphs Inter lacks (#1890). This is the same mechanism the desktop uses
    /// (<c>FEBuilderGBA.Avalonia/Program.CreateFontManagerOptions</c>) but with an EMBEDDED font,
    /// because the browser sandbox has no system fonts to fall back to. Factored out (internal) so a
    /// test can assert the fallback is wired to the exact embedded family. <c>WithInterFont()</c>
    /// registers Inter via <c>ConfigureFonts</c> (not <c>With&lt;FontManagerOptions&gt;</c>), so adding
    /// these options composes cleanly and does not clobber the Inter default.
    /// </summary>
    internal static FontManagerOptions CreateBrowserFontManagerOptions()
        => new FontManagerOptions
        {
            // DefaultFamilyName is intentionally NOT set — Inter (from WithInterFont) stays the
            // default; FontFallbacks are consulted per codepoint only when the primary font lacks a
            // glyph. The '#Noto Sans CJK SC' suffix MUST match the font's internal family name (the
            // #1 silent-failure mode — guarded by BrowserCjkFontTests).
            FontFallbacks = new[]
            {
                new FontFallback
                {
                    FontFamily = new FontFamily(
                        "avares://FEBuilderGBA.Browser/Assets/Fonts/NotoSansCJKsc-Subset.otf#Noto Sans CJK SC"),
                },
            },
        };

    /// <summary>
    /// First-run config bootstrap for wasm: fetch the bundled <c>config.zip</c> over HTTP and
    /// extract it into a writable MEMFS dir via the pure <see cref="ZipAssetSource"/> +
    /// <see cref="AndroidConfigExtractorCore"/>, then point <c>App.BaseDirectoryOverride</c> there.
    /// Wrapped so any failure (missing zip, offline) logs to the browser console and the app still
    /// boots with default config rather than showing a blank page.
    /// </summary>
    static async Task TryLoadConfigAsync(string[] args)
    {
        try
        {
            // main.js passes document.baseURI as args[0]. If it is missing or not an absolute URI
            // we cannot resolve a relative fetch, so skip config load (non-fatal) rather than throw
            // — `new Uri("./", UriKind.Absolute)` would throw and defeat the graceful fallback.
            if (args is not { Length: > 0 } || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? baseUri))
            {
                Console.WriteLine("[FEBuilderGBA] no absolute base URI arg — booting without config.");
                return;
            }

            using var http = new HttpClient { BaseAddress = baseUri };
            byte[] zipBytes = await http.GetByteArrayAsync("config.zip");

            using var ms = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            // MEMFS is writable in the .NET wasm runtime; extract under a dedicated app-private root.
            const string targetRoot = "/appdata";
            Directory.CreateDirectory(targetRoot);
            // Stamp derived from the zip size (NOT a constant) so a changed config.zip forces a
            // clean re-extract. The browser MEMFS is per-session (cleared on reload), so extraction
            // normally runs fresh each load anyway; this guards a same-session re-entry / future
            // persistent-FS use.
            string version = "wasm-" + zipBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AndroidConfigExtractorCore.ExtractionResult result =
                AndroidConfigExtractorCore.EnsureExtracted(new ZipAssetSource(archive), targetRoot, version);

            global::FEBuilderGBA.Avalonia.App.BaseDirectoryOverride = targetRoot;
            Console.WriteLine($"[FEBuilderGBA] config {result} -> {targetRoot} ({zipBytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            // Non-fatal — boot with default/empty config; the shell still renders.
            Console.WriteLine("[FEBuilderGBA] config load skipped (booting with defaults): " + ex);
        }
    }
}
