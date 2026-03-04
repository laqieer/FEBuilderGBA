using System;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveCostFE6View : Window, IEditorView, IDataVerifiableView
    {
        readonly MoveCostFE6ViewModel _vm = new();

        public string ViewTitle => "Move Cost (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public MoveCostFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadClassList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostFE6View.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMoveCost(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ClassNameLabel.Text = _vm.ClassName;

            if (_vm.MoveCosts.Length == 0)
            {
                MoveCostsLabel.Text = "(no move cost data)";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < _vm.MoveCosts.Length; i++)
            {
                if (i > 0 && i % 10 == 0) sb.AppendLine();
                sb.Append($"[{i:X2}]={_vm.MoveCosts[i]:X2} ");
            }
            MoveCostsLabel.Text = sb.ToString();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
