using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongTrackImportSelectInstrumentView : TranslatedWindow, IPickableEditor
    {
        readonly SongTrackImportSelectInstrumentViewModel _vm = new();

        public string ViewTitle => "Instrument Selection";
        public bool IsLoaded => _vm.IsLoaded;

        // #1001 PR2 / #1002: the browser is now a pick-and-return editor — the
        // SongTrack .s import opens it via WindowManager.PickFromEditor and the
        // confirmed selection becomes the instrument-set address fed into
        // SongTrackSImportCore.ImportS.
        public event Action<PickResult>? SelectionConfirmed;

        public SongTrackImportSelectInstrumentView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            EntryList.SelectionConfirmed += result => SelectionConfirmed?.Invoke(result);
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                // Mirror WinForms PickupInstrument(): default to the first
                // discovered instrument set (index 1) rather than the "Current"
                // seed, falling back to the first row. (#787)
                if (!EntryList.SelectByIndex(_vm.DefaultSelectionIndex))
                {
                    EntryList.SelectFirst();
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SongTrackImportSelectInstrumentView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("SongTrackImportSelectInstrumentView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            InstrumentInfoLabel.Text = _vm.InstrumentInfoText;
        }

        // #1001 PR2 / Copilot plan finding 2: seed the picker with the song's
        // CURRENT voicegroup (WF f.Init(P4)) so the "Current" list row is the
        // selected song's real instrument set, not the 0 default. The list is
        // re-built from the seed (InstrumentSetCore keys its cache on CurrentAddr),
        // then we land the selection on the seeded address.
        public void NavigateTo(uint address)
        {
            if (address != 0 && _vm.CurrentAddr != address)
            {
                _vm.CurrentAddr = address;
                LoadList();
            }
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem() => EntryList.SelectFirst();

        public void EnablePickMode() => EntryList.EnablePickMode();
    }
}
