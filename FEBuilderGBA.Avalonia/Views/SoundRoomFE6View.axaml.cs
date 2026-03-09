using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundRoomFE6View : Window, IEditorView
    {
        readonly SoundRoomFE6ViewModel _vm = new();

        public string ViewTitle => "Sound Room (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public SoundRoomFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
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
                Log.Error("SoundRoomFE6View.LoadList failed: {0}", ex.Message);
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
                Log.Error("SoundRoomFE6View.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            BgmIdBox.Value = _vm.BgmId;
            SongNameBox.Value = _vm.SongNameTextId;
            DescriptionBox.Value = _vm.DescriptionTextId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.BgmId = (uint)(BgmIdBox.Value ?? 0);
            _vm.SongNameTextId = (uint)(SongNameBox.Value ?? 0);
            _vm.DescriptionTextId = (uint)(DescriptionBox.Value ?? 0);
            _vm.Write();
            CoreState.Services.ShowInfo("Sound room (FE6) data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
