using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
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

        await BuildAvaloniaApp().StartBrowserAppAsync("out", new BrowserPlatformOptions
        {
            // FIX #1867: the web app hung forever on the loading splash because Avalonia's runtime
            // requested _framework/avalonia.js (404). Avalonia loads its JS modules via
            // JSHost.ImportAsync(name, resolver(file)); the DEFAULT resolver is `file => "./" + file`,
            // and JSHost resolves that dynamic import() relative to the .NET runtime dir (_framework/),
            // i.e. _framework/avalonia.js. But our AppBundle publishes Avalonia's static web assets
            // (avalonia.js, storage.js) to the wwwroot ROOT — their RelativePath is
            // "$(WasmRuntimeAssetsLocation)/<file>" and WasmRuntimeAssetsLocation is empty in this
            // build — NOT _framework/. So climb exactly one segment out of _framework/ to app-root,
            // where they actually are. This stays fully relative, so it is correct at BOTH the GitHub
            // Pages sub-path (/FEBuilderGBA/_framework/../avalonia.js -> /FEBuilderGBA/avalonia.js) and
            // a local `/` run. It also repairs storage.js (file dialogs), which 404s the same way today.
            //
            // CAVEAT: this is valid ONLY for the two import()-loaded modules (avalonia.js, storage.js).
            // sw.js is consumed via navigator.serviceWorker.register(), which resolves against the
            // DOCUMENT base (not _framework/), so "../sw.js" would point at the domain root. Service
            // worker registration is opt-in and OFF by default (RegisterAvaloniaServiceWorker=false) —
            // do NOT enable it without revisiting this. Canonical follow-up: set
            // WasmRuntimeAssetsLocation=_framework so the assets land next to dotnet.js and Avalonia's
            // default resolver works unmodified. Guarded by the headless boot smoke test in pages.yml.
            FrameworkAssetPathResolver = fileName => "../" + fileName,
        });
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<global::FEBuilderGBA.Avalonia.App>()
            // wasm has NO system fonts — embed Inter so text renders (HarfBuzz shapes it).
            .WithInterFont();

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
