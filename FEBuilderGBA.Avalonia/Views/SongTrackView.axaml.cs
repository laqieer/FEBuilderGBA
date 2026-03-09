using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTrackView : Window, IEditorView
    {
        readonly SongTrackViewModel _vm = new();

        public string ViewTitle => "Song Track Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public SongTrackView()
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
                Log.Error("SongTrackView.LoadList failed: {0}", ex.Message);
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
                Log.Error("SongTrackView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            TrackCountBox.Value = _vm.TrackCount;
            NumBlksBox.Value = _vm.NumBlks;
            PriorityBox.Value = _vm.Priority;
            ReverbBox.Value = _vm.Reverb;
            InstrumentAddrBox.Value = _vm.InstrumentAddr;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.TrackCount = (uint)(TrackCountBox.Value ?? 0);
            _vm.NumBlks = (uint)(NumBlksBox.Value ?? 0);
            _vm.Priority = (uint)(PriorityBox.Value ?? 0);
            _vm.Reverb = (uint)(ReverbBox.Value ?? 0);
            _vm.InstrumentAddr = (uint)(InstrumentAddrBox.Value ?? 0);
            _vm.Write();
            CoreState.Services.ShowInfo("Song track data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
