using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTableView : Window, IPickableEditor, IDataVerifiableView
    {
        readonly SongTableViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Song Table";
        public bool IsLoaded => _vm.CanWrite;

        public event Action<PickResult>? SelectionConfirmed;

        public SongTableView()
        {
            InitializeComponent();
            SongList.SelectedAddressChanged += OnSongSelected;
            SongList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSongList();
                SongList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SongTableView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSongSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSong(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SongTableView.OnSongSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        public void NavigateTo(uint address)
        {
            SongList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            HeaderBox.Text = $"0x{_vm.SongHeaderPointer:X08}";
            PlayerTypeBox.Value = _vm.PlayerType;
            TrackCountLabel.Text = _vm.TrackCount.ToString();
            HeaderPriorityLabel.Text = _vm.HeaderPriority.ToString();
            HeaderReverbLabel.Text = _vm.HeaderReverb.ToString();
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Song Table");
            try
            {
                _vm.SongHeaderPointer = ParseHexText(HeaderBox.Text);
                _vm.PlayerType = (uint)(PlayerTypeBox.Value ?? 0);
                _vm.WriteSong();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Song table data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SongTableView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void EnablePickMode() => SongList.EnablePickMode();

        public void SelectFirstItem()
        {
            SongList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
