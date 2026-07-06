using System;

namespace FEBuilderGBA.Avalonia.Services
{
    public record EditorDescriptor(
        string Title,
        double PreferredWidth,
        double PreferredHeight,
        bool SizeToContent = false,
        double MinWidth = 0,
        double MinHeight = 0,
        bool CanBeModal = false);
}
