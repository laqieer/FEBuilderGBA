using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonsDemonKingViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SummonsDemonKingViewerViewModel _vm = new();

        public string ViewTitle => "Demon King Summon";
        public bool IsLoaded => _vm.CanWrite;

        public SummonsDemonKingViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSummonsDemonKingList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSummonsDemonKing(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            ClassIdBox.Value = _vm.ClassId;
            Unknown1Box.Value = _vm.Unknown1;
            B3Box.Value = _vm.B3;
            W4Box.Value = _vm.W4;
            B6Box.Value = _vm.B6;
            B7Box.Value = _vm.B7;
            P8Box.Text = $"0x{_vm.P8:X08}";
            B12Box.Value = _vm.B12;
            B13Box.Value = _vm.B13;
            B14Box.Value = _vm.B14;
            B15Box.Value = _vm.B15;
            B16Box.Value = _vm.B16;
            B17Box.Value = _vm.B17;
            B18Box.Value = _vm.B18;
            B19Box.Value = _vm.B19;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
            _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.W4 = (uint)(W4Box.Value ?? 0);
            _vm.B6 = (uint)(B6Box.Value ?? 0);
            _vm.B7 = (uint)(B7Box.Value ?? 0);
            _vm.P8 = ParseHexText(P8Box.Text);
            _vm.B12 = (uint)(B12Box.Value ?? 0);
            _vm.B13 = (uint)(B13Box.Value ?? 0);
            _vm.B14 = (uint)(B14Box.Value ?? 0);
            _vm.B15 = (uint)(B15Box.Value ?? 0);
            _vm.B16 = (uint)(B16Box.Value ?? 0);
            _vm.B17 = (uint)(B17Box.Value ?? 0);
            _vm.B18 = (uint)(B18Box.Value ?? 0);
            _vm.B19 = (uint)(B19Box.Value ?? 0);
            _vm.WriteSummonsDemonKing();
            CoreState.Services.ShowInfo("Demon king summon data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
