using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class CCBranchEditorView : Window, IEditorView, IDataVerifiableView
    {
        readonly CCBranchEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "CC Branch Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public CCBranchEditorView()
        {
            InitializeComponent();
            BranchList.SelectedAddressChanged += OnBranchSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadCCBranchList();
                BranchList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("CCBranchEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnBranchSelected(uint addr)
        {
            try
            {
                _vm.LoadCCBranch(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("CCBranchEditorView.OnBranchSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            BranchList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            Promo1Box.Value = _vm.PromotionClass1;
            Promo2Box.Value = _vm.PromotionClass2;
            Promo1NameLabel.Text = _vm.PromoName1;
            Promo2NameLabel.Text = _vm.PromoName2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.PromotionClass1 = (uint)(Promo1Box.Value ?? 0);
            _vm.PromotionClass2 = (uint)(Promo2Box.Value ?? 0);

            _undoService.Begin("Edit CC Branch");
            try
            {
                _vm.WriteCCBranch();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("CC Branch data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("CCBranchEditorView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            BranchList.SelectFirst();
        }
    }
}
