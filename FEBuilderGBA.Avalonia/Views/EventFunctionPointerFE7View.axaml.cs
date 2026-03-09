using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventFunctionPointerFE7View : Window, IEditorView
    {
        readonly EventFunctionPointerFE7ViewModel _vm = new();

        public string ViewTitle => "Event Function Pointer (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventFunctionPointerFE7View()
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
                Log.Error("EventFunctionPointerFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventFunctionPointerFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            FuncPointerUpDown.Value = _vm.EventCommandFunctionPointer;
            Unknown4UpDown.Value = _vm.Unknown4;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.EventCommandFunctionPointer = (uint)(FuncPointerUpDown.Value ?? 0);
                _vm.Unknown4 = (uint)(Unknown4UpDown.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("EventFunctionPointerFE7View.OnWrite failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
