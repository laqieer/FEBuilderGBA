using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Desktop top-level wrapper for embeddable editor content.</summary>
    public sealed class EditorHostWindow : Window
    {
        public EditorHostWindow(IEmbeddableEditor editor)
        {
            var descriptor = editor.Descriptor;
            Title = descriptor.Title;
            Width = descriptor.PreferredWidth;
            Height = descriptor.PreferredHeight;
            MinWidth = descriptor.MinWidth;
            MinHeight = descriptor.MinHeight;
            CanResize = descriptor.CanResize;
            WindowStartupLocation = descriptor.StartupLocation;
            if (descriptor.SizeToContent)
                SizeToContent = SizeToContent.WidthAndHeight;
            Content = editor;
        }
    }
}
