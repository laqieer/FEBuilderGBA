using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusOptionOrderView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly StatusOptionOrderViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Status Option Order";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Status Option Order Editor", 1238, 806, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public StatusOptionOrderView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadStatusOptionOrderList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("StatusOptionOrderView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadStatusOptionOrder(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("StatusOptionOrderView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            OptionIdBox.Value = _vm.OptionId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Status Option Order");
            try
            {
                _vm.OptionId = (uint)(OptionIdBox.Value ?? 0);
                _vm.WriteStatusOptionOrder();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status option order data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("StatusOptionOrderView.Write: {0}", ex.Message); }
        }

        // リストの拡張 — expand the 1-byte-per-entry game-option-order list by a
        // prompted slot count and raise the order-count byte at
        // status_game_option_order_count_address (WinForms
        // StatusOptionOrderForm.AddressListExpandsEvent). The whole expand runs
        // under ONE UndoService scope; StatusOptionOrderViewModel.ExpandList
        // validates-before-mutate and any inner throw rolls back byte-identically.
        // Mirrors MapSettingFE6View.OnExpandListClick.
        async void ExpandList_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.ROM == null)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }

                uint current = _vm.ReadCount;
                if (current == 0)
                {
                    // Seed from a list load if the count hasn't been resolved yet.
                    current = (uint)_vm.LoadStatusOptionOrderList().Count;
                }
                if (current == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: the game option order list is empty."));
                    return;
                }

                // Default = current + 1; the count is a single byte, so max = 255.
                uint defaultCount = current + 1;
                if (defaultCount > 255) defaultCount = 255;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new game option order slot count (current: {0}, max: 255).", current),
                    R._("List Expansion"),
                    defaultCount,
                    current,
                    255);
                if (chosen == null) return; // user cancelled

                uint newCount = chosen.Value;
                if (newCount <= current)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count must be greater than the current count."));
                    return;
                }

                _undoService.Begin("Expand Status Option Order");
                try
                {
                    string err = _vm.ExpandList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded game option order list to {0} slots.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    // Log.Error joins params with spaces (no {0} composite formatting),
                    // so interpolate the message ourselves.
                    Log.Error($"StatusOptionOrderView.ExpandList_Click inner failed: {inner}");
                    CoreState.Services?.ShowError(R._("Game option order list expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StatusOptionOrderView.ExpandList_Click failed: {ex}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
