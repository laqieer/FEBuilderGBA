using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Undo history tool (#1190, port of WinForms <c>ToolUndoForm</c>). Lists
    /// <see cref="Undo.UndoBuffer"/> newest-first and lets the user revert to or
    /// test-play a selected snapshot. Rollback is gated behind the existing
    /// <see cref="ToolUndoPopupDialogView"/> confirmation (WinForms parity).
    /// </summary>
    public partial class ToolUndoView : TranslatedWindow, IEditorView
    {
        readonly ToolUndoViewModel _vm = new();

        public string ViewTitle => "Undo History Viewer";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolUndoView()
        {
            InitializeComponent();
            DataContext = _vm;
            // WinForms refreshes on both Load and Activated so the list never
            // goes stale while the window stays open (UndoForm_Load/_Activated).
            Opened += (_, _) => Refresh();
            Activated += (_, _) => Refresh();
        }

        void Refresh()
        {
            try
            {
                _vm.LoadList();
                if (_vm.CurrentDisplayIndex >= 0 && _vm.CurrentDisplayIndex < EntryList.ItemCount)
                    EntryList.SelectedIndex = _vm.CurrentDisplayIndex;
            }
            catch (Exception ex)
            {
                Log.Error("ToolUndoView.Refresh failed: " + ex.ToString());
            }
        }

        void EntryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Selection state is read on demand by the action handlers; nothing
            // to do here, but the handler keeps the binding explicit.
        }

        async void EntryList_DoubleTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
        {
            await RollbackSelectedAsync();
        }

        async void Rollback_Click(object? sender, RoutedEventArgs e)
        {
            await RollbackSelectedAsync();
        }

        void TestPlay_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int pos = _vm.TestPlayPositionFor(EntryList.SelectedIndex);
                if (pos < 0) return;
                _vm.DoTestPlay(pos);
            }
            catch (Exception ex)
            {
                Log.Error("ToolUndoView.TestPlay_Click failed: " + ex.ToString());
            }
        }

        /// <summary>
        /// Show the rollback confirmation dialog for the selected snapshot and
        /// act on the result. Mirrors WinForms <c>RollbackThisVersion</c>:
        /// Yes -&gt; rollback + refresh, Retry -&gt; test-play, Cancel -&gt; nothing.
        /// </summary>
        async System.Threading.Tasks.Task RollbackSelectedAsync()
        {
            try
            {
                int pos = _vm.RollbackPositionFor(EntryList.SelectedIndex);
                if (pos < 0) return; // invalid selection or already current (no-op)

                var popup = new ToolUndoPopupDialogView();
                popup.Init(_vm.MakeVersionName(pos));
                string? result = await popup.ShowDialog<string?>(this);

                if (result == "RunUndo")
                {
                    _vm.DoRollback(pos);
                    Refresh();
                }
                else if (result == "TestPlay")
                {
                    _vm.DoTestPlay(pos);
                }
                // "Cancel"/null -> no action
            }
            catch (Exception ex)
            {
                Log.Error("ToolUndoView.RollbackSelectedAsync failed: " + ex.ToString());
            }
        }

        // IEditorView — undo entries have positions, not addresses, so NavigateTo
        // is a no-op. SelectFirstItem selects the newest row (drives the headless
        // screenshot recipe).
        public void NavigateTo(uint address) { }

        public void SelectFirstItem()
        {
            if (EntryList.ItemCount > 0)
                EntryList.SelectedIndex = 0;
        }
    }
}
