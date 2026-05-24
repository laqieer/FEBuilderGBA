using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapStyleEditorView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapStyleEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        List<AddrResult> _styleList = new();

        public string ViewTitle => "Map Style Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public MapStyleEditorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            // Re-load the palette when either the palette index or the
            // fog flag changes — this keeps the 16-row RGB grid in sync
            // with the user's selection (mirrors WF behavior).
            PaletteCombo.SelectionChanged += (_, _) => ReloadPalette();
            PaletteTypeCombo.SelectionChanged += (_, _) => ReloadPalette();
            MapStyleCombo.SelectionChanged += MapStyle_SelectionChanged;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _styleList = _vm.LoadList();
                EntryList.SetItems(_styleList);
                // Mirror EntryList into MapStyleCombo so the top-bar
                // selector is also populated (Copilot bot inline review
                // on MapStyleCombo).
                MapStyleCombo.ItemsSource = _styleList.ConvertAll(r => r.name);
                if (MapStyleCombo.ItemsSource != null && _styleList.Count > 0)
                {
                    MapStyleCombo.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                Log.Error("MapStyleEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                // Sync the top-bar MapStyleCombo to the same entry without
                // recursing — block the SelectionChanged handler while we
                // assign by toggling _vm.IsLoading.
                int idx = _styleList.FindIndex(r => r.addr == addr);
                if (idx >= 0 && MapStyleCombo.SelectedIndex != idx)
                    MapStyleCombo.SelectedIndex = idx;
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("MapStyleEditorView.OnSelected failed: {0}", ex.Message);
            }
        }

        void MapStyle_SelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            int idx = MapStyleCombo.SelectedIndex;
            if (idx < 0 || idx >= _styleList.Count) return;
            // Forward selection to the AddressList so all other UI stays
            // in sync via the existing OnSelected path.
            EntryList.SelectAddress(_styleList[idx].addr);
        }

        void ReloadPalette()
        {
            if (_vm.IsLoading) return;
            int idx = PaletteCombo.SelectedIndex;
            if (idx < 0) idx = 0;
            bool fog = PaletteTypeCombo.SelectedIndex == 1;
            _vm.IsLoading = true;
            try
            {
                _vm.LoadPalette(_vm.PaletteAddress, idx, fog);
                UpdatePaletteUI();
            }
            finally { _vm.IsLoading = false; }
        }

        void UpdateUI()
        {
            // Tab 1 -- Map Style.
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ObjPtrBox.Text = $"0x{_vm.ObjPointer:X08}";
            ConfigPtrLabel.Text = $"0x{_vm.ConfigPointer:X08}";
            ObjAddressLabel.Text = $"0x{_vm.ObjAddress:X08}";
            ObjAddress2Label.Text = _vm.ObjAddress2 != 0 ? $"0x{_vm.ObjAddress2:X08}" : "(none)";
            PaletteAddressLabel.Text = $"0x{_vm.PaletteAddress:X08}";
            TilesetTypeLabel.Text = $"Tileset {_vm.ConfigNo}";

            // Tab 3 -- Chipset.
            ChipsetConfigAddressLabel.Text = $"0x{_vm.ChipsetConfigAddress:X08}";
            ConfigNoLabel.Text = _vm.ConfigNo;

            // Tab 2 -- Palette (populated by LoadEntry's LoadPalette call).
            UpdatePaletteUI();
        }

        void UpdatePaletteUI()
        {
            // Populate the 16 read-only RGB rows from the VM's loaded
            // palette state. The controls are IsEnabled="False" in AXAML
            // (Copilot bot inline review item -- palette data is now
            // displayed but not editable since palette writes are out
            // of scope per #374).
            for (int i = 1; i <= 16; i++)
            {
                var rBox = this.FindControl<NumericUpDown>($"Color{i}_RBox");
                var gBox = this.FindControl<NumericUpDown>($"Color{i}_GBox");
                var bBox = this.FindControl<NumericUpDown>($"Color{i}_BBox");
                if (rBox != null) rBox.Value = _vm.GetColorR(i);
                if (gBox != null) gBox.Value = _vm.GetColorG(i);
                if (bBox != null) bBox.Value = _vm.GetColorB(i);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ObjPointer = ParseHexText(ObjPtrBox.Text);

            _undoService.Begin("Edit Map Style");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Map style data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapStyleEditorView.Write_Click failed: {0}", ex.Message);
            }
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
