using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventTalkGroupFE7View : Window, IEditorView
    {
        readonly EventTalkGroupFE7ViewModel _vm = new();

        public string ViewTitle => "Talk Group (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventTalkGroupFE7View()
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
                Log.Error("EventTalkGroupFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventTalkGroupFE7View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            TextIdUpDown.Value = _vm.TextId;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.TextId = (uint)(TextIdUpDown.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("EventTalkGroupFE7View.OnWrite failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
