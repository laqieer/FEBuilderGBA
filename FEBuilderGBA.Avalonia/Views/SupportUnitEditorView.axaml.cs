using System;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportUnitEditorView : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportUnitEditorViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Support Unit Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public SupportUnitEditorView()
        {
            InitializeComponent();
            SupportList.SelectedAddressChanged += OnSupportSelected;
            WriteButton.Click += Write_Click;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSupportUnitList();
                SupportList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SupportUnitEditorView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSupportSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSupportUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportUnitEditorView.OnSupportSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        public void NavigateTo(uint address)
        {
            SupportList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            Partner1Nud.Value = _vm.Partner1;
            Partner2Nud.Value = _vm.Partner2;
            Partner3Nud.Value = _vm.Partner3;
            Partner4Nud.Value = _vm.Partner4;
            Partner5Nud.Value = _vm.Partner5;
            Partner6Nud.Value = _vm.Partner6;
            Partner7Nud.Value = _vm.Partner7;

            InitialValue1Nud.Value = _vm.InitialValue1;
            InitialValue2Nud.Value = _vm.InitialValue2;
            InitialValue3Nud.Value = _vm.InitialValue3;
            InitialValue4Nud.Value = _vm.InitialValue4;
            InitialValue5Nud.Value = _vm.InitialValue5;
            InitialValue6Nud.Value = _vm.InitialValue6;
            InitialValue7Nud.Value = _vm.InitialValue7;

            GrowthRate1Nud.Value = _vm.GrowthRate1;
            GrowthRate2Nud.Value = _vm.GrowthRate2;
            GrowthRate3Nud.Value = _vm.GrowthRate3;
            GrowthRate4Nud.Value = _vm.GrowthRate4;
            GrowthRate5Nud.Value = _vm.GrowthRate5;
            GrowthRate6Nud.Value = _vm.GrowthRate6;
            GrowthRate7Nud.Value = _vm.GrowthRate7;

            PartnerCountNud.Value = _vm.PartnerCount;
            Separator1Nud.Value = _vm.Separator1;
            Separator2Nud.Value = _vm.Separator2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Support Unit");
            try
            {
                _vm.Partner1 = (uint)(Partner1Nud.Value ?? 0);
                _vm.Partner2 = (uint)(Partner2Nud.Value ?? 0);
                _vm.Partner3 = (uint)(Partner3Nud.Value ?? 0);
                _vm.Partner4 = (uint)(Partner4Nud.Value ?? 0);
                _vm.Partner5 = (uint)(Partner5Nud.Value ?? 0);
                _vm.Partner6 = (uint)(Partner6Nud.Value ?? 0);
                _vm.Partner7 = (uint)(Partner7Nud.Value ?? 0);

                _vm.InitialValue1 = (uint)(InitialValue1Nud.Value ?? 0);
                _vm.InitialValue2 = (uint)(InitialValue2Nud.Value ?? 0);
                _vm.InitialValue3 = (uint)(InitialValue3Nud.Value ?? 0);
                _vm.InitialValue4 = (uint)(InitialValue4Nud.Value ?? 0);
                _vm.InitialValue5 = (uint)(InitialValue5Nud.Value ?? 0);
                _vm.InitialValue6 = (uint)(InitialValue6Nud.Value ?? 0);
                _vm.InitialValue7 = (uint)(InitialValue7Nud.Value ?? 0);

                _vm.GrowthRate1 = (uint)(GrowthRate1Nud.Value ?? 0);
                _vm.GrowthRate2 = (uint)(GrowthRate2Nud.Value ?? 0);
                _vm.GrowthRate3 = (uint)(GrowthRate3Nud.Value ?? 0);
                _vm.GrowthRate4 = (uint)(GrowthRate4Nud.Value ?? 0);
                _vm.GrowthRate5 = (uint)(GrowthRate5Nud.Value ?? 0);
                _vm.GrowthRate6 = (uint)(GrowthRate6Nud.Value ?? 0);
                _vm.GrowthRate7 = (uint)(GrowthRate7Nud.Value ?? 0);

                _vm.PartnerCount = (uint)(PartnerCountNud.Value ?? 0);
                _vm.Separator1 = (uint)(Separator1Nud.Value ?? 0);
                _vm.Separator2 = (uint)(Separator2Nud.Value ?? 0);

                _vm.WriteSupportUnit();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SupportUnitEditorView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void SelectFirstItem()
        {
            SupportList.SelectFirst();
        }
    }
}
