using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class EditorHostWindowTests
{
    [AvaloniaFact]
    public void Applies_descriptor_once_and_hosts_content_passthrough()
    {
        var editor = new TestEmbeddableEditor();
        var host = new EditorHostWindow(editor);

        Assert.Equal("Test Embeddable", host.Title);
        Assert.Equal(321, host.Width);
        Assert.Equal(123, host.Height);
        Assert.Equal(111, host.MinWidth);
        Assert.Equal(99, host.MinHeight);
        Assert.Equal(SizeToContent.Manual, host.SizeToContent);
        Assert.Same(editor, host.Content);
    }

    [AvaloniaFact]
    public void Applies_SizeToContent_when_descriptor_requests_it()
    {
        var editor = new MoveCostDescriptorDouble();
        var host = new EditorHostWindow(editor);
        Assert.Equal(SizeToContent.WidthAndHeight, host.SizeToContent);
    }

    sealed class MoveCostDescriptorDouble : TestEmbeddableEditor
    {
        public override EditorDescriptor Descriptor => new("Sized", 10, 20, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
    }
}

[Collection("WindowManagerSerial")]
public class EditorHostWindowTitleKeyTests
{
    [AvaloniaFact]
    public void Uses_explicit_title_key_instead_of_descriptor_title()
    {
        using var state = new GuiLocalizationState();
        state.ApplyLanguage("zh");

        var editor = new ExplicitTitleKeyEditor();
        var host = new EditorHostWindow(editor);

        Assert.Equal(R._("Item Editor"), host.Title);
        Assert.NotEqual(editor.Descriptor.Title, host.Title);
    }

    sealed class ExplicitTitleKeyEditor : TestEmbeddableEditor, IEmbeddableEditor
    {
        public override EditorDescriptor Descriptor => new("Literal descriptor", 100, 100);
        string? IEditorView.TitleKey => "Item Editor";
    }
}
