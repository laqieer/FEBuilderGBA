using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundRoomViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SoundRoomViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Sound Room";
        public bool IsLoaded => _vm.CanWrite;

        public SoundRoomViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            TextIdBox.ValueChanged += OnTextIdChanged;
            Opened += (_, _) => LoadList();
        }

        void OnTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(TextIdBox.Value ?? 0);
            try { TextIdPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { TextIdPreview.Text = ""; }
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
            try { TextIdPreview.Text = _vm.TextId != 0 ? NameResolver.GetTextById(_vm.TextId) : ""; }
            catch { TextIdPreview.Text = ""; }
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

        void OnSongIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            JumpToSong_Click(sender, new RoutedEventArgs());
        }

        void JumpToSong_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint songId = (uint)(SongIdBox.Value ?? 0);
                uint ptr = rom.RomInfo.sound_table_pointer;
                if (ptr == 0) return;
                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + songId * 8;
                WindowManager.Instance.Navigate<SongTableView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error("JumpToSong failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
