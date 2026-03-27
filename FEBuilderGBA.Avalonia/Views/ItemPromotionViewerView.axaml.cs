using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemPromotionViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemPromotionViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Item Promotion";
        public bool IsLoaded => _vm.CanWrite;

        public ItemPromotionViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemPromotionList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemPromotion(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("ItemPromotionViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TargetClassIdBox.Value = _vm.TargetClassId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.TargetClassId = (uint)(TargetClassIdBox.Value ?? 0);

            _undoService.Begin("Edit Item Promotion");
            try
            {
                _vm.WriteItemPromotion();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item Promotion data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("Write failed: {0}", ex.Message);
            }
        }

        void OnTargetClassIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint classId = (uint)(TargetClassIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.class_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + classId * rom.RomInfo.class_datasize;
                if (rom.RomInfo.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("OnTargetClassIdLinkClick failed: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
