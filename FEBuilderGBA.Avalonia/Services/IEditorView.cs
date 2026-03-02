namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Interface for editor views that can navigate to specific addresses.
    /// </summary>
    public interface IEditorView
    {
        string ViewTitle { get; }
        bool IsLoaded { get; }
        void NavigateTo(uint address);
    }
}
