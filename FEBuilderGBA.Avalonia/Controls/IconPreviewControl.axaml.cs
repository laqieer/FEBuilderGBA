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
    /// <b>WinForms parity (issue #342 follow-up 2026-05-21):</b> WinForms ships
    /// the editor-area class/item icon previews as
    /// <c>InterpolatedPictureBox</c> with
    /// <c>PictureBoxSizeMode.StretchImage</c> and
    /// <c>InterpolationMode.Bicubic</c>
    /// (FEBuilderGBA/ClassForm.Designer.cs:2477-2486;
    /// ItemForm.Designer.cs:2096-2104;
    /// ClassFE6Form.Designer.cs:2055-2065). The picture-box always paints the
    /// bitmap at the picture-box's own dimensions, regardless of source size —
    /// so all wait-icon animTypes (16x16, 16x24, 32x32) render at the SAME
    /// visual size. To preserve that user-facing contract in Avalonia, the
    /// inner <see cref="Image"/> defaults to <see cref="Stretch.Fill"/> +
    /// <see cref="BitmapInterpolationMode.HighQuality"/> and always sizes to
    /// the outer Border.
    /// </para>
    /// <para>
    /// The <see cref="Stretch"/> and <see cref="BitmapInterpolationMode"/>
    /// styled properties let consumers opt back into the previous
    /// <see cref="Stretch.Uniform"/> / nearest-neighbour behaviour per slot if
    /// they need pixel-perfect aspect-preserving previews.
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
        /// <see cref="Stretch.Fill"/> to match WinForms
        /// <c>PictureBoxSizeMode.StretchImage</c> (issue #342 follow-up).
        /// </summary>
        public static readonly StyledProperty<Stretch> StretchProperty =
            AvaloniaProperty.Register<IconPreviewControl, Stretch>(nameof(Stretch), defaultValue: Stretch.Fill);

        /// <summary>
        /// Bitmap interpolation mode for the rendered icon. Defaults to
        /// <see cref="BitmapInterpolationMode.HighQuality"/> to match WinForms
        /// <c>InterpolationMode.Bicubic</c> (issue #342 follow-up).
        /// </summary>
        public static readonly StyledProperty<BitmapInterpolationMode> BitmapInterpolationModeProperty =
            AvaloniaProperty.Register<IconPreviewControl, BitmapInterpolationMode>(
                nameof(BitmapInterpolationMode), defaultValue: BitmapInterpolationMode.HighQuality);

        /// <summary>
        /// Integer up-scale factor applied to the outer Border (and inner
        /// Image) dimensions: outer = <c>Scale * MaxImageWidth</c> by
        /// <c>Scale * MaxImageHeight</c>. Sampling mode is governed by
        /// <see cref="BitmapInterpolationMode"/> — default
        /// <see cref="BitmapInterpolationMode.HighQuality"/> (matching
        /// WinForms <c>InterpolationMode.Bicubic</c>), NOT nearest-neighbour
        /// by default (was prior to the #342 follow-up).
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
        /// <see cref="Stretch.Fill"/>, matching WinForms
        /// <c>PictureBoxSizeMode.StretchImage</c>.
        /// </summary>
        public Stretch Stretch
        {
            get => GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        /// <summary>
        /// Interpolation mode applied to the rendered icon. Defaults to
        /// <see cref="BitmapInterpolationMode.HighQuality"/>, matching WinForms
        /// <c>InterpolationMode.Bicubic</c>.
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
                // No bitmap: collapse the inner Image so the outer box stays empty
                // (Stretch.Fill on a null Source paints nothing regardless).
                ImageDisplay.Width = double.NaN;
                ImageDisplay.Height = double.NaN;
                return;
            }

            // WinForms parity: the inner Image always matches the outer Border
            // size, regardless of source bitmap dimensions. Stretch=Fill (the
            // default) then stretches the bitmap to that fixed canvas — so a
            // 16x16, 16x24, or 32x32 source all render at the same visual size.
            int scale = Math.Max(1, Scale);
            int maxW = Math.Max(1, MaxImageWidth);
            int maxH = Math.Max(1, MaxImageHeight);

            ImageDisplay.Width = scale * maxW;
            ImageDisplay.Height = scale * maxH;
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
