using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class MoveCostEditorViewEmbeddableTests
{
    [AvaloniaFact]
    public void MoveCostEditorView_is_embeddable_usercontrol_not_window()
    {
        var view = new MoveCostEditorView();
        var embeddable = Assert.IsAssignableFrom<IEmbeddableEditor>(view);
        Assert.IsAssignableFrom<UserControl>(view);
        Assert.IsNotType<Window>(view);
        Assert.Equal("Move Cost Editor", embeddable.Descriptor.Title);
        Assert.Equal(1601, embeddable.Descriptor.PreferredWidth);
        Assert.Equal(800, embeddable.Descriptor.PreferredHeight);
        Assert.True(embeddable.Descriptor.SizeToContent);
    }

    [AvaloniaFact]
    public void Desktop_open_returns_content_and_top_level_is_host()
    {
        var svc = new DesktopNavigationService();
        var view = svc.Open<MoveCostEditorView>();
        var top = svc.OpenAsTopLevel<MoveCostEditorView>();

        Assert.Same(view, svc.FindOpen<MoveCostEditorView>());
        var host = Assert.IsType<EditorHostWindow>(top);
        Assert.Same(view, host.Content);
        svc.CloseAll();
    }

    [AvaloniaFact]
    public void Android_open_pushes_MoveCostEditorView_directly()
    {
        INavigationHost host = new AndroidNavigationService();
        var service = (AndroidNavigationService)host;
        service.SetRoot(new TextBlock { Text = "root" });

        var view = service.Open<MoveCostEditorView>();

        Assert.Same(view, host.CurrentContent);
        Assert.IsNotType<Window>(host.CurrentContent);
        Assert.Equal("Move Cost Editor", host.CurrentTitle);
    }
}
