#if E2E_HOOKS
using System;
using System.Collections.Generic;
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
    //
    // Fail-closed contract (post-review fix — a review finding showed the previous `{}` returns
    // let a probe/runtime failure silently false-pass): EVERY situation where trustworthy metrics
    // cannot be produced — no navigation host/content, the wrong editor is open (named upper/map/
    // canvas controls not found), synthetic injection requested but MapImageControl is missing, an
    // unset compute result, or any caught exception — returns a JSON object with a non-empty,
    // bounded `error` string. This method must never return `{}` or a success-shaped payload with
    // missing/sentinel fields; callers (smoke.mjs) must treat ANY `error` field, or any missing/
    // non-finite metric, as a hard failure rather than "nothing to assert".
    [JSExport]
    public static string MapEditorLayoutMetrics(bool injectSyntheticMapPixels)
    {
        try
        {
            if (injectSyntheticMapPixels && !_lastLoadedRomWasSynthetic)
            {
                return SerializeError(
                    "MapEditorLayoutMetrics(injectSyntheticMapPixels: true) requested against a non-synthetic ROM load; " +
                    "synthetic map pixels are only permitted when LoadRomBase64 was called with isSynthetic: true.");
            }

            string? json = null;

            void Compute()
            {
                if (WindowManager.Instance.Service is not INavigationHost host
                    || host.CurrentContent is not Control editor)
                {
                    json = SerializeError("MapEditorLayoutMetrics: no active navigation host/content — no editor is open.");
                    return;
                }

                if (injectSyntheticMapPixels)
                {
                    var mapImage = editor.GetVisualDescendants()
                        .OfType<GbaImageControl>()
                        .FirstOrDefault(c => c.Name == "MapImageControl");
                    if (mapImage is null)
                    {
                        json = SerializeError(
                            $"MapEditorLayoutMetrics: synthetic injection requested but MapImageControl was not found " +
                            $"(current editor: \"{Bound(host.CurrentTitle)}\").");
                        return;
                    }

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

                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
                editor.UpdateLayout();

                var upperScroller = editor.GetVisualDescendants()
                    .OfType<ScrollViewer>().FirstOrDefault(c => c.Name == "MapUpperControlsScroller");
                var mapCanvas = editor.GetVisualDescendants()
                    .OfType<Border>().FirstOrDefault(c => c.Name == "MapCanvasPanel");
                var canvasScroller = editor.GetVisualDescendants()
                    .OfType<ScrollViewer>().FirstOrDefault(c => c.Name == "MapCanvasScroller");

                var missing = new List<string>();
                if (upperScroller is null) missing.Add("MapUpperControlsScroller");
                if (mapCanvas is null) missing.Add("MapCanvasPanel");
                if (canvasScroller is null) missing.Add("MapCanvasScroller");
                if (missing.Count > 0)
                {
                    json = SerializeError(
                        $"MapEditorLayoutMetrics: required control(s) not found: {string.Join(", ", missing)} " +
                        $"(current editor: \"{Bound(host.CurrentTitle)}\" — the wrong editor may be open).");
                    return;
                }

                var metrics = new
                {
                    title = host.CurrentTitle ?? "",
                    upperExtentHeight = upperScroller!.Extent.Height,
                    upperViewportHeight = upperScroller.Viewport.Height,
                    mapCanvasWidth = mapCanvas!.Bounds.Width,
                    mapCanvasHeight = mapCanvas.Bounds.Height,
                    canvasExtentWidth = canvasScroller!.Extent.Width,
                    canvasViewportWidth = canvasScroller.Viewport.Width,
                    canvasExtentHeight = canvasScroller.Extent.Height,
                    canvasViewportHeight = canvasScroller.Viewport.Height,
                };
                json = JsonSerializer.Serialize(metrics);
            }

            if (Dispatcher.UIThread.CheckAccess())
                Compute();
            else
                Dispatcher.UIThread.Invoke(Compute);

            return json ?? SerializeError("MapEditorLayoutMetrics: no result was produced.");
        }
        catch (Exception ex)
        {
            Log.Error("Browser TestHooks.MapEditorLayoutMetrics failed: ", ex.ToString());
            return SerializeError("MapEditorLayoutMetrics: an exception occurred while computing layout metrics; see server logs.");
        }
    }

    // Bounds a value embedded in a diagnostic `error` message so a runaway/unexpected editor title
    // (or any other caller-controlled string) can never blow up the returned payload.
    private static string Bound(string? value, int maxLen = 120)
    {
        string s = value ?? "";
        return s.Length > maxLen ? s.Substring(0, maxLen) + "…" : s;
    }

    private static string SerializeError(string message) => JsonSerializer.Serialize(new { error = message });
}
#endif
