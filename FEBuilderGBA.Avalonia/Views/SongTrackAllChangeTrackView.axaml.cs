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

        public string ViewTitle => "Bulk Track Change";
        public bool IsLoaded => _vm.IsLoaded;

        public SongTrackAllChangeTrackView()
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
                Log.Error("SongTrackAllChangeTrackView.LoadList failed: {0}", ex.Message);
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
                Log.Error("SongTrackAllChangeTrackView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        public void NavigateTo(uint address)
        {
            // When a non-zero context address is passed by the caller (e.g.
            // `SongTrackView.AllTracks_Click` passing `_vm.InstrumentAddr`
            // — the instrument-set pointer / P4, matching WF's
            // `f.Init((uint)P4.Value, this.Tracks)`), honor it directly so
            // the target editor reflects the requested scope instead of
            // staying pinned at the placeholder `0` row. Falls back to
            // list-based selection when address is 0 (the standalone "open"
            // path).
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
