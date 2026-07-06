using System.Runtime.CompilerServices;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

static class MoveCostEditorViewTestHostExtensions
{
    static readonly ConditionalWeakTable<MoveCostEditorView, EditorHostWindow> Hosts = new();

    public static void Show(this MoveCostEditorView view)
    {
        var host = new EditorHostWindow(view);
        Hosts.Remove(view);
        Hosts.Add(view, host);
        host.Show();
    }

    public static void Close(this MoveCostEditorView view)
    {
        if (Hosts.TryGetValue(view, out var host))
        {
            host.Close();
            Hosts.Remove(view);
        }
    }
}
