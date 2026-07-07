using System.Runtime.CompilerServices;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

static class MoveCostEditorViewTestHostExtensions
{
    static readonly ConditionalWeakTable<IEmbeddableEditor, EditorHostWindow> Hosts = new();

    public static void Show(this IEmbeddableEditor view)
    {
        var host = new EditorHostWindow(view);
        Hosts.Remove(view);
        Hosts.Add(view, host);
        host.Show();
    }

    public static void Close(this IEmbeddableEditor view)
    {
        if (Hosts.TryGetValue(view, out var host))
        {
            host.Close();
            Hosts.Remove(view);
        }
    }

    public static WriteableBitmap CaptureRenderedFrame(this IEmbeddableEditor view)
    {
        if (!Hosts.TryGetValue(view, out var host))
        {
            host = new EditorHostWindow(view);
            Hosts.Add(view, host);
            host.Show();
        }
        return host.CaptureRenderedFrame();
    }

    public static EditorHostWindow GetHeadlessHost(this IEmbeddableEditor view)
    {
        if (!Hosts.TryGetValue(view, out var host))
        {
            host = new EditorHostWindow(view);
            Hosts.Add(view, host);
        }
        return host;
    }
}
