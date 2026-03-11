using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusRMenuView : Window, IEditorView, IDataVerifiableView
    {
        readonly StatusRMenuViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Status R-Menu";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public StatusRMenuView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadStatusRMenuList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusRMenuView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadStatusRMenu(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusRMenuView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UpPtrBox.Text = $"0x{_vm.UpPtr:X08}";
            DownPtrBox.Text = $"0x{_vm.DownPtr:X08}";
            LeftPtrBox.Text = $"0x{_vm.LeftPtr:X08}";
            RightPtrBox.Text = $"0x{_vm.RightPtr:X08}";
            PosXBox.Value = _vm.PosX;
            PosYBox.Value = _vm.PosY;
            TextIdBox.Value = _vm.TextId;
            LoopBox.Text = $"0x{_vm.LoopRoutine:X08}";
            GetterBox.Text = $"0x{_vm.GetterRoutine:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Status R-Menu");
            try
            {
                _vm.UpPtr = ParseHexText(UpPtrBox.Text);
                _vm.DownPtr = ParseHexText(DownPtrBox.Text);
                _vm.LeftPtr = ParseHexText(LeftPtrBox.Text);
                _vm.RightPtr = ParseHexText(RightPtrBox.Text);
                _vm.PosX = (uint)(PosXBox.Value ?? 0);
                _vm.PosY = (uint)(PosYBox.Value ?? 0);
                _vm.TextId = (uint)(TextIdBox.Value ?? 0);
                _vm.LoopRoutine = ParseHexText(LoopBox.Text);
                _vm.GetterRoutine = ParseHexText(GetterBox.Text);
                _vm.WriteStatusRMenu();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status R-Menu data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("StatusRMenuView.Write: {0}", ex.Message); }
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
