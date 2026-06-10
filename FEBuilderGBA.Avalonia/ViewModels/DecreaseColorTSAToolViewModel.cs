using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Color Reduction Tool (WF <c>DecreaseColorTSAToolForm</c>).
    ///
    /// This is a file→file PNG color-reduction utility — NO ROM mutation, NO
    /// UndoService, NO address list. It drives the merged Core engine
    /// <see cref="DecreaseColorConvertCore"/>:
    ///   - <c>GetMethodPreset(method, romVersion)</c> populates the parameter
    ///     fields from a "Method" combo preset (mirrors WF
    ///     <c>Method_SelectedIndexChanged</c>).
    ///   - <c>ReduceColorFile(...)</c> runs the actual reduce.
    ///
    /// Combo-index → Core-bool mapping mirrors WF:
    ///   SizeMethodIndex 0 = resize (crop/pad), 1 = scale  → <see cref="Scalable"/>.
    ///   ReserveIndex    0 = no,                1 = yes     → <see cref="Reserve1st"/>.
    /// </summary>
    public class DecreaseColorTSAToolViewModel : ViewModelBase
    {
        string _inputPath = "";
        string _outputPath = "";
        int _method = 1;
        int _width = 30 * 8;
        int _height = 20 * 8;
        int _yohaku = 2 * 8;
        int _paletteNo = 8;
        int _sizeMethodIndex = 1; // WF ctor: ConvertSizeMethod.SelectedIndex = 1 (scale)
        int _reserveIndex = 1;    // WF method-1 preset: ConvertReserveColor = 1 (reserve)
        bool _ignoreTSA;
        string _statusMessage = "";

        public string InputPath { get => _inputPath; set => SetField(ref _inputPath, value); }
        public string OutputPath { get => _outputPath; set => SetField(ref _outputPath, value); }

        /// <summary>
        /// Method-combo index (mirrors WF <c>DecreaseColorTSAToolForm.Method</c>).
        /// 0 = manual (no preset), 1..0xA = presets. See <see cref="ApplyPreset"/>.
        /// </summary>
        public int Method { get => _method; set => SetField(ref _method, value); }

        public int Width { get => _width; set => SetField(ref _width, value); }
        public int Height { get => _height; set => SetField(ref _height, value); }
        public int Yohaku { get => _yohaku; set => SetField(ref _yohaku, value); }

        /// <summary>Number of 16-color palette banks (1..16).</summary>
        public int PaletteNo
        {
            get => _paletteNo;
            set
            {
                if (SetField(ref _paletteNo, value))
                    OnPropertyChanged(nameof(ShowIgnoreTSA));
            }
        }

        /// <summary>Size-method combo index: 0 = resize (crop/pad), 1 = scale.</summary>
        public int SizeMethodIndex { get => _sizeMethodIndex; set => SetField(ref _sizeMethodIndex, value); }

        /// <summary>Reserve-1st-color combo index: 0 = no, 1 = yes.</summary>
        public int ReserveIndex { get => _reserveIndex; set => SetField(ref _reserveIndex, value); }

        public bool IgnoreTSA { get => _ignoreTSA; set => SetField(ref _ignoreTSA, value); }

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// IgnoreTSA checkbox visibility — mirrors WF
        /// <c>ConvertPaletteNo_ValueChanged</c>: visible only when PaletteNo ≥ 4.
        /// PropertyChanged is raised from the <see cref="PaletteNo"/> setter.
        /// </summary>
        public bool ShowIgnoreTSA => PaletteNo >= 4;

        /// <summary>Core <c>isScalable</c> flag derived from the size-method combo.</summary>
        public bool Scalable => SizeMethodIndex == 1;

        /// <summary>Core <c>reserve1st</c> flag derived from the reserve combo.</summary>
        public bool Reserve1st => ReserveIndex == 1;

        /// <summary>
        /// Apply the "Method" combo preset, mirroring WF
        /// <c>Method_SelectedIndexChanged</c>. Method 0 (WF combo item
        /// <c>00=自分で決める</c> / "decide yourself") has NO branch in WF and
        /// therefore PRESERVES the current parameter values — a deliberate no-op
        /// so the user can hand-tune everything. Methods 1..0xA populate the
        /// fields from <see cref="DecreaseColorConvertCore.GetMethodPreset"/>.
        /// </summary>
        public void ApplyPreset(int method)
        {
            // WF parity: index 0 = manual, no overwrite.
            if (method <= 0)
            {
                return;
            }

            int romVersion = CoreState.ROM?.RomInfo?.version ?? 8;
            var p = DecreaseColorConvertCore.GetMethodPreset(method, romVersion);

            Width = p.Width;
            Height = p.Height;
            Yohaku = p.Yohaku;
            PaletteNo = p.PaletteNo;
            SizeMethodIndex = p.Scalable ? 1 : 0;
            ReserveIndex = p.Reserve1st ? 1 : 0;
            IgnoreTSA = p.IgnoreTSA;
        }

        /// <summary>
        /// Run the file→file color reduction via the Core engine and update
        /// <see cref="StatusMessage"/>. Returns the Core error code
        /// (0 = ok, -2 = bad input/output path, -1 = image/IO error).
        /// </summary>
        public int Reduce()
        {
            int code = DecreaseColorConvertCore.ReduceColorFile(
                InputPath, OutputPath, Width, Height, Yohaku, PaletteNo,
                Scalable, Reserve1st, IgnoreTSA);

            StatusMessage = code switch
            {
                0 => R._("Color reduction complete:") + " " + OutputPath,
                -2 => R._("Please select a valid input and output file."),
                _ => R._("Color reduction failed. See the log for details."),
            };
            return code;
        }
    }
}
