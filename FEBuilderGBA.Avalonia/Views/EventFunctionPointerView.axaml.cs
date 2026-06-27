using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventFunctionPointerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventFunctionPointerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _populatingFilter;

        public string ViewTitle => "Event Function Pointer Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public EventFunctionPointerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            FilterComboBox.SelectionChanged += OnFilterChanged;
            Opened += (_, _) =>
            {
                PopulateFilter();
                LoadList();
            };
        }

        // #1441 — populate the Primary/Worldmap filter. Primary is always
        // present; the FE8-only Worldmap (event_function_pointer_table2) option
        // is added only when the ROM exposes that metadata slot.
        void PopulateFilter()
        {
            _populatingFilter = true;
            try
            {
                var items = new System.Collections.Generic.List<string> { R._("Primary") };
                if (_vm.IsWorldmapAvailable)
                    items.Add(R._("Worldmap"));
                FilterComboBox.ItemsSource = items;
                FilterComboBox.SelectedIndex = 0;
                _vm.FilterIndex = 0;
            }
            finally
            {
                _populatingFilter = false;
            }
        }

        void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_populatingFilter) return;
            int idx = FilterComboBox.SelectedIndex;
            _vm.FilterIndex = idx < 0 ? 0 : idx;
            LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                // Mirror WinForms SelectedIndexSafety(AddressList, 0): if the
                // newly loaded list is empty, clear the detail panel so a stale
                // row from the previous filter cannot be edited.
                if (items.Count == 0)
                {
                    _vm.IsLoaded = false;
                    _vm.CurrentAddr = 0;
                    AddrLabel.Text = string.Empty;
                    FuncPointerUpDown.Value = 0;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventFunctionPointerView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("EventFunctionPointerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            FuncPointerUpDown.Value = _vm.EventCommandFunctionPointer;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Event Function Pointer"));
            try
            {
                _vm.EventCommandFunctionPointer = (uint)(FuncPointerUpDown.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventFunctionPointerView.OnWrite failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
