using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuDefinitionView : Window, IEditorView
    {
        readonly MenuDefinitionViewModel _vm = new();

        public string ViewTitle => "Menu Definition";
        public bool IsLoaded => _vm.IsLoaded;

        public MenuDefinitionView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMenuDefinitionList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MenuDefinitionView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMenuDefinition(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MenuDefinitionView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TextIdLabel.Text = $"0x{_vm.TextId:X04} ({_vm.TextId})";
            HandlerPtrLabel.Text = $"0x{_vm.HandlerPtr:X08}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
