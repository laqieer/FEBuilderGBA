using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorView : TranslatedWindow, IEditorView
    {
        readonly HexEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Hex Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public HexEditorView()
        {
            InitializeComponent();
            DataContext = _vm;
            Opened += (_, _) => { _vm.RefreshDisplay(); UpdateUI(); };
        }

        public void NavigateTo(uint address)
        {
            _vm.JumpTo(address);
            UpdateUI();
            AddressBox.Text = $"0x{address:X08}";
        }

        public void SelectFirstItem()
        {
            NavigateTo(0);
        }

        void UpdateUI()
        {
            HexGrid.Text = _vm.HexDisplay;
            InfoLabel.Text = _vm.AddressInfo;
        }

        void Go_Click(object? sender, RoutedEventArgs e)
        {
            uint addr = U.atoh(AddressBox.Text ?? "0");
            _vm.JumpTo(addr);
            UpdateUI();
        }

        void Search_Click(object? sender, RoutedEventArgs e)
        {
            WindowManager.Instance.Open<HexEditorSearchView>();
        }

        void PageUp_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PageUp();
            UpdateUI();
            AddressBox.Text = $"0x{_vm.BaseAddress:X08}";
        }

        void PageDown_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PageDown();
            UpdateUI();
            AddressBox.Text = $"0x{_vm.BaseAddress:X08}";
        }

        /// <summary>
        /// Commit the bytes the user edited in the hex grid to the ROM (#1466), porting
        /// WinForms <c>HexEditorForm.WriteButton_Click</c>. Every ROM write happens
        /// inside an <see cref="UndoService"/> scope so it is undo-tracked; the scope is
        /// committed only when at least one byte was written, and rolled back on any
        /// validation failure, zero-change write, or unexpected exception.
        /// </summary>
        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Hex");
            try
            {
                HexEditCore.WriteResult wr = _vm.Write(HexGrid.Text ?? string.Empty);
                if (wr.Success)
                {
                    _undoService.Commit();
                    UpdateUI(); // VM refreshed the display; reset the editable baseline
                }
                else
                {
                    _undoService.Rollback();
                    InfoLabel.Text = _vm.AddressInfo; // surface the refusal reason
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"HexEditorView.Write failed: {ex}");
                InfoLabel.Text = "Write failed: " + ex.Message;
            }
        }

        void HexGrid_KeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+S commits the edited bytes (mirrors WinForms HexBox Ctrl+S binding).
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                Write_Click(sender, new RoutedEventArgs());
            }
        }
    }
}
