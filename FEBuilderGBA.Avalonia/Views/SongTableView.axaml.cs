using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTableView : Window, IEditorView, IDataVerifiableView
    {
        readonly SongTableViewModel _vm = new();

        public string ViewTitle => "Song Table";
        public bool IsLoaded => _vm.CanWrite;

        public SongTableView()
        {
            InitializeComponent();
            SongList.SelectedAddressChanged += OnSongSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSongList();
                SongList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SongTableView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSongSelected(uint addr)
        {
            try
            {
                _vm.LoadSong(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SongTableView.OnSongSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            SongList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            HeaderBox.Value = _vm.HeaderPointer;
            D4Box.Value = _vm.D4;
            TrackCountLabel.Text = _vm.TrackCount.ToString();
            PriorityLabel.Text = _vm.Priority.ToString();
            ReverbLabel.Text = _vm.Reverb.ToString();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.HeaderPointer = (uint)(HeaderBox.Value ?? 0);
            _vm.D4 = (uint)(D4Box.Value ?? 0);
            _vm.WriteSong();
            CoreState.Services.ShowInfo("Song table data written.");
        }

        public void SelectFirstItem()
        {
            SongList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
