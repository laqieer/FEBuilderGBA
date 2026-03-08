using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEffectPointerViewerView : Window, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemEffectPointerViewerViewModel _vm = new();

        public string ViewTitle => "Item Effect Pointer";
        public bool IsLoaded => _vm.CanWrite;

        public ItemEffectPointerViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemEffectPointerList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectPointerViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadItemEffectPointer(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectPointerViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            EffectPointerBox.Value = _vm.EffectPointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.EffectPointer = (uint)(EffectPointerBox.Value ?? 0);
            _vm.WriteItemEffectPointer();
            CoreState.Services.ShowInfo("Item Effect Pointer data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
