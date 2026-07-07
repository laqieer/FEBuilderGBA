using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongExchangeView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly SongExchangeViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Song Exchange Tool";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Song Exchange Tool", 1322, 601, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public SongExchangeView()
        {
            InitializeComponent();
            // The two song ListBoxes + Convert/Open buttons bind to the VM.
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
                LoadCurrentSongs();
            }
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
                Log.ErrorF("SongExchangeView.LoadList failed: {0}", ex.Message);
            }
        }

        void LoadCurrentSongs()
        {
            try
            {
                _vm.LoadCurrentSongs();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongExchangeView.LoadCurrentSongs failed: {0}", ex.Message);
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
                Log.ErrorF("SongExchangeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        async void OpenOtherRom_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var gbaType = new FilePickerFileType(R._("GBA ROMs")) { Patterns = new[] { "*.gba", "*.bin" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storageProvider == null) return;
                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Select a ROM to import a song from"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { gbaType, allType },
                });
                if (files.Count == 0) return;

                // #1639: read the donor ROM bytes via the stream API so Android
                // content:// sources (no local path) are read, not treated as
                // cancelled. The filename is only used as a display label, so the
                // local path (when present) or the SAF display name both work.
                string label = files[0].TryGetLocalPath() ?? files[0].Name ?? "(rom)";
                byte[] data;
                await using (var stream = await files[0].OpenReadAsync())
                using (var ms = new System.IO.MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    data = ms.ToArray();
                }
                _vm.LoadOtherRom(data, label);
                OtherRomLabel.Text = R._("Other ROM: {0} ({1} songs)", label, _vm.OtherSongList.Count);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongExchangeView.OpenOtherRom_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Failed to open ROM: {0}", ex.Message));
            }
        }

        void Convert_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.HasOtherRom)
            {
                CoreState.Services.ShowError(R._("The other ROM has not been loaded."));
                return;
            }

            int srcIndex = OtherList.SelectedIndex;
            int destIndex = MyList.SelectedIndex;
            if (srcIndex < 0)
            {
                CoreState.Services.ShowError(R._("No song selected to export."));
                return;
            }
            if (destIndex < 0)
            {
                CoreState.Services.ShowError(R._("No song selected to import."));
                return;
            }
            if (destIndex == 0)
            {
                CoreState.Services.ShowError(R._("Cannot write to SongID 0x0."));
                return;
            }

            if (!CoreState.Services.ShowYesNo(
                R._("Transplant this song (#{0}) from the other ROM into the current ROM (#{1})?", srcIndex, destIndex)))
            {
                return;
            }

            _undoService.Begin("Song Exchange");
            try
            {
                string error = _vm.Convert(srcIndex, destIndex, _undoService.GetActiveUndoData());
                if (!string.IsNullOrEmpty(error))
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(error);
                    return;
                }

                _undoService.Commit();
                // Re-derive the current song list so the destination row reflects
                // the freshly-written header / voices / track pointer.
                int keep = destIndex;
                _vm.LoadCurrentSongs();
                if (keep < MyList.ItemCount) MyList.SelectedIndex = keep;
                // Surface a partial-corrupt source (WF showed a force/warn dialog):
                // warn instead of claiming a clean success.
                if (_vm.LastConvertHadStructureWarning)
                {
                    CoreState.Services.ShowInfo(R._("Transplant completed, but some instrument data was corrupt and only the recognized parts were imported."));
                }
                else
                {
                    CoreState.Services.ShowInfo(R._("Song import complete."));
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongExchangeView.Convert_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Convert failed: {0}", ex.Message));
            }
        }

        // #1383: open the FE-Repo-Music browser and import the selected
        // destination song through the Song Track Editor's ONE shared dispatcher.
        // Song Exchange is a ROM->ROM transplant with no per-song instrument
        // picker of its own, so instead of duplicating the .s/.mid/.wav importer
        // we navigate to the Song Track Editor for the selected destination song
        // and hand it the chosen path (SongTrackView.ImportMusicFromExternal).
        async void FERepoMusic_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int destIndex = MyList.SelectedIndex;
                if (destIndex < 0)
                {
                    CoreState.Services.ShowError(R._("No song selected to import."));
                    return;
                }
                if (destIndex == 0)
                {
                    CoreState.Services.ShowError(R._("Cannot write to SongID 0x0."));
                    return;
                }

                if (destIndex >= _vm.MySongList.Count)
                {
                    CoreState.Services.ShowError(R._("No song selected to import."));
                    return;
                }
                // The Song Track list selects by song-HEADER address, not index,
                // so resolve the destination song's header offset (#1399 review).
                uint destHeaderAddr = _vm.MySongList[destIndex].Header;

                string? path = await FERepoPickHelper.PickMusic(TopLevel.GetTopLevel(this));
                if (string.IsNullOrEmpty(path)) return;

                // Navigate to the Song Track Editor at the selected destination
                // song, then run its single import dispatcher with the chosen path.
                var view = WindowManager.Instance.Navigate<SongTrackView>(destHeaderAddr);
                await view.ImportMusicFromExternal(destHeaderAddr, path);
            }
            catch (Exception ex)
            {
                Log.Error("SongExchangeView.FERepoMusic_Click failed:", ex.ToString());
                CoreState.Services.ShowError(R._("FE-Repo-Music import failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address)
        {
            // The caller passes a SONG INDEX (e.g. `SongTrackView` passes
            // `_vm.SelectedSongIndex`). Song ID 0 is a VALID song index
            // (the silence song), so we cannot use 0 as a "no context"
            // sentinel — we always honor the requested context by calling
            // LoadEntry. The standalone open path also routes through here
            // with address = 0, which loads the placeholder "0" entry from
            // the stub list — matching the previous behavior.
            _vm.LoadEntry(address);
            UpdateUI();
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
