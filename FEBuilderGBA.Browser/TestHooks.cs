#if E2E_HOOKS
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

public static partial class TestHooks
{
    // #1998 follow-up: tracks whether the most recently loaded ROM was explicitly declared
    // synthetic by the caller (SMOKE_ROM=synthetic). MapEditorLayoutMetrics's synthetic-pixel
    // injection is gated on this flag so a real-ROM smoke run can never have its authentic
    // rendered map/palette pixels silently overwritten.
    private static bool _lastLoadedRomWasSynthetic;

    [JSExport]
    public static async Task<bool> LoadRomBase64(string base64, bool isSynthetic)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            var rom = new ROM();
            var (ok, _) = await rom.LoadFromStreamAsync(new MemoryStream(bytes), "e2e.gba");
            if (!ok)
                return false;

            RomFileService.InitializeLoadedRom(rom);
            MainView.RefreshForLoadedRomForTest();
            _lastLoadedRomWasSynthetic = isSynthetic;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Browser TestHooks.LoadRomBase64 failed: ", ex.ToString());
            return false;
        }
    }

    [JSExport]
    public static string OpenEditor(string key)
    {
        try
        {
            return MainView.OpenLauncherEntryForTest(key) ?? "";
        }
        catch (Exception ex)
        {
            Log.Error("Browser TestHooks.OpenEditor failed: ", ex.ToString());
            return "";
        }
    }

    [JSExport]
    public static string CurrentEditorTitle()
    {
        try
        {
            return WindowManager.Instance.Service is INavigationHost host
                ? host.CurrentTitle ?? ""
                : "";
        }
        catch (Exception ex)
        {
            Log.Error("Browser TestHooks.CurrentEditorTitle failed: ", ex.ToString());
            return "";
        }
    }

    [JSExport]
    public static bool CurrentEditorBodyRendered()
    {
        try
        {
            bool Check()
            {
                if (WindowManager.Instance.Service is not INavigationHost host
                    || host.CurrentContent is not Control { Bounds.Width: > 0, Bounds.Height: > 0 } editor)
                    return false;

                foreach (var descendant in editor.GetVisualDescendants())
                {
                    if (descendant is Control { Bounds.Width: > 0, Bounds.Height: > 0 })
                        return true;
                }

                return false;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                return Check();
            }

            return Dispatcher.UIThread.Invoke(() =>
            {
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                return Check();
            });
        }
        catch (Exception ex)
        {
            Log.Error("Browser TestHooks.CurrentEditorBodyRendered failed: ", ex.ToString());
            return false;
        }
    }

    // #1998 follow-up: bounded Map Editor layout probe for the CI browser smoke test — returns only
    // layout METRICS (extents/viewports/bounds) as JSON, never ROM contents. Synthetic map-pixel
    // injection is now OPT-IN via `injectSyntheticMapPixels` (previously this always overwrote
    // MapImageControl's pixels unconditionally, which meant a real-ROM smoke run could never prove
    // its own authentic rendered map — see issue #1998 follow-up plan). Callers must only pass
    // `true` when the loaded ROM was itself declared synthetic (SMOKE_ROM=synthetic); passing `true`
    // against a real loaded ROM is treated as an inconsistent request and hard-fails with a JSON
    // `error` field instead of silently mutating real ROM-derived pixels.
    [JSExport]
    public static string MapEditorLayoutMetrics(bool injectSyntheticMapPixels)
    {
        try
        {
            if (injectSyntheticMapPixels && !_lastLoadedRomWasSynthetic)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "MapEditorLayoutMetrics(injectSyntheticMapPixels: true) requested against a non-synthetic ROM load; " +
                             "synthetic map pixels are only permitted when LoadRomBase64 was called with isSynthetic: true."
                });
            }

            string? json = null;

            void Compute()
            {
                if (WindowManager.Instance.Service is not INavigationHost host
                    || host.CurrentContent is not Control editor)
                {
                    json = "{}";
                    return;
                }

                if (injectSyntheticMapPixels)
                {
                    var mapImage = editor.GetVisualDescendants()
                        .OfType<GbaImageControl>()
                        .FirstOrDefault(c => c.Name == "MapImageControl");
                    if (mapImage is not null)
                    {
                        // Large enough (non-square, >= ~2000x1000) to genuinely overflow
                        // MapCanvasPanel's inner scroller (MapCanvasScroller) on BOTH axes even at
                        // the 1920x852 acceptance viewport, mirroring
                        // MapEditorButtonReadabilityTests' SyntheticMapSize intent but sized for the
                        // browser smoke test's real (non-headless-fixture) viewport dimensions.
                        const int width = 2200;
                        const int height = 1200;
                        byte[] rgba = new byte[width * height * 4];
                        for (int i = 0; i < width * height; i++)
                        {
                            rgba[i * 4 + 0] = 80;
                            rgba[i * 4 + 1] = 120;
                            rgba[i * 4 + 2] = 160;
                            rgba[i * 4 + 3] = 255;
                        }
                        mapImage.SetRgbaData(rgba, width, height);
                    }
                }

                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                editor.UpdateLayout();

                var upperScroller = editor.GetVisualDescendants()
                    .OfType<ScrollViewer>().FirstOrDefault(c => c.Name == "MapUpperControlsScroller");
                var mapCanvas = editor.GetVisualDescendants()
                    .OfType<Border>().FirstOrDefault(c => c.Name == "MapCanvasPanel");
                var canvasScroller = editor.GetVisualDescendants()
                    .OfType<ScrollViewer>().FirstOrDefault(c => c.Name == "MapCanvasScroller");

                var metrics = new
                {
                    title = host.CurrentTitle ?? "",
                    upperExtentHeight = upperScroller?.Extent.Height ?? -1,
                    upperViewportHeight = upperScroller?.Viewport.Height ?? -1,
                    mapCanvasWidth = mapCanvas?.Bounds.Width ?? -1,
                    mapCanvasHeight = mapCanvas?.Bounds.Height ?? -1,
                    canvasExtentWidth = canvasScroller?.Extent.Width ?? -1,
                    canvasViewportWidth = canvasScroller?.Viewport.Width ?? -1,
                    canvasExtentHeight = canvasScroller?.Extent.Height ?? -1,
                    canvasViewportHeight = canvasScroller?.Viewport.Height ?? -1,
                };
                json = JsonSerializer.Serialize(metrics);
            }

            if (Dispatcher.UIThread.CheckAccess())
                Compute();
            else
                Dispatcher.UIThread.Invoke(Compute);

            return json ?? "{}";
        }
        catch (Exception ex)
        {
            Log.Error("Browser TestHooks.MapEditorLayoutMetrics failed: ", ex.ToString());
            return "{}";
        }
    }
}
#endif
