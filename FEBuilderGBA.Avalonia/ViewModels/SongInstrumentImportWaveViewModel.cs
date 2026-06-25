using System;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// View-model for the DirectSound wav-import conversion dialog (#1448) — the
    /// Avalonia port of WinForms <c>SongInstrumentImportWaveForm</c>. It owns the
    /// sox-resample / DPCM-compress / SNR-preview options and orchestrates the
    /// <see cref="SongWaveConvertCore"/> pipeline (no UI logic in Core).
    ///
    /// <para>It is a modal RESULT dialog: seeded with the chosen <c>.wav</c> bytes,
    /// it returns the ready GBA DirectSound sample bytes (raw 8-bit PCM or DPCM)
    /// for the caller to append + repoint. Cancel returns nothing (strict no-op —
    /// #1448 review pt 2).</para>
    ///
    /// <para>DPCM gating (#1448 review pt 1): the DPCM-compress option is only
    /// enabled when the m4a HQ-mixer patch is installed (<see cref="HqMixerAvailable"/>),
    /// because DPCM samples only play correctly with that patch — exactly the WF
    /// hide/block behavior.</para>
    /// </summary>
    public class SongInstrumentImportWaveViewModel : ViewModelBase
    {
        // The chosen source .wav bytes (raw file content). Set via Seed().
        byte[] _sourceWav;

        public string SourceFileName { get => _sourceFileName; set => SetField(ref _sourceFileName, value); }
        string _sourceFileName = "";

        // ---- conversion option lists (mirror the WF combobox values) ----------
        // WF default indices: HZ=4, Strip=1, Channel=1, Volume=9, DPCM=0, Lookahead=3.
        public static readonly uint[] HzValues = { 0, 8000, 10512, 11025, 13379, 16000, 18157, 22050, 32000, 44100 };
        public static readonly uint[] StripValues = { 0, 1, 2, 3, 6, 11 }; // 0=off; value-1 = silence %
        public static readonly uint[] ChannelValues = { 0, 1, 2 };          // 0=unchanged, 1=mono, 2=stereo
        public static readonly uint[] VolumeValues = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 100, 120, 150, 200 };
        public static readonly uint[] LookaheadValues = { 1, 2, 3, 4, 5 };

        // ItemsSource bindables (boxed so ComboBox SelectedItem matches the uint props)
        public System.Collections.Generic.IReadOnlyList<uint> HzValuesList => HzValues;
        public System.Collections.Generic.IReadOnlyList<uint> StripValuesList => StripValues;
        public System.Collections.Generic.IReadOnlyList<uint> ChannelValuesList => ChannelValues;
        public System.Collections.Generic.IReadOnlyList<uint> VolumeValuesList => VolumeValues;
        public System.Collections.Generic.IReadOnlyList<uint> LookaheadValuesList => LookaheadValues;

        // selected option values (defaults match WF)
        public uint Hz { get => _hz; set => SetField(ref _hz, value); }
        uint _hz = 13379; // WF index 4

        public uint Strip { get => _strip; set => SetField(ref _strip, value); }
        uint _strip = 1;  // WF index 1

        public uint Channel { get => _channel; set => SetField(ref _channel, value); }
        uint _channel = 1; // WF index 1 (mono)

        public uint Volume { get => _volume; set => SetField(ref _volume, value); }
        uint _volume = 100; // WF index 9

        public bool UseDpcm
        {
            get => _useDpcm;
            set { if (SetField(ref _useDpcm, value)) OnPropertyChanged(nameof(DpcmEffective)); }
        }
        bool _useDpcm; // WF index 0 == off

        public uint Lookahead { get => _lookahead; set => SetField(ref _lookahead, value); }
        uint _lookahead = 3; // WF value 3

        // ---- gating ----------------------------------------------------------

        /// <summary>True when the m4a HQ-mixer patch is installed; DPCM only offered then.</summary>
        public bool HqMixerAvailable
        {
            get => _hqMixer;
            set
            {
                if (SetField(ref _hqMixer, value))
                {
                    OnPropertyChanged(nameof(CanUseDpcm));
                    OnPropertyChanged(nameof(DpcmGateMessage));
                    OnPropertyChanged(nameof(DpcmEffective));
                }
            }
        }
        bool _hqMixer;

        /// <summary>DPCM checkbox enabled only when the HQ mixer patch exists.</summary>
        public bool CanUseDpcm => HqMixerAvailable;

        /// <summary>Whether DPCM will actually be applied (requested AND allowed).</summary>
        public bool DpcmEffective => UseDpcm && HqMixerAvailable;

        public string DpcmGateMessage => HqMixerAvailable
            ? R._("DPCM compression is available (m4a HQ mixer patch detected).")
            : R._("DPCM compression requires the m4a HQ mixer patch (not installed); raw 8-bit PCM will be used.");

        // ---- preview ---------------------------------------------------------

        public string PreviewText { get => _previewText; set => SetField(ref _previewText, value); }
        string _previewText = "";

        public bool IsLoaded => _sourceWav != null;

        // ---------------------------------------------------------------------

        /// <summary>Seed the dialog with the chosen source .wav bytes + filename and
        /// the HQ-mixer availability of the active ROM.</summary>
        public void Seed(byte[] sourceWav, string fileName)
        {
            _sourceWav = sourceWav;
            SourceFileName = fileName ?? "";
            HqMixerAvailable = SongWaveConvertCore.HasHqMixer(CoreState.ROM);
            PreviewText = R._("Press Preview to see the conversion result.");
            OnPropertyChanged(nameof(IsLoaded));
        }

        /// <summary>Run a preview conversion and format the WF-style result line
        /// (size delta + SNR for DPCM). Returns the formatted text (also stored in
        /// <see cref="PreviewText"/>).</summary>
        public string RunPreview()
        {
            if (_sourceWav == null)
            {
                PreviewText = R._("No wave file is loaded.");
                return PreviewText;
            }

            SongWaveConvertCore.PreviewResult p = SongWaveConvertCore.Preview(
                _sourceWav, Channel, Hz, Strip, Volume, UseDpcm, Lookahead, HqMixerAvailable);

            if (p.Error != null)
            {
                PreviewText = p.Error;
                return PreviewText;
            }

            double pct = p.OriginalSize > 0 ? Math.Round(p.ResultSize * 100.0 / p.OriginalSize, 2) : 0;
            if (p.IsDpcm)
            {
                PreviewText = R._(
                    "File size {0} -> {1} ({2}%)\r\nDPCM quality SNR: {3}dB (higher is better; below 20dB you may not want compression).",
                    p.OriginalSize, p.ResultSize, pct, Math.Round(p.Snr, 3));
            }
            else
            {
                PreviewText = R._("File size {0} -> {1} ({2}%) (raw 8-bit PCM)", p.OriginalSize, p.ResultSize, pct);
            }
            return PreviewText;
        }

        /// <summary>Run the final conversion and return the ready GBA DirectSound
        /// sample bytes (raw or DPCM), or <c>null</c> with <paramref name="error"/>
        /// set on failure.</summary>
        public byte[] Convert(out string error)
        {
            error = null;
            if (_sourceWav == null)
            {
                error = R._("No wave file is loaded.");
                return null;
            }
            SongWaveConvertCore.ConvertResult c = SongWaveConvertCore.Convert(
                _sourceWav, Channel, Hz, Strip, Volume, UseDpcm, Lookahead, HqMixerAvailable);
            if (c.SampleBytes == null)
            {
                error = c.Error ?? R._("Wave conversion failed.");
                return null;
            }
            return c.SampleBytes;
        }
    }
}
