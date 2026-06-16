using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// One row in the undo-history list. Mirrors a single entry rendered by
    /// WinForms <c>ToolUndoForm.Redraw</c> via <see cref="Undo.MakeName(int,bool)"/>.
    /// </summary>
    public class UndoEntryRowViewModel : ViewModelBase
    {
        /// <summary>
        /// The undo-buffer position this row represents. Ranges from
        /// <c>UndoBuffer.Count</c> (the newest / "latest" sentinel row) down to 0.
        /// </summary>
        public int Position { get; }

        /// <summary>Display name without the "-&gt;" current marker (rendered separately as <see cref="IsCurrent"/>).</summary>
        public string DisplayName { get; }

        /// <summary>True when this row is the active undo cursor (<see cref="Undo.Postion"/>).</summary>
        public bool IsCurrent { get; }

        /// <summary>Localized timestamp, or empty for the "latest" sentinel row.</summary>
        public string TimeText { get; }

        /// <summary>Number of modified address ranges in this snapshot, or "-" for the sentinel row.</summary>
        public string ChangeCountText { get; }

        public UndoEntryRowViewModel(int position, string displayName, bool isCurrent, string timeText, string changeCountText)
        {
            Position = position;
            DisplayName = displayName;
            IsCurrent = isCurrent;
            TimeText = timeText;
            ChangeCountText = changeCountText;
        }
    }

    /// <summary>
    /// View model for the Undo history tool (port of WinForms <c>ToolUndoForm</c>, #1190).
    /// Presents <see cref="Undo.UndoBuffer"/> newest-first, lets the user roll back to or
    /// test-play a selected snapshot. All ROM mutation goes through the existing Core
    /// <see cref="Undo"/> API — no new Core surface is introduced.
    /// </summary>
    public class ToolUndoViewModel : ViewModelBase
    {
        bool _isLoaded;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>The history rows, newest entry first (matches WinForms "上が最新").</summary>
        public ObservableCollection<UndoEntryRowViewModel> Entries { get; } = new();

        /// <summary>
        /// The display index (0-based, top = newest) that corresponds to the current
        /// undo cursor, so the view can pre-select it. Mirrors WinForms
        /// <c>UndoBuffer.Count - Postion</c>. -1 when there is no buffer.
        /// </summary>
        public int CurrentDisplayIndex { get; private set; } = -1;

        /// <summary>
        /// Rebuild <see cref="Entries"/> from <see cref="CoreState.Undo"/>. Safe to call
        /// repeatedly; returns the list so the view can drive an items source if it prefers.
        /// Guards a missing ROM/Undo by producing an empty list.
        /// </summary>
        public List<UndoEntryRowViewModel> LoadList()
        {
            Entries.Clear();
            CurrentDisplayIndex = -1;

            Undo? undo = CoreState.Undo;
            if (undo == null)
            {
                IsLoaded = true;
                return new List<UndoEntryRowViewModel>();
            }

            int count = undo.UndoBuffer.Count;
            // WinForms iterates i from count down to 0 (newest first). MakeName(count)
            // is the "latest" sentinel; MakeName(i<count) renders that snapshot.
            for (int pos = count; pos >= 0; pos--)
            {
                bool isCurrent = pos == undo.Postion;
                string name = undo.MakeName(pos, showAllowMark: false);

                string timeText = "";
                string changeText = "-";
                if (pos < count)
                {
                    Undo.UndoData ud = undo.UndoBuffer[pos];
                    timeText = ud.time.ToString();
                    changeText = ud.list.Count.ToString();
                }

                var row = new UndoEntryRowViewModel(pos, name, isCurrent, timeText, changeText);
                if (isCurrent)
                    CurrentDisplayIndex = Entries.Count;
                Entries.Add(row);
            }

            IsLoaded = true;
            return new List<UndoEntryRowViewModel>(Entries);
        }

        /// <summary>
        /// Resolve the undo-buffer position to roll back to for a given selected display
        /// index, applying the same guards as WinForms <c>RollbackThisVersion</c>:
        /// returns -1 when the selection is invalid or already the current position
        /// (a no-op rollback).
        /// </summary>
        public int RollbackPositionFor(int selectedDisplayIndex)
        {
            Undo? undo = CoreState.Undo;
            if (undo == null) return -1;
            if (selectedDisplayIndex < 0 || selectedDisplayIndex >= Entries.Count) return -1;

            int rollbackPos = Entries[selectedDisplayIndex].Position;
            if (rollbackPos < 0) return -1;
            if (rollbackPos == undo.Postion) return -1; // already there -> no-op (WinForms parity)
            return rollbackPos;
        }

        /// <summary>
        /// Resolve the position for a test-play of the selected row. Unlike rollback,
        /// WinForms test-play does NOT skip the current position, so only the
        /// validity guards apply.
        /// </summary>
        public int TestPlayPositionFor(int selectedDisplayIndex)
        {
            if (selectedDisplayIndex < 0 || selectedDisplayIndex >= Entries.Count) return -1;
            int pos = Entries[selectedDisplayIndex].Position;
            return pos < 0 ? -1 : pos;
        }

        /// <summary>
        /// The version name shown in the rollback confirmation dialog.
        /// Mirrors WinForms <c>MakeName(rollbackPOS, false)</c>.
        /// </summary>
        public string MakeVersionName(int pos)
        {
            Undo? undo = CoreState.Undo;
            if (undo == null) return "";
            if (pos < 0 || pos > undo.UndoBuffer.Count) return "";
            return undo.MakeName(pos, showAllowMark: false);
        }

        /// <summary>Roll the ROM back to the given undo position. Null/ROM-safe.</summary>
        public void DoRollback(int pos)
        {
            Undo? undo = CoreState.Undo;
            if (undo == null || CoreState.ROM == null) return;
            if (pos < 0) return;
            undo.Rollback(pos);
        }

        /// <summary>
        /// Save a clone of the ROM at the given undo position and hand it to the
        /// emulator (via <see cref="Undo.OnRunEmulator"/>, a no-op when unwired).
        /// Null/ROM-safe.
        /// </summary>
        public void DoTestPlay(int pos)
        {
            Undo? undo = CoreState.Undo;
            if (undo == null || CoreState.ROM == null) return;
            if (pos < 0) return;
            undo.TestPlayThisVersion(pos);
        }
    }
}
