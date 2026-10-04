namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>Desktop top-level wrapper for embeddable editor content.</summary>
    public sealed class EditorHostWindow : TitleTranslatedWindow
    {
        public EditorHostWindow(IEmbeddableEditor editor)
        {
            var descriptor = editor.Descriptor;
            SetTitle(descriptor.Title, editor.TitleKey == null ? null : descriptor.Title);
            Width = descriptor.PreferredWidth;
            Height = descriptor.PreferredHeight;
            MinWidth = descriptor.MinWidth;
            MinHeight = descriptor.MinHeight;
            CanResize = descriptor.CanResize;
            WindowStartupLocation = descriptor.StartupLocation;
            SizeToContent = descriptor.SizeToContent;
            Content = editor;
        }
    }
}
