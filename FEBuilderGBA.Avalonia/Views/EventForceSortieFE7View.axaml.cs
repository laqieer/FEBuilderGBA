using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventForceSortieFE7View : Window, IEditorView
    {
        readonly EventForceSortieFE7ViewModel _vm = new();

        public string ViewTitle => "Force Sortie (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventForceSortieFE7View()
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
                Log.Error("EventForceSortieFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventForceSortieFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            UnitListPointerUpDown.Value = _vm.UnitListPointer;
            UnitIdUpDown.Value = _vm.UnitId;
            Unknown1UpDown.Value = _vm.Unknown1;
            Unknown2UpDown.Value = _vm.Unknown2;
            Unknown3UpDown.Value = _vm.Unknown3;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.UnitListPointer = (uint)(UnitListPointerUpDown.Value ?? 0);
                _vm.UnitId = (uint)(UnitIdUpDown.Value ?? 0);
                _vm.Unknown1 = (uint)(Unknown1UpDown.Value ?? 0);
                _vm.Unknown2 = (uint)(Unknown2UpDown.Value ?? 0);
                _vm.Unknown3 = (uint)(Unknown3UpDown.Value ?? 0);
                _vm.Write();
                _vm.WriteSubEntry();
            }
            catch (Exception ex)
            {
                Log.Error("EventForceSortieFE7View.OnWrite failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
