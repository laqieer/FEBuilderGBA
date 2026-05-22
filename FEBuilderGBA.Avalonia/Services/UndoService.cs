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
                {
                    CoreState.Undo.Push(_currentUndoData);
                    NotifyUnsavedChanges();
                }
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

        /// <summary>
        /// Access the current undo data so callers that talk directly to Core
        /// (e.g. ItemUsagePointerCore.Switch2Expands which signs `Undo.UndoData`
        /// for #440) can pass the active scope through. Returns null when no
        /// scope is open.
        /// </summary>
        public Undo.UndoData? GetActiveUndoData() => _currentUndoData;

        /// <summary>Notify the main window of the current unsaved-changes state.</summary>
        static void NotifyUnsavedChanges()
        {
            if (WindowManager.Instance.MainWindow?.DataContext is ViewModels.MainWindowViewModel vm)
                vm.HasUnsavedChanges = CoreState.Undo?.IsModified ?? false;
        }
    }
}
