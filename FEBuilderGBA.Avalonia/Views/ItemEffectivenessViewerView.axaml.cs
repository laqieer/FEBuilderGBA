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
    /// Issue #368 — item-driven Avalonia Item Effectiveness Editor.
    /// Mirrors the WinForms <c>ItemEffectivenessForm</c>: outer EntryList of
    /// items, InnerList of classes per item, right-pane single-class editor,
    /// ItemListBox of items sharing the same effectiveness array, plus the
    /// IndependencePanel for forking shared arrays.
    /// </summary>
    public partial class ItemEffectivenessViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemEffectivenessViewerViewModel _vm = new();
        bool _hasLoadedList;

        // Inner-list rows are AddressListItem so we can render icons in a
        // ListBox; we keep a parallel List<AddrResult> for index lookup.
        readonly System.Collections.ObjectModel.ObservableCollection<AddressListItem> _innerDisplay = new();
        List<AddrResult> _innerData = new();

        readonly System.Collections.ObjectModel.ObservableCollection<AddressListItem> _sharedDisplay = new();
        List<AddrResult> _sharedData = new();

        public string ViewTitle => "Item Effectiveness";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Item Effectiveness Editor", 1297, 780, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public ItemEffectivenessViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnOuterSelected;
            InnerList.ItemsSource = _innerDisplay;
            ItemListBox.ItemsSource = _sharedDisplay;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
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
                Log.ErrorF("ItemEffectivenessViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnOuterSelected(uint itemAddr)
        {
            try
            {
                _vm.IsLoading = true;
                _innerData = _vm.LoadInnerClassList(itemAddr);
                _innerDisplay.Clear();
                for (int i = 0; i < _innerData.Count; i++)
                {
                    var bmp = ListIconLoaders.ClassIconLoader(_innerData, i);
                    _innerDisplay.Add(new AddressListItem { Text = _innerData[i].name, Icon = bmp });
                }

                _sharedData = _vm.LoadSharedOwners();
                _sharedDisplay.Clear();
                for (int i = 0; i < _sharedData.Count; i++)
                {
                    var bmp = ListIconLoaders.ItemIconLoader(_sharedData, i);
                    _sharedDisplay.Add(new AddressListItem { Text = _sharedData[i].name, Icon = bmp });
                }

                IndependencePanel.IsVisible = _vm.HasSharedOwners;

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
                Log.ErrorF("ItemEffectivenessViewerView.OnOuterSelected: {0}", ex.Message);
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

        void ItemListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int idx = ItemListBox.SelectedIndex;
            if (idx < 0 || idx >= _sharedData.Count) return;
            // Jump the outer list to the picked owner.
            EntryList.SelectAddress(_sharedData[idx].addr);
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
                TopBar.StartAddressText = $"0x{_vm.CurrentEffAddr:X08}";
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
                // Refresh the inner list so the new class name shows up.
                OnOuterSelected(_vm.CurrentItemAddr);
                CoreState.Services?.ShowInfo("Item Effectiveness data written.");
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEffectivenessViewerView.Write_Click: {0}", ex.Message);
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
                OnOuterSelected(_vm.CurrentItemAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEffectivenessViewerView.ListExpands_Click: {0}", ex.Message);
                CoreState.Services?.ShowError("Expand failed: " + ex.Message);
            }
        }

        void Independence_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.MakeCurrentItemIndependent();
                CoreState.Services?.ShowInfo("Item effectiveness array forked.");
                OnOuterSelected(_vm.CurrentItemAddr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEffectivenessViewerView.Independence_Click: {0}", ex.Message);
                CoreState.Services?.ShowError("Independence failed: " + ex.Message);
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
            catch (Exception ex) { Log.ErrorF("ItemEffectivenessViewerView.ClassId_Jump failed: {0}", ex.Message); }
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
            catch (Exception ex) { Log.ErrorF("ItemEffectivenessViewerView.ClassId_Pick failed: {0}", ex.Message); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // NameResolver returns a fallback on failure (Copilot review #638).
            ClassIdBox.NameText = NameResolver.GetClassName(e.NewValue);
            // Also refresh the icon when the user types a new class id. The
            // icon loader can throw on invalid id; keep the try/catch only here.
            try { ClassIconImage.Source = LoadClassIcon(e.NewValue); }
            catch { /* icon loader can fail on invalid id; leave prior icon */ }
        }

        /// <summary>
        /// Navigate to a target item. The caller may pass either the ITEM
        /// struct address (matches the outer list's stored key) OR an
        /// effectiveness-ARRAY address (the WinForms convention; the
        /// ItemEditorView jump button still passes the array address since
        /// the field is exposed as an effectiveness pointer). When the array
        /// address is given we translate it back to the owning item by
        /// scanning the item table for the first match on <c>+16</c>.
        /// PR #463 Copilot CLI review caught this — without the translation,
        /// jumps from ItemEditorView silently fall back to entry 0.
        /// </summary>
        public void NavigateTo(uint address)
        {
            // First try the direct ITEM-address path (the common case from
            // shared-owner jumps inside this editor).
            EntryList.SelectAddress(address);
            if (EntryList.SelectedItem?.addr == address) return;

            // Fallback: callers from ItemEditorView pass the effectiveness
            // array offset. Find the first item whose +16 dereferences here.
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;
            uint itemBase = rom.p32(rom.RomInfo.item_pointer);
            if (!U.isSafetyOffset(itemBase)) return;
            uint dataSize = rom.RomInfo.item_datasize;
            if (dataSize == 0) return;
            for (uint i = 0; i < 0x200; i++)
            {
                uint itemAddr = itemBase + i * dataSize;
                if (itemAddr + dataSize > (uint)rom.Data.Length) break;
                uint critPtr = rom.u32(itemAddr + 16);
                if (!U.isPointer(critPtr)) continue;
                if (U.toOffset(critPtr) == address)
                {
                    EntryList.SelectAddress(itemAddr);
                    return;
                }
            }
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
