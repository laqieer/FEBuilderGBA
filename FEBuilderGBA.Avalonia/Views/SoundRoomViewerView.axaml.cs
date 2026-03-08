using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundRoomViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SoundRoomViewerViewModel _vm = new();

        public string ViewTitle => "Sound Room";
        public bool IsLoaded => _vm.CanWrite;

        public SoundRoomViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSoundRoomList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SoundRoomViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSoundRoom(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SoundRoomViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SongIdBox.Value = _vm.SongId;
            Raw4Box.Value = _vm.Raw4;
            Raw8Box.Value = _vm.Raw8;
            TextIdBox.Value = _vm.TextId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.SongId = (uint)(SongIdBox.Value ?? 0);
            _vm.Raw4 = (uint)(Raw4Box.Value ?? 0);
            _vm.Raw8 = (uint)(Raw8Box.Value ?? 0);
            _vm.TextId = (uint)(TextIdBox.Value ?? 0);
            _vm.WriteSoundRoom();
            CoreState.Services.ShowInfo("Sound room data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
