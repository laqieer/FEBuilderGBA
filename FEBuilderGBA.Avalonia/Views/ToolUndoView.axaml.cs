using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolUndoView : TranslatedWindow, IEditorView
    {
        readonly ToolUndoViewModel _vm = new();

        public string ViewTitle => "Undo History Viewer";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolUndoView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);   // auto-selects first (HEAD) -> OnSelected -> UpdateUI
            }
            catch (Exception ex)
            {
                Log.Error("ToolUndoView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ToolUndoView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            DetailText.Text = string.IsNullOrEmpty(_vm.SelectedInfo) ? "(no snapshot selected)" : _vm.SelectedInfo;
            RollbackButton.IsEnabled = _vm.CanRollback;
            TestPlayButton.IsEnabled = _vm.CanTestPlay;
        }

        // Rollback button: confirm via the shared popup (Yes=RunUndo / Retry=TestPlay /
        // Cancel) — mirrors WinForms ToolUndoForm.RollbackThisVersion.
        async void Rollback_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int pos = _vm.SelectedPos;
                if (pos < 0 || !_vm.CanRollback) return;

                var popup = new ToolUndoPopupDialogView();
                popup.Init(_vm.MakeRollbackLabel(pos));
                string? result = await popup.ShowDialog<string?>(this);

                if (result == "RunUndo")
                {
                    if (_vm.Rollback(pos))
                        LoadList();   // refresh: positions + "->" marker move
                }
                else if (result == "TestPlay")
                {
                    _vm.TestPlay(pos);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolUndoView.Rollback_Click failed: {0}", ex.Message);
            }
        }

        // Test-play button: directly test-play the selected snapshot — mirrors
        // WinForms ToolUndoForm.testplayButton_Click.
        void TestPlay_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int pos = _vm.SelectedPos;
                if (pos < 0 || !_vm.CanTestPlay) return;
                _vm.TestPlay(pos);
            }
            catch (Exception ex)
            {
                Log.Error("ToolUndoView.TestPlay_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
