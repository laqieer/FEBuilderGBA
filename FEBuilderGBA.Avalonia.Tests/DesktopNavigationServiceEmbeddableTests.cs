using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class DesktopNavigationServiceEmbeddableTests
{
    [AvaloniaFact]
    public void Open_wraps_embeddable_and_returns_content_instance()
    {
        TestEmbeddableEditor.Reset();
        var svc = new DesktopNavigationService();
        var editor = svc.Open<TestEmbeddableEditor>();

        Assert.IsType<TestEmbeddableEditor>(editor);
        Assert.Same(editor, svc.FindOpen<TestEmbeddableEditor>());
        Assert.IsType<EditorHostWindow>(svc.OpenAsTopLevel<TestEmbeddableEditor>());
        Assert.Equal(1, TestEmbeddableEditor.CreatedCount);
        svc.CloseAll();
    }

    [AvaloniaFact]
    public void Reopen_reactivates_cached_embeddable_without_duplicate_instance()
    {
        TestEmbeddableEditor.Reset();
        var svc = new DesktopNavigationService();
        var first = svc.Open<TestEmbeddableEditor>();
        var second = svc.Open<TestEmbeddableEditor>();

        Assert.Same(first, second);
        Assert.Equal(1, TestEmbeddableEditor.CreatedCount);
        svc.CloseAll();
    }

    [AvaloniaFact]
    public void CloseRequested_closes_host_and_drops_cache()
    {
        var svc = new DesktopNavigationService();
        var editor = svc.Open<TestEmbeddableEditor>();
        editor.RequestClose();

        Assert.Null(svc.FindOpen<TestEmbeddableEditor>());
    }

    [AvaloniaFact]
    public void CloseAll_closes_embeddable_hosts_and_reopen_creates_fresh_content()
    {
        TestEmbeddableEditor.Reset();
        var svc = new DesktopNavigationService();
        var first = svc.Open<TestEmbeddableEditor>();
        svc.CloseAll();
        var second = svc.Open<TestEmbeddableEditor>();

        Assert.NotSame(first, second);
        Assert.Equal(2, TestEmbeddableEditor.CreatedCount);
        svc.CloseAll();
    }

    [AvaloniaFact]
    public void Navigate_targets_returned_embeddable_content()
    {
        var svc = new DesktopNavigationService();
        var editor = svc.Navigate<TestEmbeddableEditor>(0x1234);

        Assert.Equal(1, editor.NavigateCalls);
        Assert.Equal(0x1234u, editor.LastAddress);
        svc.CloseAll();
    }

    [AvaloniaFact]
    public async Task PickFromEditor_uses_embeddable_content_and_returns_selection()
    {
        var svc = new DesktopNavigationService();
        var task = svc.PickFromEditor<PickableEmbeddableEditor>();
        var editor = Assert.IsType<PickableEmbeddableEditor>(TestEmbeddableEditor.LastInstance);

        Assert.True(editor.PickModeEnabled);
        editor.Confirm(new PickResult(3, 0x44, "picked"));

        var result = await task;
        Assert.NotNull(result);
        Assert.Equal(3, result!.Index);
    }

    [AvaloniaFact]
    public void Legacy_window_path_still_returns_the_window_itself()
    {
        var svc = new DesktopNavigationService();
        var window = svc.Open<DesktopNavigationServiceActiveEditorTests.EditorDoubleA>();
        Assert.IsAssignableFrom<Window>(window);
        Assert.Same(window, svc.OpenAsTopLevel<DesktopNavigationServiceActiveEditorTests.EditorDoubleA>());
        svc.CloseAll();
    }
}
