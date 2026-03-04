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
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            NameLabel.Text = _vm.Name;
            TilesetLabel.Text = $"0x{_vm.TilesetPLIST:X02}";
            MapPlistLabel.Text = $"0x{_vm.MapPLIST:X02}";
            PaletteLabel.Text = $"0x{_vm.PalettePLIST:X02}";
            WeatherLabel.Text = $"0x{_vm.Weather:X02}";
            ObjTypeLabel.Text = $"0x{_vm.ObjType:X02}";
            ChapterNameIdLabel.Text = $"0x{_vm.ChapterNameId:X04}";
        }

        public void SelectFirstItem()
        {
            MapList.SelectFirst();
        }
    }
}
