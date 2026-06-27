using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTerrainNameView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapTerrainNameViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Terrain Name Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public MapTerrainNameView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapTerrainNameView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("MapTerrainNameView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            PointerBox.Text = $"0x{_vm.TerrainNamePointer:X08}";
            NameBox.Text = _vm.TerrainName;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // The editable surface is the Name string; the pointer is read-only
            // diagnostics that Write() repoints automatically. Read ONLY the Name.
            _vm.TerrainName = NameBox.Text ?? string.Empty;

            _undoService.Begin("Edit Terrain Name");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                // Refresh the display: the slot was repointed to fresh free space,
                // so the diagnostics pointer updates.
                UpdateUI();
                // Rebuild the list so the new decoded name shows in the entry list.
                LoadList();
                EntryList.SelectAddress(_vm.CurrentAddr);
                CoreState.Services.ShowInfo("Terrain name data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapTerrainNameView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
