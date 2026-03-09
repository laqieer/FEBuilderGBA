using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIASMCALLTALKView : Window, IEditorView
    {
        readonly AIASMCALLTALKViewModel _vm = new();

        public string ViewTitle => "AI ASM Call Talk";
        public bool IsLoaded => _vm.IsLoaded;

        public AIASMCALLTALKView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("AIASMCALLTALKView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("AIASMCALLTALKView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            FromUnitBox.Value = _vm.FromUnit;
            ToUnitBox.Value = _vm.ToUnit;
            Unused2Box.Value = _vm.Unused2;
            Unused3Box.Value = _vm.Unused3;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.FromUnit = (uint)(FromUnitBox.Value ?? 0);
                _vm.ToUnit = (uint)(ToUnitBox.Value ?? 0);
                _vm.Unused2 = (uint)(Unused2Box.Value ?? 0);
                _vm.Unused3 = (uint)(Unused3Box.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("AIASMCALLTALKView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
