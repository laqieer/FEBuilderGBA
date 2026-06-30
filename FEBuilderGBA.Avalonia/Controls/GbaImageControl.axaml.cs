using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Dialogs;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// Displays a GBA image (IImage) as an Avalonia WriteableBitmap.
    /// Supports cursor-centered mouse wheel zoom and click-drag pan via ScrollViewer.
    /// </summary>
    public partial class GbaImageControl : UserControl
    {
        WriteableBitmap? _bitmap;
        int _zoom = 1;

        // Drag-pan state
        bool _isPanning;
        Point _panStart;
        double _scrollStartX;
        double _scrollStartY;

        // Pinch-to-zoom state (#1726). PinchEventArgs.Scale is cumulative from
        // 1.0 for the duration of a gesture, so we capture the zoom at gesture
        // start and scale from it, resetting when the gesture ends.
        bool _isPinching;
        int _pinchBaseZoom;

        /// <summary>Minimum zoom factor.</summary>
        public const int ZoomMin = 1;

        /// <summary>Maximum zoom factor.</summary>
        public const int ZoomMax = 8;

        public GbaImageControl()
        {
            InitializeComponent();
            UpdateZoomLabel();

            // #1726: intercept the wheel in the TUNNEL phase so this handler runs
            // BEFORE the inner ScrollViewer's bubble-phase scroll handler. On the
            // modifier-zoom path we set e.Handled (pure zoom, no competing scroll);
            // on plain wheel we leave it unhandled so the ScrollViewer pans. Tunnel
            // ONLY (PointerWheelChangedEvent is Tunnel|Bubble) to avoid a double-fire.
            AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Tunnel);

            ImageScroller.PointerPressed += OnScrollerPointerPressed;
            ImageScroller.PointerMoved += OnScrollerPointerMoved;
            ImageScroller.PointerReleased += OnScrollerPointerReleased;

            // #1726: pinch-to-zoom for macOS trackpads / touch screens.
            ImageScroller.GestureRecognizers.Add(new PinchGestureRecognizer());
            Gestures.AddPinchHandler(ImageScroller, OnPinch);
            Gestures.AddPinchEndedHandler(ImageScroller, OnPinchEnded);
        }

        /// <summary>Zoom factor (1 = 1:1, 2 = 2x, etc.).</summary>
        public int Zoom
        {
            get => _zoom;
            set
            {
                int clamped = Math.Max(ZoomMin, Math.Min(ZoomMax, value));
                if (_zoom == clamped) return;
                _zoom = clamped;
                UpdateZoomLabel();
                UpdateDisplay();
            }
        }

        /// <summary>Display an IImage from Core.</summary>
        public void SetImage(IImage? image)
        {
            if (image == null)
            {
                ClearDisplay();
                return;
            }

            var bmp = IconBitmapBuilder.FromImage(image);
            if (bmp == null)
            {
                // Builder rejected the image (invalid dimensions, etc.) — clear
                // the surface so ImageDisplay.Source doesn't keep showing a
                // stale bitmap while HasImage reports false (issue #351 review).
                ClearDisplay();
                return;
            }
            _bitmap = bmp;
            UpdateDisplay();
        }

        /// <summary>Display raw RGBA pixel data.</summary>
        public void SetRgbaData(byte[] rgba, int width, int height)
        {
            var bmp = IconBitmapBuilder.FromRgba(rgba, width, height);
            if (bmp == null)
            {
                ClearDisplay();
                return;
            }
            _bitmap = bmp;
            UpdateDisplay();
        }

        void ClearDisplay()
        {
            _bitmap = null;
            if (ImageDisplay != null)
            {
                ImageDisplay.Source = null;
                ImageDisplay.Width = double.NaN;
                ImageDisplay.Height = double.NaN;
            }
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

        void UpdateZoomLabel()
        {
            if (ZoomLabel != null)
                ZoomLabel.Text = $"{_zoom}x";
        }

        /// <summary>
        /// Convert a pointer event into the displayed bitmap's source-pixel coordinates,
        /// accounting for the internal ScrollViewer + zoom. The earlier MapEditor click
        /// handler called <c>e.GetPosition(MapImageControl)</c> on the outer UserControl,
        /// which gave coordinates that did NOT account for the inner ImageDisplay's
        /// position inside the DockPanel/ScrollViewer or for the control's zoom — so a
        /// click landed at the wrong tile (issue #658).
        /// </summary>
        public bool TryGetSourcePixel(PointerEventArgs? e, out int srcX, out int srcY)
        {
            srcX = 0;
            srcY = 0;
            if (e == null || _bitmap == null || ImageDisplay == null) return false;
            var pos = e.GetPosition(ImageDisplay);
            // ImageDisplay has explicit Width/Height = pixel * _zoom (UpdateDisplay),
            // so each source pixel maps to _zoom display pixels. The actual math
            // lives in TryComputeSourcePixel so it can be unit-tested without
            // constructing a real PointerEventArgs.
            return TryComputeSourcePixel(pos.X, pos.Y, _zoom,
                _bitmap.PixelSize.Width, _bitmap.PixelSize.Height,
                out srcX, out srcY);
        }

        /// <summary>
        /// Pure-math helper: convert a pointer position (in <c>ImageDisplay</c> local
        /// DIPs) plus a zoom factor + source bitmap dimensions into source-pixel
        /// coordinates. Extracted so unit tests can validate the conversion at
        /// different zooms without having to fabricate a real
        /// <see cref="PointerEventArgs"/> (Copilot bot review on PR #726).
        /// </summary>
        internal static bool TryComputeSourcePixel(double posX, double posY, int zoom,
            int bitmapW, int bitmapH, out int srcX, out int srcY)
        {
            srcX = 0;
            srcY = 0;
            if (zoom <= 0 || bitmapW <= 0 || bitmapH <= 0) return false;
            if (posX < 0 || posY < 0) return false;
            int sx = (int)(posX / zoom);
            int sy = (int)(posY / zoom);
            if (sx >= bitmapW || sy >= bitmapH) return false;
            srcX = sx;
            srcY = sy;
            return true;
        }

        /// <summary>
        /// Whether a wheel event should zoom (vs. let the ScrollViewer pan).
        /// Zoom only when Control or Meta (⌘) is held; a plain wheel / macOS
        /// two-finger scroll pans. Pure helper so it can be unit-tested (#1726).
        /// </summary>
        internal static bool ShouldWheelZoom(KeyModifiers modifiers)
            => (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        /// <summary>
        /// Map a cumulative pinch scale (relative to the zoom captured at gesture
        /// start) to a clamped integer zoom factor. Pure helper for unit tests (#1726).
        /// </summary>
        internal static int PinchScaleToZoom(int baseZoom, double scale)
        {
            int z = (int)Math.Round(baseZoom * scale, MidpointRounding.AwayFromZero);
            return Math.Max(ZoomMin, Math.Min(ZoomMax, z));
        }

        /// <summary>
        /// Mouse-wheel zoom centered on the cursor, gated behind Ctrl/⌘ (#1726).
        /// Registered on the tunnel phase so it runs before the inner ScrollViewer:
        /// a plain wheel returns unhandled (ScrollViewer pans); a modifier wheel
        /// zooms and marks the event handled (suppressing the scroll).
        /// </summary>
        internal void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            // Nothing to zoom — let the event flow so the ScrollViewer can pan.
            if (_bitmap == null) return;

            // Plain wheel / two-finger scroll: don't handle, so the ScrollViewer pans.
            if (!ShouldWheelZoom(e.KeyModifiers)) return;

            int oldZoom = _zoom;
            int newZoom = e.Delta.Y > 0 ? oldZoom + 1 : oldZoom - 1;
            newZoom = Math.Max(ZoomMin, Math.Min(ZoomMax, newZoom));

            // Cursor-centered zoom keeps the content point under the cursor stable.
            if (newZoom != oldZoom)
                ZoomCenteredOn(newZoom, e.GetPosition(ImageScroller));

            // Consume the modifier-wheel even at the zoom clamp so it doesn't fall
            // through to the ScrollViewer and scroll instead of zooming.
            e.Handled = true;
        }

        /// <summary>Pinch-to-zoom centered on the gesture origin (#1726).</summary>
        void OnPinch(object? sender, PinchEventArgs e)
        {
            if (_bitmap == null) return;

            // e.Scale is cumulative from 1.0 for the whole gesture, so capture the
            // zoom at the first frame and scale from it.
            if (!_isPinching)
            {
                _isPinching = true;
                _pinchBaseZoom = _zoom;
            }

            int newZoom = PinchScaleToZoom(_pinchBaseZoom, e.Scale);
            if (newZoom != _zoom)
                ZoomCenteredOn(newZoom, e.ScaleOrigin);
            e.Handled = true;
        }

        void OnPinchEnded(object? sender, PinchEndedEventArgs e)
        {
            _isPinching = false;
            e.Handled = true;
        }

        /// <summary>
        /// Apply a new zoom while keeping the content point under
        /// <paramref name="centerInScroller"/> (a point in ScrollViewer-viewport
        /// coordinates) stationary. Shared by wheel and pinch zoom (#1726).
        /// </summary>
        void ZoomCenteredOn(int newZoom, Point centerInScroller)
        {
            int oldZoom = _zoom;
            if (newZoom == oldZoom) return;

            // Content coordinate under the center before zoom.
            double contentX = ImageScroller.Offset.X + centerInScroller.X;
            double contentY = ImageScroller.Offset.Y + centerInScroller.Y;

            // Apply zoom (bypassing the property to avoid a double UpdateDisplay).
            _zoom = newZoom;
            UpdateZoomLabel();
            UpdateDisplay();

            // Scale the content coordinate to the new zoom and recompute the scroll
            // offset so the same image point stays under the center.
            double scale = (double)newZoom / oldZoom;
            ImageScroller.Offset = new Vector(
                Math.Max(0, contentX * scale - centerInScroller.X),
                Math.Max(0, contentY * scale - centerInScroller.Y));
        }

        /// <summary>
        /// Begin drag-pan on middle mouse button, or left button when zoomed beyond 1x.
        /// </summary>
        internal void OnScrollerPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(ImageScroller).Properties;
            bool isMiddle = props.IsMiddleButtonPressed;
            bool isLeftZoomed = props.IsLeftButtonPressed && _zoom > 1;

            if (isMiddle || isLeftZoomed)
            {
                _isPanning = true;
                _panStart = e.GetPosition(ImageScroller);
                _scrollStartX = ImageScroller.Offset.X;
                _scrollStartY = ImageScroller.Offset.Y;
                e.Handled = true;
            }
        }

        /// <summary>Update scroll offset during drag-pan.</summary>
        internal void OnScrollerPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isPanning) return;

            Point current = e.GetPosition(ImageScroller);
            double dx = _panStart.X - current.X;
            double dy = _panStart.Y - current.Y;

            ImageScroller.Offset = new Vector(
                Math.Max(0, _scrollStartX + dx),
                Math.Max(0, _scrollStartY + dy));

            e.Handled = true;
        }

        /// <summary>End drag-pan.</summary>
        internal void OnScrollerPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                e.Handled = true;
            }
        }

        /// <summary>Whether a drag-pan operation is in progress.</summary>
        public bool IsPanning => _isPanning;

        /// <summary>Zoom in button click.</summary>
        void OnZoomInClick(object? sender, RoutedEventArgs e) => Zoom++;

        /// <summary>Zoom out button click.</summary>
        void OnZoomOutClick(object? sender, RoutedEventArgs e) => Zoom--;

        /// <summary>Reset zoom to 1x.</summary>
        void OnZoomResetClick(object? sender, RoutedEventArgs e) => Zoom = 1;

        /// <summary>Whether a bitmap is available for export.</summary>
        public bool HasImage => _bitmap != null;

        /// <summary>Export the current image as PNG via a save dialog.</summary>
        public async Task ExportPng(Window owner, string? suggestedName = null)
        {
            if (_bitmap == null) return;

            // #1639: write via the SAF bridge so Android content:// targets
            // (no local path) are written through OpenWriteAsync.
            var bmp = _bitmap;
            await FileDialogHelper.SaveImageFileVia(owner, suggestedName, async path =>
            {
                await using var stream = File.Create(path);
                bmp.Save(stream);
            });
        }
    }
}
