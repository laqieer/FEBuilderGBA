using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIPerformItemView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly AIPerformItemViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "AI Item Performance";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("AI Item Performance", 800, 500, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public AIPerformItemView()
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
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconFromAddrU16Loader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIPerformItemView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIPerformItemView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            ItemBox.Value = _vm.Item;
            Unused2Box.Value = _vm.Unused2;
            AsmPointerBox.Value = _vm.AsmPointer;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit AI Item Performance");
            try
            {
                _vm.Item = (uint)(ItemBox.Value ?? 0);
                _vm.Unused2 = (uint)(Unused2Box.Value ?? 0);
                _vm.AsmPointer = (uint)(AsmPointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("AIPerformItemView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
