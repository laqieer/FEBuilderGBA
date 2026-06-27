using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTrackAllChangeTrackView : TranslatedWindow, IEditorView
    {
        readonly SongTrackAllChangeTrackViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Bulk Track Change";
        public bool IsLoaded => _vm.IsLoaded;

        public SongTrackAllChangeTrackView()
        {
            InitializeComponent();
            // The voice-remap ListBox binds to _vm.Rows, so the View needs a
            // DataContext pointing at the VM.
            DataContext = _vm;
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
                Log.ErrorF("SongTrackAllChangeTrackView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            // The left-hand list rows are voice indices (0..N-1), NOT song
            // addresses — selecting a row just scrolls/highlights it. The song
            // itself is loaded via NavigateTo (the Song Track jump). Ignore the
            // selection callback to avoid re-deriving on a bogus address.
            _ = addr;
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.SongAddr);
            // _vm.Rows is an ObservableCollection bound to VoiceList, so the
            // grid refreshes automatically; rebuild the left list labels too.
            LoadList();
        }

        void Apply_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            // No-op apply: don't open/commit an undo scope or claim "applied"
            // when nothing was edited — no voice remap AND no Vol/Pan/Tempo/velocity
            // delta (Copilot bot review #1088; Vol/Pan/Tempo-only fix #1002 Finding 5).
            if (!_vm.HasPendingChanges)
            {
                CoreState.Services.ShowInfo(R._("No track changes to apply."));
                return;
            }

            _undoService.Begin("Bulk Track Change");
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
                // Re-derive from ROM so the rows reflect the freshly-written
                // voices (To resets to the new From for each remapped voice).
                _vm.LoadEntry(_vm.SongAddr);
                UpdateUI();
                CoreState.Services.ShowInfo(R._("Track changes applied."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SongTrackAllChangeTrackView.Apply_Click failed: {0}", ex.Message);
                CoreState.Services.ShowError(R._("Apply failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address)
        {
            // The caller (SongTrackView.AllTracks_Click) passes the SONG HEADER
            // address so the editor re-derives the full track list and the
            // distinct 0xBD voices used across them straight from the header.
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
