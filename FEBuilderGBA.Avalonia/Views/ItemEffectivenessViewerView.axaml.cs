using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEffectivenessViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemEffectivenessViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Item Effectiveness";
        public bool IsLoaded => _vm.CanWrite;

        public ItemEffectivenessViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemEffectivenessList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemEffectiveness(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("ItemEffectivenessViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassIdBox.Value = _vm.ClassId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);

            _undoService.Begin("Edit Item Effectiveness");
            try
            {
                _vm.WriteItemEffectiveness();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item Effectiveness data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
