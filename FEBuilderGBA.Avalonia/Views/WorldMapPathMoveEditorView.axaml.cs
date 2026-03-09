using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapPathMoveEditorView : Window, IEditorView
    {
        readonly WorldMapPathMoveEditorViewModel _vm = new();

        public string ViewTitle => "Path Movement Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapPathMoveEditorView()
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
                Log.Error("WorldMapPathMoveEditorView.LoadList failed: {0}", ex.Message);
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
                Log.Error("WorldMapPathMoveEditorView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ElapsedTimeBox.Value = _vm.ElapsedTime;
            CoordinateXBox.Value = _vm.CoordinateX;
            CoordinateYBox.Value = _vm.CoordinateY;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded) return;
                _vm.ElapsedTime = (uint)(ElapsedTimeBox.Value ?? 0);
                _vm.CoordinateX = (uint)(CoordinateXBox.Value ?? 0);
                _vm.CoordinateY = (uint)(CoordinateYBox.Value ?? 0);
                _vm.Write();
                CoreState.Services?.ShowInfo("Path movement data written.");
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapPathMoveEditorView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
