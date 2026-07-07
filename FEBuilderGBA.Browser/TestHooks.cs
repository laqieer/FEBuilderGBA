#if E2E_HOOKS
using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using FEBuilderGBA;
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
}
#endif
