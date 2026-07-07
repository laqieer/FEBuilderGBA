using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTerrainBGLookupView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapTerrainBGLookupTableViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Terrain BG Lookup";
        public new bool IsLoaded => _vm.IsLoaded;

        public EditorDescriptor Descriptor => new("Terrain BG Lookup", 1253, 790, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public MapTerrainBGLookupView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                LoadList();

            }

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
                Log.ErrorF("MapTerrainBGLookupView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("MapTerrainBGLookupView.OnSelected failed: {0}", ex.Message);
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
                Log.ErrorF("MapTerrainBGLookupView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
