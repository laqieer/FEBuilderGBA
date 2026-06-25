using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTableView : TranslatedWindow, IPickableEditor, IDataVerifiableView
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
                // LoadSong derives SongIndex from the table base, so the
                // write-protection guard (IsSongIdZero) recognises Song ID 0
                // regardless of how the entry was selected.
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
            // WF parity: SongID 0 is write-protected (UseWriteProtectionID00).
            // Mirrors SongTrackView — the reserved silence entry must not be
            // overwritten (breaks "no music" semantics).
            if (_vm.IsSongIdZero)
            {
                CoreState.Services.ShowError("Song ID 0 is write-protected (silence song).");
                return;
            }

            _undoService.Begin("Edit Song Table");
            try
            {
                _vm.SongHeaderPointer = ParseHexText(HeaderBox.Text);
                _vm.PlayerType = (uint)(PlayerTypeBox.Value ?? 0);
                // Only commit + report success when a write actually occurred.
                // WriteSong() returns false (no ROM mutation) for a protected /
                // out-of-range entry — roll back and surface an error instead of
                // falsely reporting success.
                if (_vm.WriteSong())
                {
                    _undoService.Commit();
                    _vm.MarkClean();
                    CoreState.Services.ShowInfo("Song table data written.");
                }
                else
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError("Song table data was not written.");
                }
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
