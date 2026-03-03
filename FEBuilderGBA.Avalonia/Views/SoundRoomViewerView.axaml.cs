using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundRoomViewerView : Window, IEditorView
    {
        readonly SoundRoomViewerViewModel _vm = new();

        public string ViewTitle => "Sound Room";
        public bool IsLoaded => _vm.IsLoaded;

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
            SongIdLabel.Text = $"0x{_vm.SongId:X04} ({_vm.SongId})";
            Raw4Label.Text = $"0x{_vm.Raw4:X08}";
            Raw8Label.Text = $"0x{_vm.Raw8:X08}";
            TextIdLabel.Text = $"0x{_vm.TextId:X04} ({_vm.TextId})";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
