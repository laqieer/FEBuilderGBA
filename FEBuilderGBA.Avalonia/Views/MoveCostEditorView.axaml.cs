using System;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MoveCostEditorView : Window, IEditorView
    {
        readonly MoveCostEditorViewModel _vm = new();

        public string ViewTitle => "Move Cost Editor";
        public bool IsLoaded => _vm.CanWrite;

        public MoveCostEditorView()
        {
            InitializeComponent();
            ClassList.SelectedAddressChanged += OnClassSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadClassList();
                ClassList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostEditorView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnClassSelected(uint addr)
        {
            try
            {
                _vm.LoadMoveCost(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MoveCostEditorView.OnClassSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            ClassList.SelectAddress(address);
        }

        void UpdateUI()
        {
            ClassNameLabel.Text = _vm.ClassName;
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            if (_vm.MoveCosts.Length == 0)
            {
                MoveCostBlock.Text = "(No move cost data found)";
                return;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < _vm.MoveCosts.Length; i++)
            {
                if (i > 0 && i % 8 == 0) sb.AppendLine();
                sb.Append($"[{i:X2}]={_vm.MoveCosts[i]:X2}  ");
            }
            MoveCostBlock.Text = sb.ToString();
        }

        public void SelectFirstItem()
        {
            ClassList.SelectFirst();
        }
    }
}
