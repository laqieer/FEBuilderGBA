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
        public bool IsLoaded => _vm.CanWrite;

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
            SongId1Box.Value = _vm.SongId1;
            SongId2Box.Value = _vm.SongId2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.SongId1 = (uint)(SongId1Box.Value ?? 0);
            _vm.SongId2 = (uint)(SongId2Box.Value ?? 0);
            _vm.WriteWorldMapBGM();
            CoreState.Services?.ShowInfo("World map BGM data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
