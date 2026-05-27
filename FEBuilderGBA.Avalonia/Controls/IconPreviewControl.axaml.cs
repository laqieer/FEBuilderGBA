using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;

namespace FEBuilderGBA.Avalonia.Controls
{
    /// <summary>
    /// A non-interactive read-only icon preview that renders a bitmap inside a
    /// fixed-size outer box (<see cref="Scale"/> * <see cref="MaxImageWidth"/> by
    /// <see cref="Scale"/> * <see cref="MaxImageHeight"/>).
    ///
    /// Unlike <see cref="GbaImageControl"/> there is no zoom toolbar, no scroll
    /// viewer, and no pointer interaction.
    ///
    /// <para>
    /// <b>Natural-aspect rendering (issue #342 follow-up 2026-05-26):</b> the
    /// user clarified that class wait-icon sources genuinely have different
    /// aspects in the game (16x16, 16x24, 32x32 per animType) and should NOT
    /// be forced to a single visual size by aspect-distorting stretch. The
    /// inner <see cref="Image"/> defaults to <see cref="Stretch.Uniform"/> and
    /// <see cref="BitmapInterpolationMode.None"/> (nearest-neighbour, for
    /// pixel-art crispness). The inner Image is sized to
    /// <c>bitmap.PixelSize * Scale</c> clamped to
    /// <c>MaxImageWidth*Scale</c>/<c>MaxImageHeight*Scale</c>, so small icons
    /// render at their natural size centered in the outer Border, larger icons
    /// fill it, and aspect ratio is always preserved.
    /// </para>
    /// <para>
    /// The <see cref="Stretch"/> and <see cref="BitmapInterpolationMode"/>
    /// styled properties are surfaced so consumers can override the
    /// nearest-neighbour interpolation (e.g. set
    /// <see cref="BitmapInterpolationMode.HighQuality"/> for non-pixel-art
    /// previews) and override the <see cref="Stretch.Uniform"/> default
    /// (e.g. <see cref="Stretch.None"/> when a slot must show the bitmap
    /// 1:1 with no upscaling). Because the inner <see cref="Image"/> is
    /// sized to <c>bitmap.PixelSize * Scale</c> (clamped to the outer
    /// Border), changing <see cref="Stretch"/> to <see cref="Stretch.Fill"/>
    /// here only affects how the bitmap paints inside that already-aspect-
    /// preserving inner box — it does NOT make the icon stretch out to
    /// fill the entire outer Border the way WinForms
    /// <c>PictureBoxSizeMode.StretchImage</c> would. The natural-aspect
    /// sizing is the contract of this control by design (issue #342
    /// follow-up); pick a different control if you need WinForms
    /// StretchImage semantics.
    /// </para>
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

        /// <summary>
        /// How to stretch the bitmap inside the outer box. Defaults to
        /// <see cref="Stretch.Uniform"/> so source bitmaps render at their
        /// natural aspect ratio with no distortion (issue #342 follow-up
        /// 2026-05-26 — the user's stated preference: icons of different
        /// natural aspects should render at different visual sizes).
        /// </summary>
        public static readonly StyledProperty<Stretch> StretchProperty =
            AvaloniaProperty.Register<IconPreviewControl, Stretch>(nameof(Stretch), defaultValue: Stretch.Uniform);

        /// <summary>
        /// Bitmap interpolation mode for the rendered icon. Defaults to
        /// <see cref="BitmapInterpolationMode.None"/> (nearest-neighbour) so
        /// GBA pixel-art renders crisp at integer up-scale (issue #342
        /// follow-up 2026-05-26 — bicubic on tiny sprites blurs the pixels).
        /// </summary>
        public static readonly StyledProperty<BitmapInterpolationMode> BitmapInterpolationModeProperty =
            AvaloniaProperty.Register<IconPreviewControl, BitmapInterpolationMode>(
                nameof(BitmapInterpolationMode), defaultValue: BitmapInterpolationMode.None);

        /// <summary>
        /// Integer up-scale factor applied to the bitmap and to the outer
        /// Border. The outer Border is sized to
        /// <c>Scale * MaxImageWidth</c> by <c>Scale * MaxImageHeight</c> so it
        /// always reserves enough room for the largest expected icon. The
        /// inner Image is sized to <c>bitmap.PixelSize * Scale</c> (clamped to
        /// the outer Border) so each icon renders at its natural aspect and
        /// integer-scaled size (issue #342 follow-up 2026-05-26).
        /// </summary>
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

        /// <summary>
        /// Stretch mode applied to the inner Image. Defaults to
        /// <see cref="Stretch.Uniform"/> so the bitmap renders at its natural
        /// aspect ratio with no distortion (issue #342 follow-up 2026-05-26).
        /// </summary>
        public Stretch Stretch
        {
            get => GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        /// <summary>
        /// Interpolation mode applied to the rendered icon. Defaults to
        /// <see cref="BitmapInterpolationMode.None"/> (nearest-neighbour) for
        /// pixel-art crispness on integer up-scale (issue #342 follow-up
        /// 2026-05-26).
        /// </summary>
        public BitmapInterpolationMode BitmapInterpolationMode
        {
            get => GetValue(BitmapInterpolationModeProperty);
            set => SetValue(BitmapInterpolationModeProperty, value);
        }

        public IconPreviewControl()
        {
            InitializeComponent();
            UpdateOuterSize();
            UpdateImageSize();
            ApplyStretch();
            ApplyInterpolation();
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
            else if (change.Property == StretchProperty)
            {
                ApplyStretch();
            }
            else if (change.Property == BitmapInterpolationModeProperty)
            {
                ApplyInterpolation();
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
                // No bitmap: collapse the inner Image so the outer box stays empty.
                ImageDisplay.Width = double.NaN;
                ImageDisplay.Height = double.NaN;
                return;
            }

            // Natural-aspect rendering (issue #342 follow-up 2026-05-26): the
            // inner Image is sized to bitmap.PixelSize * Scale, clamped to the
            // outer Border. With Stretch=Uniform this preserves the source
            // aspect ratio with no distortion — so a 16x16 wait icon renders
            // at 32x32 (Scale=2) while a 16x24 renders at 32x48, both inside
            // the same 64x64 outer slot. Smaller icons appear centered within
            // the outer Border via the Image's HorizontalAlignment/VerticalAlignment.
            int scale = Math.Max(1, Scale);
            int maxW = Math.Max(1, MaxImageWidth);
            int maxH = Math.Max(1, MaxImageHeight);

            int srcW = _bitmap.PixelSize.Width;
            int srcH = _bitmap.PixelSize.Height;

            // Clamp scaled bitmap size to the outer Border so oversized sources
            // never paint outside the slot. Stretch=Uniform (the default) will
            // then preserve aspect even when the clamp kicks in.
            int innerW = Math.Min(scale * srcW, scale * maxW);
            int innerH = Math.Min(scale * srcH, scale * maxH);

            ImageDisplay.Width = innerW;
            ImageDisplay.Height = innerH;
        }

        void ApplyStretch()
        {
            if (ImageDisplay != null)
                ImageDisplay.Stretch = Stretch;
        }

        void ApplyInterpolation()
        {
            if (ImageDisplay != null)
                RenderOptions.SetBitmapInterpolationMode(ImageDisplay, BitmapInterpolationMode);
        }
    }
}
