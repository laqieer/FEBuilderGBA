using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapPathView : Window, IEditorView
    {
        readonly WorldMapPathViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "World Map Paths";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapPathView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("WorldMapPathView.LoadList failed: {0}", ex.Message);
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
                Log.Error("WorldMapPathView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            PathDataPointerBox.Text = $"0x{_vm.PathDataPointer:X08}";
            StartBasePointIdBox.Value = _vm.StartBasePointId;
            EndBasePointIdBox.Value = _vm.EndBasePointId;
            Padding6Box.Value = _vm.Padding6;
            Padding7Box.Value = _vm.Padding7;
            PathMovePointerBox.Text = $"0x{_vm.PathMovePointer:X08}";
        }

        void ReadFromUI()
        {
            _vm.PathDataPointer = U.atoh(PathDataPointerBox.Text ?? "");
            _vm.StartBasePointId = (uint)(StartBasePointIdBox.Value ?? 0);
            _vm.EndBasePointId = (uint)(EndBasePointIdBox.Value ?? 0);
            _vm.Padding6 = (uint)(Padding6Box.Value ?? 0);
            _vm.Padding7 = (uint)(Padding7Box.Value ?? 0);
            _vm.PathMovePointer = U.atoh(PathMovePointerBox.Text ?? "");
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _undoService.Begin("Edit World Map Path");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("World map path data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapPathView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
