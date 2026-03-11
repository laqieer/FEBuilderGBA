using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapLoadFunctionView : Window, IEditorView
    {
        readonly MapLoadFunctionViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Map Load Functions";
        public bool IsLoaded => _vm.IsLoaded;

        public MapLoadFunctionView()
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
                Log.Error("MapLoadFunctionView.LoadList failed: {0}", ex.Message);
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
                Log.Error("MapLoadFunctionView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            PointerBox.Value = _vm.P0;
            PointerInfoLabel.Text = _vm.PointerInfo;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _vm.P0 = (uint)(PointerBox.Value ?? 0);

            _undoService.Begin("Edit Map Load Function");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                LoadList();
                CoreState.Services?.ShowInfo("Map load function pointer written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapLoadFunctionView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
