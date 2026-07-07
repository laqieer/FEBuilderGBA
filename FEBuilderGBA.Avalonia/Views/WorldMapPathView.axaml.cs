using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapPathView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly WorldMapPathViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "World Map Paths";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("World Map Paths", 1284, 787, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public WorldMapPathView()
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
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("WorldMapPathView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("WorldMapPathView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            PathDataPointerBox.Text = $"0x{_vm.PathDataPointer:X08}";
            StartBasePointIdBox.Value = _vm.StartBasePointId;
            EndBasePointIdBox.Value = _vm.EndBasePointId;
            Padding6Box.Value = _vm.Padding6;
            Padding7Box.Value = _vm.Padding7;
            PathMovePointerBox.Text = $"0x{_vm.PathMovePointer:X08}";
        }

        void ReadFromUI()
        {
            _vm.PathDataPointer = U.atoh(PathDataPointerBox.Text ?? "");
            _vm.StartBasePointId = (uint)(StartBasePointIdBox.Value ?? 0);
            _vm.EndBasePointId = (uint)(EndBasePointIdBox.Value ?? 0);
            _vm.Padding6 = (uint)(Padding6Box.Value ?? 0);
            _vm.Padding7 = (uint)(Padding7Box.Value ?? 0);
            _vm.PathMovePointer = U.atoh(PathMovePointerBox.Text ?? "");
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _undoService.Begin("Edit World Map Path");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("World map path data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapPathView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
