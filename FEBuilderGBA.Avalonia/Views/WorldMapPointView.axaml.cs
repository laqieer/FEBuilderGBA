using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapPointView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly WorldMapPointViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "World Map Point";
        public bool IsLoaded => _vm.CanWrite;

        public WorldMapPointView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            NameTextIdBox.ValueChanged += OnNameTextIdChanged;
            Opened += (_, _) => LoadList();
        }

        void OnNameTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(NameTextIdBox.Value ?? 0);
            try { NameTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { NameTextPreview.Text = ""; }
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadWorldMapPointList();
                EntryList.SetItems(items);
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("WorldMapPointView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadWorldMapPoint(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("WorldMapPointView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AlwaysAccessibleBox.Value = _vm.AlwaysAccessible;
            FreeMapTypeBox.Value = _vm.FreeMapType;
            PreClearIconBox.Value = _vm.PreClearIcon;
            PostClearIconBox.Value = _vm.PostClearIcon;
            ChapterId1Box.Value = _vm.ChapterId1;
            ChapterId2Box.Value = _vm.ChapterId2;
            EventBranchFlagBox.Value = _vm.EventBranchFlag;
            NextNodeEirikaBox.Value = _vm.NextNodeEirika;
            NextNodeEphraimBox.Value = _vm.NextNodeEphraim;
            NextNodeEirika2ndBox.Value = _vm.NextNodeEirika2nd;
            NextNodeEphraim2ndBox.Value = _vm.NextNodeEphraim2nd;
            ArmoryPointerBox.Text = $"0x{_vm.ArmoryPointer:X08}";
            VendorPointerBox.Text = $"0x{_vm.VendorPointer:X08}";
            SecretShopPointerBox.Text = $"0x{_vm.SecretShopPointer:X08}";
            CoordinateXBox.Value = _vm.CoordinateX;
            CoordinateYBox.Value = _vm.CoordinateY;
            NameTextIdBox.Value = _vm.NameTextId;
            try { NameTextPreview.Text = _vm.NameTextId != 0 ? NameResolver.GetTextById(_vm.NameTextId) : ""; }
            catch { NameTextPreview.Text = ""; }
            ShipSettingBox.Value = _vm.ShipSetting;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.AlwaysAccessible = (uint)(AlwaysAccessibleBox.Value ?? 0);
            _vm.FreeMapType = (uint)(FreeMapTypeBox.Value ?? 0);
            _vm.PreClearIcon = (uint)(PreClearIconBox.Value ?? 0);
            _vm.PostClearIcon = (uint)(PostClearIconBox.Value ?? 0);
            _vm.ChapterId1 = (uint)(ChapterId1Box.Value ?? 0);
            _vm.ChapterId2 = (uint)(ChapterId2Box.Value ?? 0);
            _vm.EventBranchFlag = (uint)(EventBranchFlagBox.Value ?? 0);
            _vm.NextNodeEirika = (uint)(NextNodeEirikaBox.Value ?? 0);
            _vm.NextNodeEphraim = (uint)(NextNodeEphraimBox.Value ?? 0);
            _vm.NextNodeEirika2nd = (uint)(NextNodeEirika2ndBox.Value ?? 0);
            _vm.NextNodeEphraim2nd = (uint)(NextNodeEphraim2ndBox.Value ?? 0);
            _vm.ArmoryPointer = U.atoh(ArmoryPointerBox.Text ?? "");
            _vm.VendorPointer = U.atoh(VendorPointerBox.Text ?? "");
            _vm.SecretShopPointer = U.atoh(SecretShopPointerBox.Text ?? "");
            _vm.CoordinateX = (uint)(CoordinateXBox.Value ?? 0);
            _vm.CoordinateY = (uint)(CoordinateYBox.Value ?? 0);
            _vm.NameTextId = (uint)(NameTextIdBox.Value ?? 0);
            _vm.ShipSetting = (uint)(ShipSettingBox.Value ?? 0);

            _undoService.Begin("Edit World Map Point");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("World map point data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapPointView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
