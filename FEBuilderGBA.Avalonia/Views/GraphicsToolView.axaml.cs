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

        // Captured from the last Jump(...) call so the "TSA Editor" button can
        // open ImageTSAEditorView with the same image/TSA/palette context. The
        // WinForms equivalent reads these straight off the GraphicsToolForm
        // controls in TSAEditorButton_Click; the Avalonia preview path keeps
        // them here because the VM does not own a TSA field.
        bool _tsaNavReady;
        int _navWidth, _navHeight, _navPaletteCount, _navTsaType;
        uint _navImageAddr, _navTsaAddr, _navPaletteAddr;

        public string ViewTitle => "Graphics Tool";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public GraphicsToolView()
        {
            InitializeComponent();

            // ComboBox.Items strings are NOT scanned by ViewTranslationHelper
            // (ClassOPDemoView precedent — Copilot bot review thread on PR #544),
            // so route the TSA-type labels through R._() so ja/zh locales pick
            // them up. The index order MUST equal the WF TSAOption index space
            // consumed by GraphicsToolViewViewModel.MapTsaType:
            //   0 = None, 1 = Compressed, 2 = Compressed Header,
            //   3 = Raw Header, 4 = Raw.
            // Populate BEFORE `DataContext = _vm` (below) so the SelectedIndex
            // binding applies onto already-present items (Copilot bot review on
            // PR #1075 — items must exist before the binding fires).
            TsaTypeCombo.Items.Add(R._("None"));
            TsaTypeCombo.Items.Add(R._("Compressed"));
            TsaTypeCombo.Items.Add(R._("Compressed Header"));
            TsaTypeCombo.Items.Add(R._("Raw Header"));
            TsaTypeCombo.Items.Add(R._("Raw"));

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

        /// <summary>
        /// Open the TSA Tile Editor pre-populated with the last Jump(...)
        /// image/TSA/palette context. Mirrors WinForms
        /// <c>GraphicsToolForm.TSAEditorButton_Click</c>: convert the direct
        /// data addresses to pointer slots via <c>U.GrepPointer</c> (the WF
        /// <c>refPointer</c> equivalent, which returns <c>U.NOT_FOUND</c> when
        /// no slot references the address), map the TSA option index to the
        /// header/LZ77 flags, then call <c>ImageTSAEditorView.Init</c>.
        /// </summary>
        void TSAEditor_Click(object? sender, RoutedEventArgs e)
        {
            if (!_tsaNavReady) return;

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            try
            {
                uint width8 = (uint)System.Math.Max(1, _navWidth / 8);
                uint height8 = (uint)System.Math.Max(1, _navHeight / 8);

                // refPointer(addr): find the pointer slot that references the
                // data address, or U.NOT_FOUND when none does.
                uint zimgPointer = U.GrepPointer(rom.Data, U.toPointer(_navImageAddr));
                uint tsaPointer = U.GrepPointer(rom.Data, U.toPointer(_navTsaAddr));

                // Mirror WF TSAOption.SelectedIndex mapping via the SHARED helper
                // (#1030 — the same GraphicsToolViewViewModel.MapTsaType the
                // TSA-composited preview path uses, so the button and the preview
                // agree on the flags):
                //   1 = LZ77; 2 = LZ77 + header; 3 = raw header; else raw.
                var (isLZ77TSA, isHeaderTSA) =
                    GraphicsToolViewViewModel.MapTsaType(_navTsaType);

                uint paletteOffset = U.toOffset(_navPaletteAddr);
                uint palettePointer = U.GrepPointer(rom.Data, U.toPointer(_navPaletteAddr));

                int paletteCount = _navPaletteCount;
                if (paletteCount <= 0) paletteCount = 1;

                var f = WindowManager.Instance.Open<ImageTSAEditorView>();
                f.Init(width8, height8, zimgPointer, isHeaderTSA, isLZ77TSA,
                       tsaPointer, palettePointer, paletteOffset, paletteCount);
            }
            catch (Exception ex)
            {
                Log.ErrorF("GraphicsToolView.TSAEditor_Click failed: {0}", ex.Message);
            }
        }

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
        /// Note (#1030 / #1074): the Avalonia <c>GraphicsToolViewViewModel</c>
        /// now consumes <paramref name="tsa"/> + <paramref name="tsaType"/> and
        /// routes the preview through <c>ImageTSAEditorCore.TryRenderMainImage</c>
        /// (TSA-composited, fixed 4bpp + LZ77 image). #1074 additionally wires
        /// <paramref name="image2"/> (2nd-image join, gated on
        /// <paramref name="imageType"/> == 2) and <paramref name="paletteType"/>
        /// == 1 (LZ77-compressed palette) into that same preview path.
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
        ///     in Battle BG callsites; 0 = raw. <c>paletteType == 1</c> now
        ///     selects the LZ77-compressed palette decode (#1074).</item>
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
        /// <param name="paletteType">WF `PaletteOption.SelectedIndex` value
        ///   (1 = LZ77-compressed palette decode, #1074).</param>
        /// <param name="paletteCount">Number of 16-color rows the WF form would show.</param>
        /// <param name="image2">Secondary image address (WF "Image 2"); joined
        ///   after the first when <paramref name="imageType"/> == 2 (#1074).</param>
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

                // #1030: feed the TSA context to the preview so existing
                // entrypoints (ImageBattleBGView, ImageBGView) open in
                // TSA-composited mode. A 0 TSA pointer maps to "" (empty box)
                // so a null TSA is not shown/read as a valid ROM address —
                // unlike image/palette above which format 0 as "0x00000000"
                // via NormalizeGbaPointer. The combo index space IS the WF
                // TSAOption space that MapTsaType consumes, so the incoming
                // tsaType maps to the combo index directly (clamped defensively
                // to the 5 populated items 0..4).
                _vm.TsaAddressText = tsa == 0 ? "" : $"0x{NormalizeGbaPointer(tsa):X08}";
                _vm.TsaTypeIndex = System.Math.Clamp(tsaType, 0, 4);

                // #1074: feed the image2-join + compressed-palette context to the
                // preview. A 0 image2 pointer maps to "" (empty box) like the TSA
                // address above. IsImage2Join is gated on the WF imageType == 2
                // (the join mode), NOT merely image2 != 0 (#1074 refinement #5),
                // so a non-join mode that still carries a 2nd address does not
                // accidentally join. IsCompressedPalette mirrors WF
                // PaletteOption == 1 (paletteType == 1).
                _vm.Image2AddressText = image2 == 0 ? "" : $"0x{NormalizeGbaPointer(image2):X08}";
                _vm.IsImage2Join = (imageType == 2);
                _vm.IsCompressedPalette = (paletteType == 1);

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

                // Snapshot the full TSA context for the "TSA Editor" button.
                _navWidth = width;
                _navHeight = height;
                _navImageAddr = image;
                _navTsaAddr = tsa;
                _navTsaType = tsaType;
                _navPaletteAddr = palette;
                _navPaletteCount = paletteCount;
                _tsaNavReady = true;
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

            // Intentional enable-on-Jump pattern (#860 regression anchor):
            // Initial state is disabled (no TSA context — axaml IsEnabled="False").
            // Jump() enables it once image/TSA/palette context is loaded.
            // Do NOT default to enabled — TSAEditor_Click requires _tsaNavReady.
            // Real callers (ImageBattleBGView, ImageBGView) call Open<GraphicsToolView>()
            // then Jump(); the standalone menu path (OpenGraphicsTool_Click) has no
            // TSA context so the button stays disabled. Both behaviors are CORRECT.
            TSAEditorButton.IsEnabled = _tsaNavReady;
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
