using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OtherTextView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly OtherTextViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Other Text Strings";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Other Text Strings", 1166, 930, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public OtherTextView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
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
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("OtherTextView.LoadList failed: " + ex.ToString());
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
                Log.Error("OtherTextView.OnSelected failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            PointerLabel.Text = string.Format("0x{0:X08}", _vm.StringAddr);
            LengthLabel.Text = _vm.ByteLength.ToString();
            TextInput.Text = _vm.Text;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            uint current = _vm.CurrentAddr;
            _undoService.Begin(R._("Edit Other Text"));
            try
            {
                _vm.Text = TextInput.Text ?? "";
                if (_vm.Write(_undoService.GetActiveUndoData()))
                    _undoService.Commit();
                else
                    _undoService.Rollback();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("OtherTextView.OnWrite failed: " + ex.ToString());
                return;
            }

            // Refresh the list previews and re-select the edited entry.
            LoadList();
            EntryList.SelectAddress(current);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
