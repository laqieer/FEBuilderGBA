using global::Avalonia.Controls;
using System;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Host metadata supplied by an <see cref="IEmbeddableEditor"/>. Desktop uses
    /// it once when constructing the generic <see cref="EditorHostWindow"/>;
    /// single-view hosts use the same editor content directly as a page.
    /// </summary>
    public record EditorDescriptor(
        string Title,
        double PreferredWidth,
        double PreferredHeight,
        bool SizeToContent = false,
        double MinWidth = 0,
        double MinHeight = 0,
        bool CanBeModal = false,
        bool CanResize = true,
        WindowStartupLocation StartupLocation = WindowStartupLocation.CenterOwner);
}
