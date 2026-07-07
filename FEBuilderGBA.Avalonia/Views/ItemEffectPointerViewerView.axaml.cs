using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEffectPointerViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemEffectPointerViewerViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Item Effect Pointer";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("Item Effect Pointer Editor", 1185, 658, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ItemEffectPointerViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;        }


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
                var items = _vm.LoadItemEffectPointerList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemEffectPointerViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemEffectPointer(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemEffectPointerViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            EffectPointerBox.Text = $"0x{_vm.EffectPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.EffectPointer = ParseHexText(EffectPointerBox.Text);

            _undoService.Begin("Edit Item Effect Pointer");
            try
            {
                _vm.WriteItemEffectPointer();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item Effect Pointer data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
