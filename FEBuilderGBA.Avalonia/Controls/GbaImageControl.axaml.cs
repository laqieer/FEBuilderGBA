using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Displays a GBA image (IImage) as an Avalonia WriteableBitmap.
    /// Supports zoom and pixel-perfect rendering.
    /// </summary>
    public partial class GbaImageControl : UserControl
    {
        WriteableBitmap? _bitmap;
        int _zoom = 2;

        public GbaImageControl()
        {
            InitializeComponent();
        }

        /// <summary>Zoom factor (1 = 1:1, 2 = 2x, etc.).</summary>
        public int Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Max(1, Math.Min(8, value));
                UpdateDisplay();
            }
        }

        /// <summary>Display an IImage from Core.</summary>
        public void SetImage(IImage? image)
        {
            if (image == null)
            {
                ImageDisplay.Source = null;
                _bitmap = null;
                return;
            }

            byte[] rgba = image.GetPixelData();
            int w = image.Width;
            int h = image.Height;

            // If indexed, the pixel data is palette indices — get RGBA instead
            if (image.IsIndexed)
            {
                byte[] palette = image.GetPaletteRGBA();
                byte[] indexData = rgba;
                rgba = new byte[w * h * 4];
                for (int i = 0; i < w * h; i++)
                {
                    int palIdx = indexData[i];
                    if (palIdx * 4 + 3 < palette.Length)
                    {
                        rgba[i * 4 + 0] = palette[palIdx * 4 + 0];
                        rgba[i * 4 + 1] = palette[palIdx * 4 + 1];
                        rgba[i * 4 + 2] = palette[palIdx * 4 + 2];
                        rgba[i * 4 + 3] = palIdx == 0 ? (byte)0 : palette[palIdx * 4 + 3];
                    }
                }
            }

            SetRgbaData(rgba, w, h);
        }

        /// <summary>Display raw RGBA pixel data.</summary>
        public void SetRgbaData(byte[] rgba, int width, int height)
        {
            if (rgba == null || rgba.Length < width * height * 4) return;

            _bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                global::Avalonia.Platform.PixelFormat.Rgba8888,
                global::Avalonia.Platform.AlphaFormat.Premul);

            using (var fb = _bitmap.Lock())
            {
                unsafe
                {
                    var ptr = (byte*)fb.Address;
                    int stride = fb.RowBytes;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = (y * width + x) * 4;
                            int dstIdx = y * stride + x * 4;
                            byte a = rgba[srcIdx + 3];
                            if (a == 0)
                            {
                                // Premul: alpha=0 → all channels must be 0
                                ptr[dstIdx] = 0;
                                ptr[dstIdx + 1] = 0;
                                ptr[dstIdx + 2] = 0;
                                ptr[dstIdx + 3] = 0;
                            }
                            else
                            {
                                ptr[dstIdx] = rgba[srcIdx];
                                ptr[dstIdx + 1] = rgba[srcIdx + 1];
                                ptr[dstIdx + 2] = rgba[srcIdx + 2];
                                ptr[dstIdx + 3] = a;
                            }
                        }
                    }
                }
            }

            UpdateDisplay();
        }

        void UpdateDisplay()
        {
            if (_bitmap != null)
            {
                ImageDisplay.Source = _bitmap;
                ImageDisplay.Width = _bitmap.PixelSize.Width * _zoom;
                ImageDisplay.Height = _bitmap.PixelSize.Height * _zoom;
            }
        }

        /// <summary>Whether a bitmap is available for export.</summary>
        public bool HasImage => _bitmap != null;

        /// <summary>Export the current image as PNG via a save dialog.</summary>
        public async Task ExportPng(Window owner, string? suggestedName = null)
        {
            if (_bitmap == null) return;

            string? path = await FileDialogHelper.SaveImageFile(owner, suggestedName);
            if (string.IsNullOrEmpty(path)) return;

            using var stream = File.Create(path);
            _bitmap.Save(stream);
        }
    }
}
