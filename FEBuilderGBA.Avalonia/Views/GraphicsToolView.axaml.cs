using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class GraphicsToolView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly GraphicsToolViewViewModel _vm = new();

        public string ViewTitle => "Graphics Tool";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public GraphicsToolView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        void Draw_Click(object? sender, RoutedEventArgs e)
        {
            var image = _vm.DrawTiles();
            if (image != null)
            {
                ImageDisplay.Zoom = _vm.Zoom;
                ImageDisplay.SetImage(image);
            }
            else
            {
                ImageDisplay.SetImage(null);
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }

        /// <summary>
        /// Pre-populate the Graphics Tool with image + palette addresses
        /// and re-draw. Mirrors the WinForms
        /// <c>GraphicsToolForm.Jump(int width, int height, uint image,
        /// int imageType, uint tsa, int tsaType, uint palette,
        /// int paletteType, int paletteCount, uint image2)</c> signature
        /// — the WF version takes pixels and converts to tile counts via
        /// `PicWidth.Value = width / 8`; the Avalonia VM uses tile counts
        /// directly, so we perform the same `/ 8` conversion here.
        ///
        /// <para>
        /// Note: the Avalonia <c>GraphicsToolViewViewModel</c> currently
        /// renders the tile + palette pair only (no separate TSA field).
        /// The <paramref name="tsa"/> / <paramref name="tsaType"/> /
        /// <paramref name="paletteType"/> / <paramref name="image2"/>
        /// parameters are accepted for WinForms signature parity but are
        /// not consumed by the Avalonia preview path. Callers that need
        /// TSA-aware decoding should use the editor view that owns the
        /// TSA pointer (e.g. <c>ImageBattleBGView</c>) which renders the
        /// preview separately.
        /// </para>
        ///
        /// Parameter semantics mirror WF (see <c>GraphicsToolForm.Draw()</c>
        /// at lines 309 + 322 — the `ImageOption.SelectedIndex` value is
        /// LZ77-compressed when in {0, 2, 3, 4}, raw when 1):
        /// <list type="bullet">
        ///   <item><c>imageType</c>: 0 / 2 / 3 / 4 = LZ77-compressed
        ///     (single, 2nd-joined, or other compressed modes);
        ///     1 = raw uncompressed. Battle BG callsites pass 0.</item>
        ///   <item><c>tsaType</c> / <c>paletteType</c>: 1 = LZ77-compressed
        ///     in Battle BG callsites; 0 = raw. (Recorded on the WF VM;
        ///     not yet consumed by the Avalonia decoder — see note above.)</item>
        ///   <item><c>paletteCount</c>: number of 16-color rows the WF form
        ///     would show.</item>
        /// </list>
        /// </summary>
        /// <param name="width">Pixel width of the rendered image (multiple of 8).</param>
        /// <param name="height">Pixel height of the rendered image (multiple of 8).</param>
        /// <param name="image">ROM offset or GBA pointer of the tile data.</param>
        /// <param name="imageType">WF `ImageOption.SelectedIndex` value
        ///   (0 / 2 / 3 / 4 = LZ77; 1 = raw).</param>
        /// <param name="tsa">ROM offset or GBA pointer of the TSA data.</param>
        /// <param name="tsaType">WF `TSAOption.SelectedIndex` value (kept for parity).</param>
        /// <param name="palette">ROM offset or GBA pointer of the palette.</param>
        /// <param name="paletteType">WF `PaletteOption.SelectedIndex` value (kept for parity).</param>
        /// <param name="paletteCount">Number of 16-color rows the WF form would show.</param>
        /// <param name="image2">Secondary image address (WF "Image 2" — unused in AV preview).</param>
        public void Jump(int width, int height, uint image, int imageType,
            uint tsa, int tsaType, uint palette, int paletteType,
            int paletteCount, uint image2)
        {
            _vm.IsLoading = true;
            try
            {
                // Preserve a 0 pointer as 0x00000000 — only OR the
                // 0x08000000 base for non-zero ROM-offset values.
                // (Copilot bot review on PR #513 — the previous code
                // normalised 0 to 0x08000000 and would have shown a
                // null-pointer call as a valid ROM read.)
                _vm.ImageAddressText = $"0x{NormalizeGbaPointer(image):X08}";
                _vm.PaletteAddressText = $"0x{NormalizeGbaPointer(palette):X08}";

                // WF passes pixels; AV stores tile counts. Convert.
                _vm.TileCountX = System.Math.Max(1, width / 8);
                _vm.TileCountY = System.Math.Max(1, height / 8);

                // Mirror WF semantics from `GraphicsToolForm.Draw()` line 309:
                //   ImageOption.SelectedIndex == 0 || 3 || 4 → LZ77 compressed
                //   ImageOption.SelectedIndex == 1           → raw
                //   ImageOption.SelectedIndex == 2           → LZ77 (with image2)
                // Battle BG passes imageType=0, so IsCompressed must be true.
                _vm.IsCompressed = IsCompressedImageType(imageType);
                // Battle-BG graphics are 4bpp.
                _vm.Is4bpp = true;
            }
            finally
            {
                _vm.IsLoading = false;
            }

            // Auto-draw so the user sees the pre-populated image immediately
            // (mirrors WF where Jump sets the values and the existing
            // Draw_Click handler is invoked on focus events).
            try
            {
                var img = _vm.DrawTiles();
                if (img != null)
                {
                    ImageDisplay.Zoom = _vm.Zoom;
                    ImageDisplay.SetImage(img);
                }
            }
            catch { /* keep the view usable even if Draw fails */ }
        }

        /// <summary>
        /// Map the WinForms `GraphicsToolForm.ImageOption.SelectedIndex`
        /// value to whether the image data is LZ77-compressed. Source of
        /// truth: `GraphicsToolForm.Draw()` line 309 —
        /// <c>ImageOption.SelectedIndex == 0 || 3 || 4</c> means LZ77
        /// (圧縮画像), 1 means raw (無圧縮), 2 means LZ77 joined with
        /// `image2`.
        /// </summary>
        internal static bool IsCompressedImageType(int imageType)
        {
            return imageType == 0
                || imageType == 2
                || imageType == 3
                || imageType == 4;
        }

        /// <summary>
        /// Normalise a value into a GBA-format pointer for display.
        /// Preserves 0 (null pointer) as 0x00000000 rather than rewriting
        /// it to 0x08000000 — null pointers should not be shown as valid
        /// ROM addresses. (Copilot bot review on PR #513.)
        /// </summary>
        internal static uint NormalizeGbaPointer(uint value)
        {
            if (value == 0) return 0;
            if (value >= 0x08000000u) return value;
            return value | 0x08000000u;
        }
    }
}
