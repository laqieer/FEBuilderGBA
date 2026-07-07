using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTalkGroupFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventTalkGroupFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Talk Group (FE7)";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Talk Group (FE7)", 1252, 570, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);

        public event EventHandler? CloseRequested;
        public EventTalkGroupFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            TextIdUpDown.ValueChanged += OnTextIdChanged;
            WriteButton.Click += OnWrite;
            NewBlockButton.Click += OnNewBlock;        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                LoadList();

            }

        }

        void OnTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(TextIdUpDown.Value ?? 0);
            try { TextIdPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { TextIdPreview.Text = ""; }
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventTalkGroupFE7View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("EventTalkGroupFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            TextIdUpDown.Value = _vm.TextId;
            try { TextIdPreview.Text = _vm.TextId != 0 ? NameResolver.GetTextById(_vm.TextId) : ""; }
            catch { TextIdPreview.Text = ""; }
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Talk Group (FE7)"));
            try
            {
                _vm.TextId = (uint)(TextIdUpDown.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventTalkGroupFE7View.OnWrite failed: " + ex.ToString());
            }
        }

        // #1442 — "New Block" allocates a fresh 14×4=56-byte talk-group block in ROM
        // free space and repoints the editor onto it (parity with WinForms NewAlloc).
        // The append is undo-tracked via the UndoService scope (#1428 pattern).
        void OnNewBlock(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin(R._("New Block Talk Group (FE7)"));
            try
            {
                uint addr = _vm.NewAlloc();
                if (addr == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    Log.Error("EventTalkGroupFE7View.OnNewBlock: allocation failed (no free space)");
                    return;
                }
                _undoService.Commit();

                // Reload the list onto the new block and select its first entry.
                LoadList();
                EntryList.SelectFirst();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventTalkGroupFE7View.OnNewBlock failed: " + ex.ToString());
            }
        }

        // #1442 — Repoint onto an arbitrary block base (parity with WinForms
        // JumpToAddr → ReInit) so the editor can be driven from a POINTER_TALKGROUP
        // arg. If the target address is not inside the currently loaded block,
        // repoint the VM to that block's base and reload before selecting.
        public void NavigateTo(uint address)
        {
            if (address == 0 || address == U.NOT_FOUND)
            {
                EntryList.SelectAddress(address);
                return;
            }

            uint offset = U.toOffset(address);
            if (!EntryList.SelectAddress(offset))
            {
                // Out-of-range: treat the navigated address as the block base and
                // repoint the list onto it.
                _vm.SetBaseAddr(offset);
                LoadList();
                if (!EntryList.SelectAddress(offset))
                {
                    EntryList.SelectFirst();
                }
            }
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
