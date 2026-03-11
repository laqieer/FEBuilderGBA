using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportAttributeView : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportAttributeViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Support Attribute";
        public bool IsLoaded => _vm.CanWrite;

        public SupportAttributeView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSupportAttributeList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SupportAttributeView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSupportAttribute(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportAttributeView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AffinityTypeBox.Value = _vm.AffinityType;
            AttackBonusBox.Value = _vm.AttackBonus;
            DefenseBonusBox.Value = _vm.DefenseBonus;
            HitBonusBox.Value = _vm.HitBonus;
            AvoidBonusBox.Value = _vm.AvoidBonus;
            CritBonusBox.Value = _vm.CritBonus;
            CritAvoidBonusBox.Value = _vm.CritAvoidBonus;
            Unknown7Box.Value = _vm.Unknown7;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Support Attribute");
            try
            {
                _vm.AffinityType = (uint)(AffinityTypeBox.Value ?? 0);
                _vm.AttackBonus = (uint)(AttackBonusBox.Value ?? 0);
                _vm.DefenseBonus = (uint)(DefenseBonusBox.Value ?? 0);
                _vm.HitBonus = (uint)(HitBonusBox.Value ?? 0);
                _vm.AvoidBonus = (uint)(AvoidBonusBox.Value ?? 0);
                _vm.CritBonus = (uint)(CritBonusBox.Value ?? 0);
                _vm.CritAvoidBonus = (uint)(CritAvoidBonusBox.Value ?? 0);
                _vm.Unknown7 = (uint)(Unknown7Box.Value ?? 0);
                _vm.WriteSupportAttribute();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Support attribute data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SupportAttributeView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
