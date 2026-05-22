using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Issue #368 — CC-item-driven Avalonia Item Promotion Editor.
    /// Mirrors the WinForms <c>ItemPromotionForm</c>: outer EntryList of fixed
    /// CC items, InnerList of classes per CC item, right-pane single-class
    /// editor. When the IER patch is detected the X_IER_Patch warning panel
    /// appears with a button that opens Patch Manager.
    /// </summary>
    public partial class ItemPromotionViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemPromotionViewerViewModel _vm = new();

        readonly System.Collections.ObjectModel.ObservableCollection<AddressListItem> _innerDisplay = new();
        List<AddrResult> _innerData = new();

        public string ViewTitle => "Item Promotion";
        public bool IsLoaded => _vm.CanWrite;

        public ItemPromotionViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnOuterSelected;
            InnerList.ItemsSource = _innerDisplay;
            Opened += (_, _) => { LoadList(); CheckIERPatch(); };
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadItemList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.LoadList: {0}", ex.Message);
            }
        }

        void CheckIERPatch()
        {
            try
            {
                bool ier = PatchDetectionService.Instance.ItemEffectRange;
                X_IER_PatchPanel.IsVisible = ier;
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.CheckIERPatch: {0}", ex.Message);
            }
        }

        void OnOuterSelected(uint pointerAddr)
        {
            try
            {
                _vm.IsLoading = true;
                _innerData = _vm.LoadInnerClassList(pointerAddr);
                _innerDisplay.Clear();
                for (int i = 0; i < _innerData.Count; i++)
                {
                    var bmp = ListIconLoaders.ClassIconLoader(_innerData, i);
                    _innerDisplay.Add(new AddressListItem { Text = _innerData[i].name, Icon = bmp });
                }

                if (_innerDisplay.Count > 0)
                {
                    InnerList.SelectedIndex = 0;
                }
                else
                {
                    _vm.CurrentClassAddr = 0;
                    _vm.ClassId = 0;
                    _vm.ClassName = "";
                    UpdateRightPanel();
                }
                UpdateHeader();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error("ItemPromotionViewerView.OnOuterSelected: {0}", ex.Message);
            }
        }

        void InnerList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int idx = InnerList.SelectedIndex;
            if (idx < 0 || idx >= _innerData.Count) return;
            _vm.LoadClassByte(_innerData[idx].addr);
            UpdateRightPanel();
            UpdateHeader();
        }

        void UpdateRightPanel()
        {
            ClassIdInput.Value = _vm.ClassId;
            ClassNameLabel.Text = _vm.ClassName;
            ClassIconImage.Source = LoadClassIcon(_vm.ClassId);
        }

        void UpdateHeader()
        {
            AddressLabel.Text = $"0x{_vm.CurrentArrayAddr:X08}";
            BlockSizeLabel.Text = "1";
            SelectAddressLabel.Text = $"0x{_vm.CurrentClassAddr:X08}";
        }

        static Bitmap? LoadClassIcon(uint classId)
        {
            if (classId == 0) return null;
            try
            {
                using var img = PreviewIconHelper.LoadClassWaitIconByClassId(classId);
                return ImageConversionHelper.ToAvaloniaBitmap(img);
            }
            catch { return null; }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.ClassId = (uint)(ClassIdInput.Value ?? 0);
                _vm.ClassName = NameResolver.GetClassName(_vm.ClassId);
                _vm.WriteCurrentClassByte();
                _vm.MarkClean();
                OnOuterSelected(_vm.CurrentPointerAddr);
                CoreState.Services?.ShowInfo("Item Promotion data written.");
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.Write_Click: {0}", ex.Message);
                CoreState.Services?.ShowError("Write failed: " + ex.Message);
            }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            LoadList();
        }

        void ListExpands_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.ExpandCurrentList();
                CoreState.Services?.ShowInfo("Added a new class slot.");
                OnOuterSelected(_vm.CurrentPointerAddr);
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.ListExpands_Click: {0}", ex.Message);
                CoreState.Services?.ShowError("Expand failed: " + ex.Message);
            }
        }

        void OpenPatchManager_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                WindowManager.Instance.Open<PatchManagerView>();
            }
            catch (Exception ex)
            {
                Log.Error("ItemPromotionViewerView.OpenPatchManager_Click: {0}", ex.Message);
            }
        }

        void OnClassIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint classId = (uint)(ClassIdInput.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.class_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + classId * rom.RomInfo.class_datasize;
                if (rom.RomInfo.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("OnClassIdLinkClick failed: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
