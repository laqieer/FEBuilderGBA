using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTerrainBGLookupView : Window, IEditorView, IDataVerifiableView
    {
        readonly MapTerrainBGLookupTableViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Terrain BG Lookup";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapTerrainBGLookupView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;

                // Load terrain BG lookup table from the first pointer (default terrain set)
                uint pointer = rom.RomInfo.lookup_table_battle_bg_00_pointer;
                if (pointer == 0) return;
                uint baseAddr = rom.p32(pointer);
                if (!U.isSafetyOffset(baseAddr)) return;

                uint count = rom.RomInfo.map_terrain_type_count;
                var items = new System.Collections.Generic.List<AddrResult>();

                for (uint i = 0; i < count; i++)
                {
                    uint addr = baseAddr + i; // Each entry is 1 byte (B0 field)
                    uint bgValue = rom.u8(addr);
                    string name = U.ToHexString(i) + " BG=" + U.ToHexString(bgValue);
                    items.Add(new AddrResult(addr, name, i));
                }

                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapTerrainBGLookupView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapTerrainBGLookupView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            BattleBGBox.Value = _vm.BattleBG;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _vm.BattleBG = (uint)(BattleBGBox.Value ?? 0);
            _undoService.Begin("Edit Terrain BG Lookup");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Terrain BG Lookup data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapTerrainBGLookupView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
