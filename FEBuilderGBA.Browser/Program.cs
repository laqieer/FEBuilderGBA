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

        await BuildAvaloniaApp().StartBrowserAppAsync("out");
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
            // main.js passes document.baseURI as args[0], so a relative fetch resolves under the
            // GitHub Pages project base href (e.g. https://laqieer.github.io/FEBuilderGBA/).
            string baseUri = (args is { Length: > 0 } && !string.IsNullOrEmpty(args[0])) ? args[0] : "./";
            using var http = new HttpClient { BaseAddress = new Uri(baseUri, UriKind.Absolute) };
            byte[] zipBytes = await http.GetByteArrayAsync("config.zip");

            using var ms = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            // MEMFS is writable in the .NET wasm runtime; extract under a dedicated app-private root.
            const string targetRoot = "/appdata";
            Directory.CreateDirectory(targetRoot);
            AndroidConfigExtractorCore.ExtractionResult result =
                AndroidConfigExtractorCore.EnsureExtracted(new ZipAssetSource(archive), targetRoot, "wasm-1.0");

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
