using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapTerrainNameEngView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapTerrainNameEngViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Terrain Name (English)";
        public new bool IsLoaded => _vm.IsLoaded;

        public EditorDescriptor Descriptor => new("Terrain Name (English)", 1253, 790, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public MapTerrainNameEngView()
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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapTerrainNameEngView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapTerrainNameEngView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            TerrainNameTextIDBox.Value = _vm.TerrainNameTextID;
            // Resolve terrain name text
            NameLabel.Text = _vm.TerrainNameTextID > 0
                ? NameResolver.GetTextById(_vm.TerrainNameTextID)
                : "(none)";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _undoService.Begin("Edit Terrain Name (English)");
            try
            {
                _vm.TerrainNameTextID = (uint)(TerrainNameTextIDBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Terrain Name (English) data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("MapTerrainNameEngView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
