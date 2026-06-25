using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextCharCodeView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly TextCharCodeViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Text Character Code";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public TextCharCodeView()
        {
            InitializeComponent();
            CharCodeList.ItemsSource = _vm.CharCodes;
            CharCodeList.SelectionChanged += CharCodeList_SelectionChanged;
            WriteButton.Click += OnWrite;
            _vm.Initialize();
            if (_vm.CharCodes.Count > 0)
            {
                CharCodeList.SelectedIndex = 0;
            }
        }

        void Close_Click(object? sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// Persist the edited Char Code (u16@0) and Terminator (u16@2) to the ROM under an
        /// ambient undo scope, then rebuild the Huffman text encoder so the change takes
        /// effect immediately. Mirrors WinForms <c>TextCharCodeForm</c>'s InputFormRef write
        /// button + <c>PostWriteHandler → Program.ReBuildFETextEncoder()</c> (#1446).
        /// </summary>
        void OnWrite(object? sender, RoutedEventArgs e)
        {
            // No entry selected → nothing to persist. _vm.Write() also guards on
            // CurrentAddr == 0, but bail early so we don't open an empty undo scope.
            if (_vm.CurrentAddr == 0)
            {
                return;
            }

            _undoService.Begin("Edit Char Code");
            try
            {
                _vm.CharCode = (uint)(CharCodeBox.Value ?? 0);
                _vm.TerminatorValue = (uint)(TerminatorBox.Value ?? 0);
                _vm.Write();

                // Equivalent to WinForms Program.ReBuildFETextEncoder(): rebuild the Huffman
                // text encoder so the encoding table change is picked up. Non-fatal if it
                // fails (matches MainWindow/RomFixture init pattern).
                try { CoreState.FETextEncoder = new FETextEncode(); }
                catch (Exception ex) { Log.Error($"TextCharCodeView: FETextEncode rebuild failed: {ex}"); }

                _undoService.Commit();
                _vm.MarkClean();

                // _vm.Write() refreshed the list label + CharacterDisplay; push the new
                // values back into the editor controls.
                UpdateUI();
                UpdateFontPreview();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"TextCharCodeView.OnWrite failed: {ex}");
            }
        }

        public void NavigateTo(uint address)
        {
            _vm.IsLoading = true;
            try
            {
                int index = _vm.IndexOfAddress(address);
                if (index >= 0)
                {
                    CharCodeList.SelectedIndex = index;
                }
                else
                {
                    _vm.LoadEntry(address);
                    UpdateUI();
                    UpdateFontPreview();
                }
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
            if (CharCodeList.SelectedIndex >= 0)
            {
                _vm.SelectIndex(CharCodeList.SelectedIndex);
                UpdateUI();
            }
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
