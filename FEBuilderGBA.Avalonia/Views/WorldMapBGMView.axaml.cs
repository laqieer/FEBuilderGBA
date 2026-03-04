using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapBGMView : Window, IEditorView, IDataVerifiableView
    {
        readonly WorldMapBGMViewModel _vm = new();

        public string ViewTitle => "World Map BGM";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapBGMView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadWorldMapBGMList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapBGMView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadWorldMapBGM(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapBGMView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SongId1Label.Text = $"0x{_vm.SongId1:X04} ({_vm.SongId1})";
            SongId2Label.Text = $"0x{_vm.SongId2:X04} ({_vm.SongId2})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
