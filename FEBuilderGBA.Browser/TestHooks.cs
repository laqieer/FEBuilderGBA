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
    [JSExport]
    public static async Task<bool> LoadRomBase64(string base64)
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

    // #1998: bounded Map Editor layout probe for the CI browser smoke test — returns only layout
    // METRICS (extents/viewports/bounds) as JSON, never ROM contents. When the currently-open editor
    // is the Map Editor, MapImageControl is populated with a small deterministic synthetic RGBA
    // pattern (no ROM/license data involved) purely so its inner MapCanvasScroller genuinely
    // overflows at 1x zoom, letting the smoke test assert real (non-degenerate) overflow behavior
    // the same way FEBuilderGBA.Avalonia.Tests.MapEditorButtonReadabilityTests does headlessly.
    [JSExport]
    public static string MapEditorLayoutMetrics()
    {
        try
        {
            string? json = null;

            void Compute()
            {
                if (WindowManager.Instance.Service is not INavigationHost host
                    || host.CurrentContent is not Control editor)
                {
                    json = "{}";
                    return;
                }

                var mapImage = editor.GetVisualDescendants()
                    .OfType<GbaImageControl>()
                    .FirstOrDefault(c => c.Name == "MapImageControl");
                if (mapImage is not null)
                {
                    // Large enough to genuinely overflow MapCanvasPanel's inner scroller
                    // (MapCanvasScroller) regardless of viewport size, mirroring
                    // MapEditorButtonReadabilityTests' SyntheticMapSize.
                    const int size = 600;
                    byte[] rgba = new byte[size * size * 4];
                    for (int i = 0; i < size * size; i++)
                    {
                        rgba[i * 4 + 0] = 80;
                        rgba[i * 4 + 1] = 120;
                        rgba[i * 4 + 2] = 160;
                        rgba[i * 4 + 3] = 255;
                    }
                    mapImage.SetRgbaData(rgba, size, size);
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
