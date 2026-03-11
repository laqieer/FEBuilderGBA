using System;
using System.IO;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageViewerView : Window, IDataVerifiableView
    {
        readonly ImageViewerViewModel _vm = new();
        byte[]? _palette;

        public ViewModelBase? DataViewModel => _vm;

        public ImageViewerView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.Initialize();
            IconList.SelectedAddressChanged += OnIconSelected;
            Opened += (_, _) => LoadList();
            ImageControl.Zoom = _vm.Zoom;
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadIconList();
                IconList.SetItems(items);
                if (items.Count > 0)
                    IconList.SelectFirst();
            }
            catch (Exception ex)
            {
                Log.Error("ImageViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnIconSelected(uint addr)
        {
            try
            {
                var image = _vm.DrawIcon(addr);
                ImageControl.SetImage(image);
                _palette = _vm.GetPaletteBytes();
                _vm.ImageInfo = $"16x16 4bpp @ 0x{addr:X06}";
            }
            catch (Exception ex)
            {
                Log.Error("ImageViewerView.OnIconSelected failed: {0}", ex.Message);
                ImageControl.SetImage(null);
            }
        }

        /// <summary>Display an IImage.</summary>
        public void ShowImage(IImage? image, string? info = null)
        {
            ImageControl.SetImage(image);
            _vm.ImageInfo = info ?? "";
        }

        /// <summary>Display an IImage with optional GBA palette data for export.</summary>
        public void ShowImage(IImage? image, byte[]? palette, string? info = null)
        {
            _palette = palette;
            ImageControl.SetImage(image);
            _vm.ImageInfo = info ?? "";
        }

        /// <summary>Set palette data for export without changing the displayed image.</summary>
        public void SetPalette(byte[]? palette) => _palette = palette;

        async void ExportPng_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await ImageControl.ExportPng(this, "icon.png");
        }

        async void ExportPal_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (_palette == null || _palette.Length < 32) { CoreState.Services.ShowError("No palette data available"); return; }
                string? path = await FileDialogHelper.SavePaletteFile(this, "icon_palette.pal");
                if (string.IsNullOrEmpty(path)) return;
                PaletteFormat fmt = PaletteFormatConverter.FormatFromExtension(System.IO.Path.GetExtension(path));
                File.WriteAllBytes(path, PaletteFormatConverter.ExportToFormat(_palette, fmt));
            }
            catch (Exception ex) { CoreState.Services.ShowError($"Export palette failed: {ex.Message}"); }
        }

        void ZoomBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (ZoomBox.Value.HasValue)
            {
                int zoom = (int)ZoomBox.Value.Value;
                _vm.Zoom = zoom;
                ImageControl.Zoom = zoom;
            }
        }
    }
}
