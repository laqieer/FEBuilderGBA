using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIUnitsView : Window, IEditorView
    {
        readonly AIUnitsViewModel _vm = new();

        public string ViewTitle => "AI Units Evaluation";
        public bool IsLoaded => _vm.IsLoaded;

        public AIUnitsView()
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
                Log.Error("AIUnitsView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AIUnitsView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            UnitBox.Value = _vm.Unit;
            Unknown1Box.Value = _vm.Unknown1;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Unit = (uint)(UnitBox.Value ?? 0);
                _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("AIUnitsView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
