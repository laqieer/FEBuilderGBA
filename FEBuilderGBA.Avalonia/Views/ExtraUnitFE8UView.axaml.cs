using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ExtraUnitFE8UView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly ExtraUnitFE8UViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Extra Unit (FE8U)";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Extra Unit (FE8U)", 1121, 735, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ExtraUnitFE8UView()
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
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ExtraUnitFE8UView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("ExtraUnitFE8UView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            FlagIdBox.Text = $"0x{_vm.FlagId:X08}";
            UnitInfoPtrBox.Text = $"0x{_vm.UnitInfoPtr:X08}";
        }

        void ReadFromUI()
        {
            _vm.FlagId = U.atoh(FlagIdBox.Text ?? "");
            _vm.UnitInfoPtr = U.atoh(UnitInfoPtrBox.Text ?? "");
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadFromUI();
            _undoService.Begin("Edit Extra Unit (FE8U)");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Extra unit data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("ExtraUnitFE8UView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
