using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Undo history tool — parity with WinForms <c>ToolUndoForm</c> (#1190).
    /// Lists every undo snapshot (HEAD "最新版" down to position 0) and lets the
    /// user roll back the ROM to a chosen snapshot (and, where wired, test-play it).
    /// The Core undo instance is <see cref="CoreState.Undo"/>; the WinForms form
    /// uses the same <c>Undo.UndoBuffer</c> / <c>MakeName</c> / <c>Rollback(int)</c>
    /// / <c>TestPlayThisVersion(int)</c> API.
    /// </summary>
    public class ToolUndoViewModel : ViewModelBase
    {
        int _selectedPos = -1;
        bool _isLoaded;
        string _selectedInfo = "";
        bool _canRollback;
        bool _canTestPlay;

        /// <summary>The undo position of the current selection (-1 = none).</summary>
        public int SelectedPos { get => _selectedPos; set => SetField(ref _selectedPos, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Multi-line details of the selected snapshot, shown in the right panel.</summary>
        public string SelectedInfo { get => _selectedInfo; set => SetField(ref _selectedInfo, value); }
        /// <summary>True when the selection is a different, valid position to roll back to.</summary>
        public bool CanRollback { get => _canRollback; set => SetField(ref _canRollback, value); }
        public bool CanTestPlay { get => _canTestPlay; set => SetField(ref _canTestPlay, value); }

        // The undo position is encoded into AddrResult.addr as (pos + 1): the
        // AddressListControl keys selection on addr and AddrResult.isNULL() treats
        // addr==0 as null, so a +1 bias keeps position 0 selectable and unique.
        public static uint AddrFromPos(int pos) => (uint)(pos + 1);
        public static int PosFromAddr(uint addr) => (int)addr - 1;

        /// <summary>
        /// Build the history list newest-first: HEAD (pos == Count, "最新版") down to
        /// pos 0 — mirrors WinForms <c>ToolUndoForm.Redraw</c>. <c>MakeName</c> prefixes
        /// the CURRENT position with "->".
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            Undo undo = CoreState.Undo;
            if (undo == null) return result;

            for (int pos = undo.UndoBuffer.Count; pos >= 0; pos--)
                result.Add(new AddrResult(AddrFromPos(pos), undo.MakeName(pos), (uint)pos));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            IsLoaded = true;
            Undo undo = CoreState.Undo;
            if (undo == null)
            {
                SelectedPos = -1; SelectedInfo = ""; CanRollback = false; CanTestPlay = false;
                return;
            }

            int pos = PosFromAddr(addr);
            bool valid = pos >= 0 && pos <= undo.UndoBuffer.Count;
            SelectedPos = valid ? pos : -1;
            SelectedInfo = valid ? BuildInfo(undo, pos) : "";
            CanRollback = valid && pos != undo.Postion;   // can't roll back to where we already are
            CanTestPlay = valid;
        }

        static string BuildInfo(Undo undo, int pos)
        {
            bool current = pos == undo.Postion;
            if (pos == undo.UndoBuffer.Count)
                return $"最新版 / latest (HEAD)\nPosition: {pos} / {undo.UndoBuffer.Count}\nCurrent: {(current ? "yes" : "no")}";

            Undo.UndoData ud = undo.UndoBuffer[pos];
            int regions = ud.list?.Count ?? 0;
            string f5 = ud.is_f5test ? "\n[F5 test build]" : "";
            return $"{ud.name}\nTime: {ud.time}\nChanged regions: {regions}\nFile size: {ud.filesize} bytes{f5}\n"
                 + $"Position: {pos} / {undo.UndoBuffer.Count}\nCurrent: {(current ? "yes" : "no")}";
        }

        /// <summary>The confirm-dialog version label (no current-position marker), like WinForms MakeName(pos,false).</summary>
        public string MakeRollbackLabel(int pos)
        {
            Undo undo = CoreState.Undo;
            return undo == null ? "" : undo.MakeName(pos, false);
        }

        /// <summary>Roll back the ROM to <paramref name="pos"/>. Returns true if applied.</summary>
        public bool Rollback(int pos)
        {
            Undo undo = CoreState.Undo;
            if (undo == null) return false;
            if (pos < 0 || pos > undo.UndoBuffer.Count) return false;
            if (pos == undo.Postion) return false;   // no-op: already here
            undo.Rollback(pos);
            return true;
        }

        /// <summary>Test-play a snapshot (writes a .emulator ROM; launches the emulator only where wired).</summary>
        public void TestPlay(int pos)
        {
            Undo undo = CoreState.Undo;
            if (undo == null) return;
            if (pos < 0 || pos > undo.UndoBuffer.Count) return;
            undo.TestPlayThisVersion(pos);
        }
    }
}
