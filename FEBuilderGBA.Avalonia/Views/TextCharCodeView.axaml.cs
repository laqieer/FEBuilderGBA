using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextCharCodeView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextCharCodeViewModel _vm = new();

        public string ViewTitle => "Text Character Code";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextCharCodeView()
        {
            InitializeComponent();
            _vm.Initialize();
            CharCodeList.ItemsSource = _vm.CharCodes;
            CharCodeList.SelectionChanged += CharCodeList_SelectionChanged;
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        public void NavigateTo(uint address)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(address);
                UpdateUI();
                UpdateFontPreview();
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        public void SelectFirstItem() { if (_vm.CharCodes.Count > 0) CharCodeList.SelectedIndex = 0; }

        void CharCodeList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateFontPreview();
        }

        void UpdateUI()
        {
            CharCodeBox.Value = (decimal)_vm.CharCode;
            TerminatorBox.Value = (decimal)_vm.TerminatorValue;
            CharDisplayLabel.Text = _vm.CharacterDisplay;
        }

        void UpdateFontPreview()
        {
            uint code = _vm.CharCode;

            // Render item font glyph
            int itemWidth;
            byte[]? itemRgba = _vm.RenderGlyphRgba(code, true, out itemWidth);
            SetFontImage(ItemFontImage, itemRgba, 16, 16);
            _vm.ItemFontWidth = itemWidth;
            ItemFontWidthLabel.Text = _vm.ItemFontWidthText;

            // Render serif font glyph
            int serifWidth;
            byte[]? serifRgba = _vm.RenderGlyphRgba(code, false, out serifWidth);
            SetFontImage(SerifFontImage, serifRgba, 16, 16);
            _vm.SerifFontWidth = serifWidth;
            SerifFontWidthLabel.Text = _vm.SerifFontWidthText;
        }

        /// <summary>Set an RGBA byte array as the source of an Image control.</summary>
        static void SetFontImage(Image imageControl, byte[]? rgba, int width, int height)
        {
            if (rgba == null || rgba.Length < width * height * 4)
            {
                imageControl.Source = null;
                return;
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                global::Avalonia.Platform.PixelFormat.Rgba8888,
                global::Avalonia.Platform.AlphaFormat.Premul);

            using (var fb = bitmap.Lock())
            {
                unsafe
                {
                    byte* ptr = (byte*)fb.Address;
                    int stride = fb.RowBytes;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = (y * width + x) * 4;
                            int dstIdx = y * stride + x * 4;
                            ptr[dstIdx + 0] = rgba[srcIdx + 0]; // R
                            ptr[dstIdx + 1] = rgba[srcIdx + 1]; // G
                            ptr[dstIdx + 2] = rgba[srcIdx + 2]; // B
                            ptr[dstIdx + 3] = rgba[srcIdx + 3]; // A
                        }
                    }
                }
            }

            imageControl.Source = bitmap;
        }
    }
}
