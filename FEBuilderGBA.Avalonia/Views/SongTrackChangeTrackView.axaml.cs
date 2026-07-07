using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTrackChangeTrackView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly SongTrackChangeTrackViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Track Change";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Track Change", 754, 640, SizeToContent: true);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public SongTrackChangeTrackView()
        {
            InitializeComponent();
            // The voice-remap ListBox + Vol/Pan/Velocity inputs bind to _vm, so the
            // View needs a DataContext pointing at the VM.
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
                Log.ErrorF("SongTrackChangeTrackView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            // The left-hand list rows are voice indices (0..N-1), NOT track
            // addresses — selecting a row just scrolls/highlights it. The track
            // itself is loaded via NavigateTo (the Song Track jump). Ignore the
            // selection callback to avoid re-deriving on a bogus address.
            _ = addr;
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.TrackDataOffset);
            // _vm.Rows is an ObservableCollection bound to VoiceList, so the grid
            // refreshes automatically; rebuild the left list labels too.
            LoadList();
        }

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            // No-op apply: don't open/commit an undo scope or claim "applied" when
            // nothing was edited (no voice remap, no Vol/Pan/velocity delta).
            if (!_vm.HasPendingChanges)
            {
                CoreState.Services.ShowInfo(R._("No track changes to apply."));
                return;
            }

            _undoService.Begin("Track Change");
            try
            {
                string error = _vm.ApplyChanges();
                if (!string.IsNullOrEmpty(error))
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(error);
                    return;
                }

                _undoService.Commit();
                // Re-derive from ROM so the rows reflect the freshly-written voices
                // (To resets to the new From for each remapped voice; nudges reset).
                _vm.LoadEntry(_vm.TrackDataOffset);
                UpdateUI();
                CoreState.Services.ShowInfo(R._("Track changes applied."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongTrackChangeTrackView.Apply_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Apply failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address)
        {
            // The caller (SongTrackView.TrackLabel_Click) passes the clicked track's
            // TrackInfo.DataOffset — the resolved track-data ROM offset, matching
            // WF's SongUtil.Track.basepointer — so the editor parses that exact
            // track. Falls back to list-based selection when address is 0 (the
            // standalone "open" path).
            if (address != 0)
            {
                _vm.LoadEntry(address);
                UpdateUI();
            }
            else
            {
                EntryList.SelectAddress(address);
            }
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
