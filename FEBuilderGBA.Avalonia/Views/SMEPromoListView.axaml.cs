using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SMEPromoListView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly SMEPromoListViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "SME Promo List";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("SME Promo List", 1155, 655, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public SMEPromoListView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
            // #649: seed the unified EditorTopBar inputs from the VM so the
            // initial render shows the same values the legacy {Binding}
            // approach would have shown.
            TopBar.ReadStartAddress = (uint)_vm.ReadStartAddress;
            TopBar.ReadCount = _vm.ReadCount;
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SMEPromoListViewModel.ReadStartAddress))
                    TopBar.ReadStartAddress = (uint)_vm.ReadStartAddress;
                else if (e.PropertyName == nameof(SMEPromoListViewModel.ReadCount))
                    TopBar.ReadCount = _vm.ReadCount;
            };
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            _vm.Reload();
        }

        // #649: routed event from the unified EditorTopBarWithInputs Reload
        // button. Push the bar's editable values back into the VM, then
        // reload — matches the legacy two-way binding behavior.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            _vm.ReadStartAddress = (int)TopBar.ReadStartAddress;
            _vm.ReadCount = TopBar.ReadCount;
            _vm.Reload();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit SME Promo");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("SME Promo data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SMEPromoListView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            _vm.InitializeWithAddress(address);
        }

        public void SelectFirstItem()
        {
            if (_vm.AddressList.Count > 0)
                _vm.SelectedIndex = 0;
        }
    }
}
