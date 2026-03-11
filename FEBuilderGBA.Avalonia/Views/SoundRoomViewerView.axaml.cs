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
        readonly UndoService _undoService = new();

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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSoundRoomList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SoundRoomViewerView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSoundRoom(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SoundRoomViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
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

            _undoService.Begin("Edit Sound Room");
            try
            {
                _vm.SongId = (uint)(SongIdBox.Value ?? 0);
                _vm.Raw4 = (uint)(Raw4Box.Value ?? 0);
                _vm.Raw8 = (uint)(Raw8Box.Value ?? 0);
                _vm.TextId = (uint)(TextIdBox.Value ?? 0);
                _vm.WriteSoundRoom();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Sound room data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SoundRoomViewerView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
