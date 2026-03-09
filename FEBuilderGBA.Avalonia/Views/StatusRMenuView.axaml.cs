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
            try
            {
                var items = _vm.LoadStatusRMenuList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("StatusRMenuView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadStatusRMenu(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("StatusRMenuView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UpPtrBox.Text = $"0x{_vm.UpPtr:X08}";
            DownPtrBox.Text = $"0x{_vm.DownPtr:X08}";
            LeftPtrBox.Text = $"0x{_vm.LeftPtr:X08}";
            RightPtrBox.Text = $"0x{_vm.RightPtr:X08}";
            B16Box.Value = _vm.B16;
            B17Box.Value = _vm.B17;
            TextIdBox.Value = _vm.TextId;
            P20Box.Text = $"0x{_vm.P20:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.UpPtr = ParseHexText(UpPtrBox.Text);
            _vm.DownPtr = ParseHexText(DownPtrBox.Text);
            _vm.LeftPtr = ParseHexText(LeftPtrBox.Text);
            _vm.RightPtr = ParseHexText(RightPtrBox.Text);
            _vm.B16 = (uint)(B16Box.Value ?? 0);
            _vm.B17 = (uint)(B17Box.Value ?? 0);
            _vm.TextId = (uint)(TextIdBox.Value ?? 0);
            _vm.P20 = ParseHexText(P20Box.Text);
            _vm.WriteStatusRMenu();
            CoreState.Services?.ShowInfo("Status R-Menu data written.");
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
