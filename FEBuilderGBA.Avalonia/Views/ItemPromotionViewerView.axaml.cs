using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    public partial class ItemPromotionViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemPromotionViewerViewModel _vm = new();
        bool _hasLoadedList;

        readonly System.Collections.ObjectModel.ObservableCollection<AddressListItem> _innerDisplay = new();
        List<AddrResult> _innerData = new();

        public string ViewTitle => "Item Promotion";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Item Promotion Editor", 1180, 720, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public ItemPromotionViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnOuterSelected;
            InnerList.ItemsSource = _innerDisplay;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList(); CheckIERPatch();
            }
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
                Log.ErrorF("ItemPromotionViewerView.LoadList: {0}", ex.Message);
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
                Log.ErrorF("ItemPromotionViewerView.CheckIERPatch: {0}", ex.Message);
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
                Log.ErrorF("ItemPromotionViewerView.OnOuterSelected: {0}", ex.Message);
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
            ClassIdBox.Value = _vm.ClassId;
            // NameResolver returns a fallback on failure (Copilot review #638).
            ClassIdBox.NameText = _vm.ClassName ?? NameResolver.GetClassName(_vm.ClassId);
            ClassIconImage.Source = LoadClassIcon(_vm.ClassId);
        }

        void UpdateHeader()
        {
            // #649: address/size labels live on the unified EditorTopBar.
            if (TopBar != null)
            {
                TopBar.StartAddressText = $"0x{_vm.CurrentArrayAddr:X08}";
                TopBar.SizeText = "1";
            }
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
                _vm.ClassId = ClassIdBox.Value;
                _vm.ClassName = NameResolver.GetClassName(_vm.ClassId);
                _vm.WriteCurrentClassByte();
                _vm.MarkClean();
                OnOuterSelected(_vm.CurrentPointerAddr);
                CoreState.Services?.ShowInfo("Item Promotion data written.");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemPromotionViewerView.Write_Click: {0}", ex.Message);
                CoreState.Services?.ShowError("Write failed: " + ex.Message);
            }
        }

        // #649: routed event from the unified EditorTopBar Reload button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
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
                Log.ErrorF("ItemPromotionViewerView.ListExpands_Click: {0}", ex.Message);
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
                Log.ErrorF("ItemPromotionViewerView.OpenPatchManager_Click: {0}", ex.Message);
            }
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + classId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void ClassId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("ItemPromotionViewerView.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr, TopLevel.GetTopLevel(this) as Window);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) ClassIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("ItemPromotionViewerView.ClassId_Pick failed: {0}", ex.Message); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // NameResolver returns a fallback on failure (Copilot review #638).
            ClassIdBox.NameText = NameResolver.GetClassName(e.NewValue);
            try { ClassIconImage.Source = LoadClassIcon(e.NewValue); }
            catch { /* icon loader can fail on invalid id; leave prior icon */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
