using System;

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

        /// <summary>Select the first item in the editor list. Default does nothing.</summary>
        void SelectFirstItem() { }
    }

    /// <summary>Result returned from a pick-and-return operation.</summary>
    public record PickResult(int Index, uint Address, string Name);

    /// <summary>
    /// Interface for editors that support pick-and-return (modal selection).
    /// User double-clicks or presses Enter to confirm selection.
    /// </summary>
    public interface IPickableEditor : IEditorView
    {
        event Action<PickResult>? SelectionConfirmed;
        void EnablePickMode();
    }
}
