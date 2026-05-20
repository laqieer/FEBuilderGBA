using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media.Imaging;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// A non-interactive read-only icon preview that renders a bitmap inside a
    /// fixed-size outer box (<see cref="Scale"/> * <see cref="MaxImageWidth"/> by
    /// <see cref="Scale"/> * <see cref="MaxImageHeight"/>).
    ///
    /// Unlike <see cref="GbaImageControl"/> there is no zoom toolbar, no scroll
    /// viewer, and no pointer interaction — the control simply scales the bitmap
    /// up by <see cref="Scale"/> (nearest-neighbour) and lets Avalonia
    /// <c>Stretch="Uniform"</c> centre and clamp it inside the outer box. This
    /// fixes Avalonia list-preview slots clipping icons larger than the source
    /// hardware sprite (issue #342).
    /// </summary>
    public partial class IconPreviewControl : UserControl
    {
        WriteableBitmap? _bitmap;

        // ---- StyledProperty declarations so XAML may set values and the
        // property-changed handler fires for runtime updates. ----

        public static readonly StyledProperty<int> ScaleProperty =
            AvaloniaProperty.Register<IconPreviewControl, int>(nameof(Scale), defaultValue: 2);

        public static readonly StyledProperty<int> MaxImageWidthProperty =
            AvaloniaProperty.Register<IconPreviewControl, int>(nameof(MaxImageWidth), defaultValue: 32);

        public static readonly StyledProperty<int> MaxImageHeightProperty =
            AvaloniaProperty.Register<IconPreviewControl, int>(nameof(MaxImageHeight), defaultValue: 32);

        /// <summary>Integer up-scale factor applied to the bitmap (nearest-neighbour).</summary>
        public int Scale
        {
            get => GetValue(ScaleProperty);
            set => SetValue(ScaleProperty, value);
        }

        /// <summary>Maximum source-pixel width the preview slot reserves for the icon.</summary>
        public int MaxImageWidth
        {
            get => GetValue(MaxImageWidthProperty);
            set => SetValue(MaxImageWidthProperty, value);
        }

        /// <summary>Maximum source-pixel height the preview slot reserves for the icon.</summary>
        public int MaxImageHeight
        {
            get => GetValue(MaxImageHeightProperty);
            set => SetValue(MaxImageHeightProperty, value);
        }

        public IconPreviewControl()
        {
            InitializeComponent();
            UpdateOuterSize();
            UpdateImageSize();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ScaleProperty
                || change.Property == MaxImageWidthProperty
                || change.Property == MaxImageHeightProperty)
            {
                UpdateOuterSize();
                UpdateImageSize();
            }
        }

        /// <summary>
        /// Display an IImage (the cross-platform Core abstraction).
        /// Pass null to clear.
        /// </summary>
        public void SetImage(IImage? image)
        {
            if (image == null)
            {
                if (ImageDisplay != null) ImageDisplay.Source = null;
                _bitmap = null;
                UpdateImageSize();
                return;
            }

            _bitmap = IconBitmapBuilder.FromImage(image);
            if (ImageDisplay != null) ImageDisplay.Source = _bitmap;
            UpdateImageSize();
        }

        /// <summary>Display raw RGBA pixel data (mainly for tests).</summary>
        public void SetRgbaData(byte[] rgba, int width, int height)
        {
            _bitmap = IconBitmapBuilder.FromRgba(rgba, width, height);
            if (ImageDisplay != null) ImageDisplay.Source = _bitmap;
            UpdateImageSize();
        }

        /// <summary>True once a bitmap has been loaded.</summary>
        public bool HasImage => _bitmap != null;

        // ---- Internal helpers ----

        void UpdateOuterSize()
        {
            int scale = Math.Max(1, Scale);
            int maxW = Math.Max(1, MaxImageWidth);
            int maxH = Math.Max(1, MaxImageHeight);

            double outerW = scale * maxW;
            double outerH = scale * maxH;

            if (OuterBorder != null)
            {
                OuterBorder.Width = outerW;
                OuterBorder.Height = outerH;
            }
        }

        void UpdateImageSize()
        {
            if (ImageDisplay == null) return;

            if (_bitmap == null)
            {
                ImageDisplay.Width = double.NaN;
                ImageDisplay.Height = double.NaN;
                return;
            }

            int scale = Math.Max(1, Scale);
            int maxW = Math.Max(1, MaxImageWidth);
            int maxH = Math.Max(1, MaxImageHeight);

            double scaledW = _bitmap.PixelSize.Width * scale;
            double scaledH = _bitmap.PixelSize.Height * scale;
            double capW = scale * maxW;
            double capH = scale * maxH;

            // Stretch="Uniform" will centre+clamp at render time; we still set
            // explicit Width/Height so the layout pass reports the same size
            // tests can assert against.
            ImageDisplay.Width = Math.Min(scaledW, capW);
            ImageDisplay.Height = Math.Min(scaledH, capH);
        }
    }
}
