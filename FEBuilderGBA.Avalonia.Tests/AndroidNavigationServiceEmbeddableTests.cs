using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

public class AndroidNavigationServiceEmbeddableTests
{
    static AndroidNavigationService NewServiceWithRoot()
    {
        var service = new AndroidNavigationService();
        service.SetRoot(new TextBlock { Text = "root" });
        return service;
    }

    [AvaloniaFact]
    public void Open_embeddable_pushes_usercontrol_itself_without_window_factory()
    {
        TestEmbeddableEditor.Reset();
        INavigationHost host = NewServiceWithRoot();
        var service = (AndroidNavigationService)host;

        var editor = service.Open<TestEmbeddableEditor>();

        Assert.Same(editor, host.CurrentContent);
        Assert.IsNotType<Window>(host.CurrentContent);
        Assert.Equal(1, TestEmbeddableEditor.CreatedCount);
        Assert.Equal("Test Embeddable", host.CurrentTitle);
    }

    [AvaloniaFact]
    public void CloseRequested_pops_embeddable_page_and_clears_cache()
    {
        INavigationHost host = NewServiceWithRoot();
        var service = (AndroidNavigationService)host;
        var editor = service.Open<TestEmbeddableEditor>();

        editor.RequestClose();

        Assert.False(host.CanGoBack);
        Assert.Null(service.FindOpen<TestEmbeddableEditor>());
    }

    [AvaloniaFact]
    public void Reopen_resurfaces_same_embeddable_without_duplicate_stack_entry()
    {
        INavigationHost host = NewServiceWithRoot();
        var service = (AndroidNavigationService)host;
        var first = service.Open<TestEmbeddableEditor>();
        service.Open<CoverEmbeddableEditor>();

        var again = service.Open<TestEmbeddableEditor>();

        Assert.Same(first, again);
        Assert.Same(first, host.CurrentContent);
        host.GoBack();
        Assert.True(host.CanGoBack);
        host.GoBack();
        Assert.False(host.CanGoBack);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task PickFromEditor_embeddable_returns_selection_and_pops()
    {
        INavigationHost host = NewServiceWithRoot();
        var service = (AndroidNavigationService)host;
        var task = service.PickFromEditor<PickableEmbeddableEditor>(0x77);
        var editor = Assert.IsType<PickableEmbeddableEditor>(host.CurrentContent);

        Assert.True(editor.PickModeEnabled);
        Assert.Equal(0x77u, editor.LastAddress);
        editor.Confirm(new PickResult(9, 0x99, "nine"));

        var result = await task;
        Assert.NotNull(result);
        Assert.Equal(9, result!.Index);
        Assert.False(host.CanGoBack);
    }

    [AvaloniaFact]
    public void Cover_and_resurface_attach_loads_once()
    {
        var service = NewServiceWithRoot();
        INavigationHost host = service;
        var presenter = new ContentControl();
        host.StackChanged += () => presenter.Content = host.CurrentContent;
        var top = new Window { Content = presenter };
        top.Show();
        try
        {
            var editor = service.Open<TestEmbeddableEditor>();
            presenter.Content = host.CurrentContent;
            Assert.Equal(1, editor.LoadCalls);

            service.Open<CoverEmbeddableEditor>();
            presenter.Content = host.CurrentContent;
            service.Open<TestEmbeddableEditor>();
            presenter.Content = host.CurrentContent;

            Assert.Same(editor, host.CurrentContent);
            Assert.Equal(1, editor.LoadCalls);
        }
        finally
        {
            top.Close();
        }
    }
}
