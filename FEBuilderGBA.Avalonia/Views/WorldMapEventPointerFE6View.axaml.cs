// SPDX-License-Identifier: GPL-3.0-or-later
// World Map Event Pointer (FE6) editor view — port of WinForms
// WorldMapEventPointerFE6Form (#1181).
//
// FE6 is navigate-only: a single AddressListControl lists every map whose
// world-map-event PLIST is set, and selecting a row resolves to the
// MAP-pointer-table slot the PLIST indexes (see the VM). There are no
// editable ROM-data fields and no Write button, so the view holds no
// UndoService and the VM does not implement IDataVerifiable.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapEventPointerFE6View : TranslatedWindow, IEditorView
    {
        readonly WorldMapEventPointerFE6ViewModel _vm = new();

        public string ViewTitle => "Event Pointer (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapEventPointerFE6View()
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
                Log.Error("WorldMapEventPointerFE6View.LoadList failed: " + ex.ToString());
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
                Log.Error("WorldMapEventPointerFE6View.OnSelected failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
