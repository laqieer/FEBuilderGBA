using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class BattleTerrainViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly BattleTerrainViewerViewModel _vm = new();

        public string ViewTitle => "Battle Terrain Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public BattleTerrainViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try { var items = _vm.LoadBattleTerrainList(); EntryList.SetItems(items); }
            catch (Exception ex) { Log.Error("BattleTerrainViewerView.LoadList: {0}", ex.Message); }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadBattleTerrain(addr);
                UpdateUI();
                LoadImage();
            }
            catch (Exception ex) { Log.Error("BattleTerrainViewerView.OnSelected: {0}", ex.Message); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TerrainNameLabel.Text = _vm.TerrainName;
            B0Box.Value = _vm.B0;
            B1Box.Value = _vm.B1;
            B2Box.Value = _vm.B2;
            B3Box.Value = _vm.B3;
            B4Box.Value = _vm.B4;
            B5Box.Value = _vm.B5;
            B6Box.Value = _vm.B6;
            B7Box.Value = _vm.B7;
            B8Box.Value = _vm.B8;
            B9Box.Value = _vm.B9;
            B10Box.Value = _vm.B10;
            B11Box.Value = _vm.B11;
            ImgPtrBox.Text = $"0x{_vm.ImagePointer:X08}";
            PalPtrBox.Text = $"0x{_vm.PalettePointer:X08}";
            D20Box.Text = $"0x{_vm.D20:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.B0 = (uint)(B0Box.Value ?? 0);
            _vm.B1 = (uint)(B1Box.Value ?? 0);
            _vm.B2 = (uint)(B2Box.Value ?? 0);
            _vm.B3 = (uint)(B3Box.Value ?? 0);
            _vm.B4 = (uint)(B4Box.Value ?? 0);
            _vm.B5 = (uint)(B5Box.Value ?? 0);
            _vm.B6 = (uint)(B6Box.Value ?? 0);
            _vm.B7 = (uint)(B7Box.Value ?? 0);
            _vm.B8 = (uint)(B8Box.Value ?? 0);
            _vm.B9 = (uint)(B9Box.Value ?? 0);
            _vm.B10 = (uint)(B10Box.Value ?? 0);
            _vm.B11 = (uint)(B11Box.Value ?? 0);
            _vm.ImagePointer = ParseHexText(ImgPtrBox.Text);
            _vm.PalettePointer = ParseHexText(PalPtrBox.Text);
            _vm.D20 = ParseHexText(D20Box.Text);
            _vm.WriteBattleTerrain();
            CoreState.Services.ShowInfo("Battle Terrain data written.");
        }

        void LoadImage()
        {
            try
            {
                ImageDisplay.SetImage(_vm.TryLoadImage());
            }
            catch { ImageDisplay.SetImage(null); }
        }

        async void ExportPng_Click(object? sender, RoutedEventArgs e)
        {
            await ImageDisplay.ExportPng(this, "battle_terrain.png");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
