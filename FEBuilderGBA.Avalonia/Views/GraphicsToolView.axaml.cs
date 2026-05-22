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
        /// Pre-populate the Graphics Tool with image / TSA / palette
        /// addresses and re-draw. Mirrors WinForms
        /// <c>GraphicsToolForm.Jump(int width, int height, uint image,
        /// int imageType, uint tsa, int tsaType, uint palette,
        /// int paletteType, int paletteCount, uint image2)</c> — the WF
        /// signature takes pixels and converts to tile counts via
        /// `PicWidth.Value = width / 8`. The Avalonia VM uses tile counts
        /// directly, so we perform the same `/ 8` conversion here.
        ///
        /// Parameter semantics mirror WF:
        ///   imageType: 0 = raw, 1 = LZ77 compressed (4bpp default).
        ///   paletteCount: number of palette rows the WF form would show.
        /// </summary>
        /// <param name="width">Pixel width of the rendered image (multiple of 8).</param>
        /// <param name="height">Pixel height of the rendered image (multiple of 8).</param>
        /// <param name="image">ROM offset or GBA pointer of the tile data.</param>
        /// <param name="imageType">0 = raw, 1 = LZ77-compressed.</param>
        /// <param name="tsa">ROM offset or GBA pointer of the TSA data.</param>
        /// <param name="tsaType">0 = raw, 1 = LZ77-compressed (unused here but kept for parity).</param>
        /// <param name="palette">ROM offset or GBA pointer of the palette.</param>
        /// <param name="paletteType">0 = raw, 1 = LZ77-compressed (unused here but kept for parity).</param>
        /// <param name="paletteCount">Number of 16-color rows the WF form would show.</param>
        /// <param name="image2">Secondary image address (WF "Image 2" — unused in AV preview).</param>
        public void Jump(int width, int height, uint image, int imageType,
            uint tsa, int tsaType, uint palette, int paletteType,
            int paletteCount, uint image2)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.ImageAddressText = $"0x{(image >= 0x08000000u ? image : image | 0x08000000u):X08}";
                _vm.PaletteAddressText = $"0x{(palette >= 0x08000000u ? palette : palette | 0x08000000u):X08}";

                // WF passes pixels; AV stores tile counts. Convert.
                _vm.TileCountX = System.Math.Max(1, width / 8);
                _vm.TileCountY = System.Math.Max(1, height / 8);

                // imageType==1 → LZ77-compressed.
                _vm.IsCompressed = imageType == 1;
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
    }
}
