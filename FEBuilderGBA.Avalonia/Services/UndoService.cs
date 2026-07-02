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
        // virtual: allow test-double spies (UndoServiceSpy) to record Begin/Commit/Rollback ordering. (#536)
        public virtual void Begin(string name)
        {
            var undo = CoreState.Undo;
            if (undo == null) return;

            _currentUndoData = undo.NewUndoData(name);
            _scope = ROM.BeginUndoScope(_currentUndoData);
        }

        /// <summary>Commit the undo group to the undo buffer.</summary>
        public virtual void Commit()
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
        public virtual void Rollback()
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

        /// <summary>
        /// Push an EXTERNALLY-created <see cref="Undo.UndoData"/> onto the undo buffer
        /// and refresh the dirty indicator — for callers that cannot use the
        /// thread-local ambient <see cref="Begin"/>/<see cref="Commit"/> scope because
        /// the ROM writes happen on a background thread (e.g. the Event Assembler tool
        /// runs the compile+insert inside <c>Task.Run</c> and passes the UndoData
        /// explicitly to the Core helper, which records into it directly). No ambient
        /// scope is opened, so an unrelated UI-thread write can't leak into this group.
        /// Returns true if anything was recorded (and thus pushed).
        /// </summary>
        public virtual bool CommitExternal(Undo.UndoData undoData)
        {
            if (undoData == null || CoreState.Undo == null) return false;
            if (undoData.list.Count == 0) return false;
            CoreState.Undo.Push(undoData);
            NotifyUnsavedChanges();
            return true;
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
        public static void NotifyUnsavedChanges()
        {
            // The dirty indicator lives on the MainWindow (an Avalonia UI object).
            // Commit()/Rollback() are public and may run off the UI thread (e.g.
            // headless tests, future async callers); touching the Window off the
            // UI thread throws "Call from invalid thread". Marshal to the UI
            // thread when we are not already on it.
            var dispatcher = global::Avalonia.Threading.Dispatcher.UIThread;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Post(NotifyUnsavedChanges);
                return;
            }
            if (WindowManager.Instance.MainWindow?.DataContext is ViewModels.MainWindowViewModel vm)
                vm.HasUnsavedChanges = CoreState.Undo?.IsModified ?? false;
        }
    }
}
