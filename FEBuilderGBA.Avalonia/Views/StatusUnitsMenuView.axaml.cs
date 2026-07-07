using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusUnitsMenuView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly StatusUnitsMenuViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Status Units Menu";
        public new bool IsLoaded => _vm.CanWrite;

        public EditorDescriptor Descriptor => new("Status Units Menu Editor", 1238, 806, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public StatusUnitsMenuView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            ItemNameTextIdBox.ValueChanged += OnItemNameTextIdChanged;
            RMenuTextIdBox.ValueChanged += OnRMenuTextIdChanged;
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

        void OnItemNameTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(ItemNameTextIdBox.Value ?? 0);
            try { ItemNameTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { ItemNameTextPreview.Text = ""; }
        }

        void OnRMenuTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(RMenuTextIdBox.Value ?? 0);
            try { RMenuTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { RMenuTextPreview.Text = ""; }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadStatusUnitsMenuList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("StatusUnitsMenuView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadStatusUnitsMenu(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("StatusUnitsMenuView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            OrderBox.Value = _vm.Order;
            ItemNameTextIdBox.Value = _vm.ItemNameTextId;
            try { ItemNameTextPreview.Text = _vm.ItemNameTextId != 0 ? NameResolver.GetTextById(_vm.ItemNameTextId) : ""; }
            catch { ItemNameTextPreview.Text = ""; }
            ReferenceDataBox.Value = _vm.ReferenceData;
            RMenuTextIdBox.Value = _vm.RMenuTextId;
            try { RMenuTextPreview.Text = _vm.RMenuTextId != 0 ? NameResolver.GetTextById(_vm.RMenuTextId) : ""; }
            catch { RMenuTextPreview.Text = ""; }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Status Units Menu");
            try
            {
                _vm.Order = (uint)(OrderBox.Value ?? 0);
                _vm.ItemNameTextId = (uint)(ItemNameTextIdBox.Value ?? 0);
                _vm.ReferenceData = (uint)(ReferenceDataBox.Value ?? 0);
                _vm.RMenuTextId = (uint)(RMenuTextIdBox.Value ?? 0);
                _vm.WriteStatusUnitsMenu();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status units menu data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("StatusUnitsMenuView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
