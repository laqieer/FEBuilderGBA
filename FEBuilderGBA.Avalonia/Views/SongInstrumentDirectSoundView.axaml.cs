using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongInstrumentDirectSoundView : Window, IEditorView
    {
        readonly SongInstrumentDirectSoundViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Direct Sound Instruments";
        public bool IsLoaded => _vm.IsLoaded;

        public SongInstrumentDirectSoundView()
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
                Log.Error("SongInstrumentDirectSoundView.LoadList failed: {0}", ex.Message);
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
                Log.Error("SongInstrumentDirectSoundView.OnSelected failed: {0}", ex.Message);
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
            HeaderBox.Value = _vm.Header;
            FrequencyBox.Value = _vm.FrequencyHz1024;
            LoopStartBox.Value = _vm.LoopStartByte;
            LengthBox.Value = _vm.LengthByte;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin("Edit Direct Sound Instrument");
            try
            {
                _vm.Header = (uint)(HeaderBox.Value ?? 0);
                _vm.FrequencyHz1024 = (uint)(FrequencyBox.Value ?? 0);
                _vm.LoopStartByte = (uint)(LoopStartBox.Value ?? 0);
                // LengthByte is read-only
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Direct sound data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SongInstrumentDirectSoundView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
