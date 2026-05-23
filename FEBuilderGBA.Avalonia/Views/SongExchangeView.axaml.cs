using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongExchangeView : TranslatedWindow, IEditorView
    {
        readonly SongExchangeViewModel _vm = new();

        public string ViewTitle => "Song Exchange Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public SongExchangeView()
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
                Log.Error("SongExchangeView.LoadList failed: {0}", ex.Message);
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
                Log.Error("SongExchangeView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
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
