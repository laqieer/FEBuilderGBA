using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class HexEditorView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly HexEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Hex Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Hex Editor", 820, 600, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public HexEditorView()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                _vm.RefreshDisplay(); UpdateUI();
            }
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
            // UndoService.Begin() no-ops when CoreState.Undo is null. Never mutate
            // ROM bytes without an active undo scope — refuse to write instead, so
            // the Hex Editor is ALWAYS undo-tracked (Copilot PR review).
            if (!_undoService.HasPendingUndo)
            {
                InfoLabel.Text = "Cannot write: undo system unavailable.";
                return;
            }
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
