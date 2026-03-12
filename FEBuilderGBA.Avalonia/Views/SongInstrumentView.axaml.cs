using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SongInstrumentView : Window, IEditorView
    {
        readonly SongInstrumentViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Instrument Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public SongInstrumentView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        /// <summary>
        /// Reload the instrument list from a specific base address (e.g., from SongTrack).
        /// </summary>
        public void JumpToAddr(uint baseAddr)
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadInstrumentList(baseAddr);
                EntryList.SetItems(items);
                if (items.Count > 0)
                    EntryList.SelectFirst();
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentView.JumpToAddr failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SongInstrumentView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            HeaderByteBox.Value = _vm.HeaderByte;
            TypeNameLabel.Text = _vm.TypeName;

            // Show/hide type-specific panels
            DirectSoundPanel.IsVisible = _vm.IsDirectSound || _vm.IsWaveMemory;
            SquareWavePanel.IsVisible = _vm.IsSquareWave;
            NoisePanel.IsVisible = _vm.IsNoise;
            MultiSamplePanel.IsVisible = _vm.IsMultiSample;
            DrumPanel.IsVisible = _vm.IsDrum;
            UnknownPanel.IsVisible = _vm.Category == InstrumentCategory.Unknown;

            // Populate type-specific fields
            switch (_vm.Category)
            {
                case InstrumentCategory.DirectSound:
                case InstrumentCategory.WaveMemory:
                    WavePtrBox.Value = _vm.WavePtr;
                    DS_AttackBox.Value = _vm.Attack;
                    DS_DecayBox.Value = _vm.Decay;
                    DS_SustainBox.Value = _vm.Sustain;
                    DS_ReleaseBox.Value = _vm.Release;
                    break;

                case InstrumentCategory.SquareWave:
                    SweepBox.Value = _vm.Sweep;
                    DutyLenBox.Value = _vm.DutyLen;
                    EnvStepBox.Value = _vm.EnvStep;
                    SQ_AttackBox.Value = _vm.Attack;
                    SQ_DecayBox.Value = _vm.Decay;
                    SQ_SustainBox.Value = _vm.Sustain;
                    SQ_ReleaseBox.Value = _vm.Release;
                    break;

                case InstrumentCategory.Noise:
                    PeriodBox.Value = _vm.Period;
                    NS_AttackBox.Value = _vm.Attack;
                    NS_DecayBox.Value = _vm.Decay;
                    NS_SustainBox.Value = _vm.Sustain;
                    NS_ReleaseBox.Value = _vm.Release;
                    break;

                case InstrumentCategory.MultiSample:
                    KeyMapPtrBox.Value = _vm.KeyMapPtr;
                    MS_SubInstrPtrBox.Value = _vm.SubInstrPtr;
                    break;

                case InstrumentCategory.Drum:
                    DR_SubInstrPtrBox.Value = _vm.SubInstrPtr;
                    break;
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin("Edit Instrument");
            try
            {
                _vm.HeaderByte = (byte)(HeaderByteBox.Value ?? 0);

                // Re-classify in case the user changed the header byte
                var newCat = SongInstrumentViewModel.ClassifyType(_vm.HeaderByte);
                _vm.Category = newCat;
                _vm.TypeName = SongInstrumentViewModel.GetInstrumentTypeName(_vm.HeaderByte);

                switch (newCat)
                {
                    case InstrumentCategory.DirectSound:
                    case InstrumentCategory.WaveMemory:
                        _vm.WavePtr = (uint)(WavePtrBox.Value ?? 0);
                        _vm.Attack = (byte)(DS_AttackBox.Value ?? 0);
                        _vm.Decay = (byte)(DS_DecayBox.Value ?? 0);
                        _vm.Sustain = (byte)(DS_SustainBox.Value ?? 0);
                        _vm.Release = (byte)(DS_ReleaseBox.Value ?? 0);
                        break;

                    case InstrumentCategory.SquareWave:
                        _vm.Sweep = (byte)(SweepBox.Value ?? 0);
                        _vm.DutyLen = (byte)(DutyLenBox.Value ?? 0);
                        _vm.EnvStep = (byte)(EnvStepBox.Value ?? 0);
                        _vm.Attack = (byte)(SQ_AttackBox.Value ?? 0);
                        _vm.Decay = (byte)(SQ_DecayBox.Value ?? 0);
                        _vm.Sustain = (byte)(SQ_SustainBox.Value ?? 0);
                        _vm.Release = (byte)(SQ_ReleaseBox.Value ?? 0);
                        break;

                    case InstrumentCategory.Noise:
                        _vm.Period = (byte)(PeriodBox.Value ?? 0);
                        _vm.Attack = (byte)(NS_AttackBox.Value ?? 0);
                        _vm.Decay = (byte)(NS_DecayBox.Value ?? 0);
                        _vm.Sustain = (byte)(NS_SustainBox.Value ?? 0);
                        _vm.Release = (byte)(NS_ReleaseBox.Value ?? 0);
                        break;

                    case InstrumentCategory.MultiSample:
                        _vm.KeyMapPtr = (uint)(KeyMapPtrBox.Value ?? 0);
                        _vm.SubInstrPtr = (uint)(MS_SubInstrPtrBox.Value ?? 0);
                        break;

                    case InstrumentCategory.Drum:
                        _vm.SubInstrPtr = (uint)(DR_SubInstrPtrBox.Value ?? 0);
                        break;
                }

                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Instrument data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SongInstrumentView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
