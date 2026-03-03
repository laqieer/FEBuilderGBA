using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTableView : Window, IEditorView
    {
        readonly SongTableViewModel _vm = new();

        public string ViewTitle => "Song Table";
        public bool IsLoaded => _vm.IsLoaded;

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
            HeaderLabel.Text = $"0x{_vm.HeaderPointer:X08}";
            TrackCountLabel.Text = _vm.TrackCount.ToString();
            PriorityLabel.Text = _vm.Priority.ToString();
            ReverbLabel.Text = _vm.Reverb.ToString();
        }

        public void SelectFirstItem()
        {
            SongList.SelectFirst();
        }
    }
}
