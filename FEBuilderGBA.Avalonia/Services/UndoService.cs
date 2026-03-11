using System;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Wrapper around Core Undo class for Avalonia editors.
    /// Uses ROM.BeginUndoScope() so all rom.write_*() calls within a
    /// Begin/Commit block are automatically undo-tracked.
    /// </summary>
    public class UndoService
    {
        Undo.UndoData? _currentUndoData;
        IDisposable? _scope;

        /// <summary>Begin a new undo group. All ROM writes until Commit/Rollback are tracked.</summary>
        public void Begin(string name)
        {
            var undo = CoreState.Undo;
            if (undo == null) return;

            _currentUndoData = undo.NewUndoData(name);
            _scope = ROM.BeginUndoScope(_currentUndoData);
        }

        /// <summary>Commit the undo group to the undo buffer.</summary>
        public void Commit()
        {
            _scope?.Dispose();
            _scope = null;

            if (_currentUndoData != null && CoreState.Undo != null)
            {
                if (_currentUndoData.list.Count > 0)
                    CoreState.Undo.Push(_currentUndoData);
            }
            _currentUndoData = null;
        }

        /// <summary>Rollback the current undo group (discard changes).</summary>
        public void Rollback()
        {
            _scope?.Dispose();
            _scope = null;

            if (_currentUndoData != null && CoreState.Undo != null)
            {
                if (_currentUndoData.list.Count > 0)
                {
                    CoreState.Undo.Push(_currentUndoData);
                    CoreState.Undo.RunUndo();
                }
            }
            _currentUndoData = null;
        }

        /// <summary>Whether there's an active undo group.</summary>
        public bool HasPendingUndo => _currentUndoData != null;
    }
}
