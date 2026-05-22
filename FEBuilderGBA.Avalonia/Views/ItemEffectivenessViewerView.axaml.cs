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
    /// Issue #368 — item-driven Avalonia Item Effectiveness Editor.
    /// Mirrors the WinForms <c>ItemEffectivenessForm</c>: outer EntryList of
    /// items, InnerList of classes per item, right-pane single-class editor,
    /// ItemListBox of items sharing the same effectiveness array, plus the
    /// IndependencePanel for forking shared arrays.
    /// </summary>
    public partial class ItemEffectivenessViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;
        readonly ItemEffectivenessViewerViewModel _vm = new();

        // Inner-list rows are AddressListItem so we can render icons in a
        // ListBox; we keep a parallel List<AddrResult> for index lookup.
        readonly System.Collections.ObjectModel.ObservableCollection<AddressListItem> _innerDisplay = new();
        List<AddrResult> _innerData = new();

        readonly System.Collections.ObjectModel.ObservableCollection<AddressListItem> _sharedDisplay = new();
        List<AddrResult> _sharedData = new();

        public string ViewTitle => "Item Effectiveness";
        public bool IsLoaded => _vm.CanWrite;

        public ItemEffectivenessViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnOuterSelected;
            InnerList.ItemsSource = _innerDisplay;
            ItemListBox.ItemsSource = _sharedDisplay;
            Opened += (_, _) => LoadList();
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
                Log.Error("ItemEffectivenessViewerView.LoadList: {0}", ex.Message);
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
                Log.Error("ItemEffectivenessViewerView.OnOuterSelected: {0}", ex.Message);
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
            ClassIdInput.Value = _vm.ClassId;
            ClassNameLabel.Text = _vm.ClassName;
            ClassIconImage.Source = LoadClassIcon(_vm.ClassId);
        }

        void UpdateHeader()
        {
            AddressLabel.Text = $"0x{_vm.CurrentEffAddr:X08}";
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
                // Refresh the inner list so the new class name shows up.
                OnOuterSelected(_vm.CurrentItemAddr);
                CoreState.Services?.ShowInfo("Item Effectiveness data written.");
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessViewerView.Write_Click: {0}", ex.Message);
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
                OnOuterSelected(_vm.CurrentItemAddr);
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessViewerView.ListExpands_Click: {0}", ex.Message);
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
                Log.Error("ItemEffectivenessViewerView.Independence_Click: {0}", ex.Message);
                CoreState.Services?.ShowError("Independence failed: " + ex.Message);
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
