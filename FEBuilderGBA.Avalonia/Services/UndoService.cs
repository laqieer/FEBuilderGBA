namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Wrapper around Core Undo class for Avalonia editors.
    /// </summary>
    public class UndoService
    {
        Undo? _currentUndo;

        /// <summary>Begin a new undo group.</summary>
        public Undo Begin()
        {
            _currentUndo = new Undo();
            return _currentUndo;
        }

        /// <summary>Rollback the last undo operation via CoreState.Undo.</summary>
        public void Rollback()
        {
            CoreState.Undo?.RunUndo();
            _currentUndo = null;
        }

        /// <summary>Commit (discard undo data — changes are permanent until Save).</summary>
        public void Commit()
        {
            _currentUndo = null;
        }

        /// <summary>Whether there's an active undo group.</summary>
        public bool HasPendingUndo => _currentUndo != null;
    }
}
