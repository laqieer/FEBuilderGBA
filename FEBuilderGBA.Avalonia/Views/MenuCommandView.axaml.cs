using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuCommandView : Window, IEditorView
    {
        readonly MenuCommandViewModel _vm = new();

        public string ViewTitle => "Menu Command";
        public bool IsLoaded => _vm.IsLoaded;

        public MenuCommandView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadMenuCommandList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadMenuCommand(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UsabilityPtrLabel.Text = $"0x{_vm.UsabilityPtr:X08}";
            EffectPtrLabel.Text = $"0x{_vm.EffectPtr:X08}";
            MenuCmdIdLabel.Text = $"0x{_vm.MenuCommandId:X04} ({_vm.MenuCommandId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
