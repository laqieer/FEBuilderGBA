using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapSettingViewModel _vm = new();

        public string ViewTitle => "Map Settings";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapSettingView()
        {
            InitializeComponent();
            MapList.SelectedAddressChanged += OnMapSelected;
            WriteButton.Click += OnWriteClick;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMapSettingList();
                MapList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnMapSelected(uint addr)
        {
            try
            {
                _vm.LoadMapSetting(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingView.OnMapSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            MapList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"Address: 0x{_vm.CurrentAddr:X08}  (struct size: {_vm.DataSize} bytes)";

            // D0
            ND0.Text = $"0x{_vm.D0:X08}";

            // W4
            NW4.Value = _vm.W4;

            // B6-B19
            NB6.Value = _vm.B6;
            NB7.Value = _vm.B7;
            NB8.Value = _vm.B8;
            NB9.Value = _vm.B9;
            NB10.Value = _vm.B10;
            NB11.Value = _vm.B11;
            NB12.Value = _vm.B12;
            NB13.Value = _vm.B13;
            NB14.Value = _vm.B14;
            NB15.Value = _vm.B15;
            NB16.Value = _vm.B16;
            NB17.Value = _vm.B17;
            NB18.Value = _vm.B18;
            NB19.Value = _vm.B19;

            // W20-W42
            NW20.Value = _vm.W20;
            NW22.Value = _vm.W22;
            NW24.Value = _vm.W24;
            NW26.Value = _vm.W26;
            NW28.Value = _vm.W28;
            NW30.Value = _vm.W30;
            NW32.Value = _vm.W32;
            NW34.Value = _vm.W34;
            NW36.Value = _vm.W36;
            NW38.Value = _vm.W38;
            NW40.Value = _vm.W40;
            NW42.Value = _vm.W42;

            // B44-B61
            NB44.Value = _vm.B44;
            NB45.Value = _vm.B45;
            NB46.Value = _vm.B46;
            NB47.Value = _vm.B47;
            NB48.Value = _vm.B48;
            NB49.Value = _vm.B49;
            NB50.Value = _vm.B50;
            NB51.Value = _vm.B51;
            NB52.Value = _vm.B52;
            NB53.Value = _vm.B53;
            NB54.Value = _vm.B54;
            NB55.Value = _vm.B55;
            NB56.Value = _vm.B56;
            NB57.Value = _vm.B57;
            NB58.Value = _vm.B58;
            NB59.Value = _vm.B59;
            NB60.Value = _vm.B60;
            NB61.Value = _vm.B61;

            // W62-W94
            NW62.Value = _vm.W62;
            NW64.Value = _vm.W64;
            NW66.Value = _vm.W66;
            NW68.Value = _vm.W68;
            NW70.Value = _vm.W70;
            NW72.Value = _vm.W72;
            NW74.Value = _vm.W74;
            NW76.Value = _vm.W76;
            NW78.Value = _vm.W78;
            NW80.Value = _vm.W80;
            NW82.Value = _vm.W82;
            NW84.Value = _vm.W84;
            NW86.Value = _vm.W86;
            NW88.Value = _vm.W88;
            NW90.Value = _vm.W90;
            NW92.Value = _vm.W92;
            NW94.Value = _vm.W94;

            // D96-D108
            ND96.Text = $"0x{_vm.D96:X08}";
            ND100.Text = $"0x{_vm.D100:X08}";
            ND104.Text = $"0x{_vm.D104:X08}";
            ND108.Text = $"0x{_vm.D108:X08}";

            // W112, W114
            NW112.Value = _vm.W112;
            NW114.Value = _vm.W114;
            W112TextLabel.Text = _vm.W112Text;
            W114TextLabel.Text = _vm.W114Text;

            // B116-B135
            NB116.Value = _vm.B116;
            NB117.Value = _vm.B117;
            NB118.Value = _vm.B118;
            NB119.Value = _vm.B119;
            NB120.Value = _vm.B120;
            NB121.Value = _vm.B121;
            NB122.Value = _vm.B122;
            NB123.Value = _vm.B123;
            NB124.Value = _vm.B124;
            NB125.Value = _vm.B125;
            NB126.Value = _vm.B126;
            NB127.Value = _vm.B127;
            NB128.Value = _vm.B128;
            NB129.Value = _vm.B129;
            NB130.Value = _vm.B130;
            NB131.Value = _vm.B131;
            NB132.Value = _vm.B132;
            NB133.Value = _vm.B133;
            NB134.Value = _vm.B134;
            NB135.Value = _vm.B135;

            // W136, W138
            NW136.Value = _vm.W136;
            NW138.Value = _vm.W138;
            W136TextLabel.Text = _vm.W136Text;
            W138TextLabel.Text = _vm.W138Text;

            // B140-B147
            NB140.Value = _vm.B140;
            NB141.Value = _vm.B141;
            NB142.Value = _vm.B142;
            NB143.Value = _vm.B143;
            NB144.Value = _vm.B144;
            NB145.Value = _vm.B145;
            NB146.Value = _vm.B146;
            NB147.Value = _vm.B147;
        }

        void ReadUIToVM()
        {
            _vm.D0 = ParseHexText(ND0.Text);
            _vm.W4 = (uint)(NW4.Value ?? 0);

            _vm.B6 = (uint)(NB6.Value ?? 0);
            _vm.B7 = (uint)(NB7.Value ?? 0);
            _vm.B8 = (uint)(NB8.Value ?? 0);
            _vm.B9 = (uint)(NB9.Value ?? 0);
            _vm.B10 = (uint)(NB10.Value ?? 0);
            _vm.B11 = (uint)(NB11.Value ?? 0);
            _vm.B12 = (uint)(NB12.Value ?? 0);
            _vm.B13 = (uint)(NB13.Value ?? 0);
            _vm.B14 = (uint)(NB14.Value ?? 0);
            _vm.B15 = (uint)(NB15.Value ?? 0);
            _vm.B16 = (uint)(NB16.Value ?? 0);
            _vm.B17 = (uint)(NB17.Value ?? 0);
            _vm.B18 = (uint)(NB18.Value ?? 0);
            _vm.B19 = (uint)(NB19.Value ?? 0);

            _vm.W20 = (uint)(NW20.Value ?? 0);
            _vm.W22 = (uint)(NW22.Value ?? 0);
            _vm.W24 = (uint)(NW24.Value ?? 0);
            _vm.W26 = (uint)(NW26.Value ?? 0);
            _vm.W28 = (uint)(NW28.Value ?? 0);
            _vm.W30 = (uint)(NW30.Value ?? 0);
            _vm.W32 = (uint)(NW32.Value ?? 0);
            _vm.W34 = (uint)(NW34.Value ?? 0);
            _vm.W36 = (uint)(NW36.Value ?? 0);
            _vm.W38 = (uint)(NW38.Value ?? 0);
            _vm.W40 = (uint)(NW40.Value ?? 0);
            _vm.W42 = (uint)(NW42.Value ?? 0);

            _vm.B44 = (uint)(NB44.Value ?? 0);
            _vm.B45 = (uint)(NB45.Value ?? 0);
            _vm.B46 = (uint)(NB46.Value ?? 0);
            _vm.B47 = (uint)(NB47.Value ?? 0);
            _vm.B48 = (uint)(NB48.Value ?? 0);
            _vm.B49 = (uint)(NB49.Value ?? 0);
            _vm.B50 = (uint)(NB50.Value ?? 0);
            _vm.B51 = (uint)(NB51.Value ?? 0);
            _vm.B52 = (uint)(NB52.Value ?? 0);
            _vm.B53 = (uint)(NB53.Value ?? 0);
            _vm.B54 = (uint)(NB54.Value ?? 0);
            _vm.B55 = (uint)(NB55.Value ?? 0);
            _vm.B56 = (uint)(NB56.Value ?? 0);
            _vm.B57 = (uint)(NB57.Value ?? 0);
            _vm.B58 = (uint)(NB58.Value ?? 0);
            _vm.B59 = (uint)(NB59.Value ?? 0);
            _vm.B60 = (uint)(NB60.Value ?? 0);
            _vm.B61 = (uint)(NB61.Value ?? 0);

            _vm.W62 = (uint)(NW62.Value ?? 0);
            _vm.W64 = (uint)(NW64.Value ?? 0);
            _vm.W66 = (uint)(NW66.Value ?? 0);
            _vm.W68 = (uint)(NW68.Value ?? 0);
            _vm.W70 = (uint)(NW70.Value ?? 0);
            _vm.W72 = (uint)(NW72.Value ?? 0);
            _vm.W74 = (uint)(NW74.Value ?? 0);
            _vm.W76 = (uint)(NW76.Value ?? 0);
            _vm.W78 = (uint)(NW78.Value ?? 0);
            _vm.W80 = (uint)(NW80.Value ?? 0);
            _vm.W82 = (uint)(NW82.Value ?? 0);
            _vm.W84 = (uint)(NW84.Value ?? 0);
            _vm.W86 = (uint)(NW86.Value ?? 0);
            _vm.W88 = (uint)(NW88.Value ?? 0);
            _vm.W90 = (uint)(NW90.Value ?? 0);
            _vm.W92 = (uint)(NW92.Value ?? 0);
            _vm.W94 = (uint)(NW94.Value ?? 0);

            _vm.D96 = ParseHexText(ND96.Text);
            _vm.D100 = ParseHexText(ND100.Text);
            _vm.D104 = ParseHexText(ND104.Text);
            _vm.D108 = ParseHexText(ND108.Text);

            _vm.W112 = (uint)(NW112.Value ?? 0);
            _vm.W114 = (uint)(NW114.Value ?? 0);

            _vm.B116 = (uint)(NB116.Value ?? 0);
            _vm.B117 = (uint)(NB117.Value ?? 0);
            _vm.B118 = (uint)(NB118.Value ?? 0);
            _vm.B119 = (uint)(NB119.Value ?? 0);
            _vm.B120 = (uint)(NB120.Value ?? 0);
            _vm.B121 = (uint)(NB121.Value ?? 0);
            _vm.B122 = (uint)(NB122.Value ?? 0);
            _vm.B123 = (uint)(NB123.Value ?? 0);
            _vm.B124 = (uint)(NB124.Value ?? 0);
            _vm.B125 = (uint)(NB125.Value ?? 0);
            _vm.B126 = (uint)(NB126.Value ?? 0);
            _vm.B127 = (uint)(NB127.Value ?? 0);
            _vm.B128 = (uint)(NB128.Value ?? 0);
            _vm.B129 = (uint)(NB129.Value ?? 0);
            _vm.B130 = (uint)(NB130.Value ?? 0);
            _vm.B131 = (uint)(NB131.Value ?? 0);
            _vm.B132 = (uint)(NB132.Value ?? 0);
            _vm.B133 = (uint)(NB133.Value ?? 0);
            _vm.B134 = (uint)(NB134.Value ?? 0);
            _vm.B135 = (uint)(NB135.Value ?? 0);

            _vm.W136 = (uint)(NW136.Value ?? 0);
            _vm.W138 = (uint)(NW138.Value ?? 0);

            _vm.B140 = (uint)(NB140.Value ?? 0);
            _vm.B141 = (uint)(NB141.Value ?? 0);
            _vm.B142 = (uint)(NB142.Value ?? 0);
            _vm.B143 = (uint)(NB143.Value ?? 0);
            _vm.B144 = (uint)(NB144.Value ?? 0);
            _vm.B145 = (uint)(NB145.Value ?? 0);
            _vm.B146 = (uint)(NB146.Value ?? 0);
            _vm.B147 = (uint)(NB147.Value ?? 0);
        }

        void OnWriteClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                ReadUIToVM();
                _vm.WriteMapSetting();
                // Reload to confirm write
                _vm.LoadMapSetting(_vm.CurrentAddr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingView.Write failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            MapList.SelectFirst();
        }

        private static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        }
    }
}
