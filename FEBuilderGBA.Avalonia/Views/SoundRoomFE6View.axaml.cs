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
        readonly UndoService _undoService = new();

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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SoundRoomFE6View.LoadList failed: {0}", ex.Message);
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
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SoundRoomFE6View.OnSelected failed: {0}", ex.Message);
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
            BgmIdBox.Value = _vm.BgmId;
            SongNameBox.Value = _vm.SongNameTextId;
            DescriptionBox.Value = _vm.DescriptionTextId;
            SongNamePreview.Text = _vm.SongNamePreview;
            DescriptionPreview.Text = _vm.DescriptionPreview;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin("Edit Sound Room (FE6)");
            try
            {
                _vm.BgmId = (uint)(BgmIdBox.Value ?? 0);
                _vm.SongNameTextId = (uint)(SongNameBox.Value ?? 0);
                _vm.DescriptionTextId = (uint)(DescriptionBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Sound room (FE6) data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SoundRoomFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
