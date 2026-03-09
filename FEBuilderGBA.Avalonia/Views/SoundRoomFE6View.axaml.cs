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
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            SongIdBox.Value = _vm.D0;
            SongNameBox.Value = _vm.D4;
            DescriptionBox.Value = _vm.D8;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.D0 = (uint)(SongIdBox.Value ?? 0);
            _vm.D4 = (uint)(SongNameBox.Value ?? 0);
            _vm.D8 = (uint)(DescriptionBox.Value ?? 0);
            _vm.WriteEntry();
            CoreState.Services.ShowInfo("Sound room (FE6) data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
