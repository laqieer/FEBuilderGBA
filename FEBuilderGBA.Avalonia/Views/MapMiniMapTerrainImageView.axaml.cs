using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapMiniMapTerrainImageView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapMiniMapTerrainImageViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _loading;

        public string ViewTitle => "Mini-Map Terrain";
        public bool IsLoaded => _vm.IsLoaded;

        public MapMiniMapTerrainImageView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            TileArrayCombo.ItemsSource = _vm.OptionLabels;
            TileArrayCombo.SelectionChanged += OnComboChanged;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapMiniMapTerrainImageView.LoadList failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapMiniMapTerrainImageView.OnSelected failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            _loading = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                PointerBox.Value = _vm.P0;
                TileArrayCombo.SelectedIndex = _vm.GetOptionIndex(_vm.P0);
            }
            finally
            {
                _loading = false;
            }
        }

        // Selecting a known tile array sets the pointer field to its value.
        void OnComboChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            int idx = TileArrayCombo.SelectedIndex;
            if (idx < 0) return;
            PointerBox.Value = _vm.GetOptionValue(idx);
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Mini-Map Terrain"));
            try
            {
                _vm.P0 = (uint)(PointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapMiniMapTerrainImageView.OnWrite failed: " + ex.ToString());
            }

            UpdateUI();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
