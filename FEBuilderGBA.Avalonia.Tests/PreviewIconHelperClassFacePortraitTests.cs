using System;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for <see cref="PreviewIconHelper.LoadClassFacePortrait"/>.
    /// Issue #357: Avalonia Class Editor needs a class-card preview matching
    /// the WinForms <c>L_8_PORTRAIT_CLASS</c> picture box. The helper must
    /// resolve a class's portrait ID to a portrait-table entry, then render the
    /// class-card image with version-correct struct layout:
    ///   FE6   16-byte struct, D0=unit_face (used as class card when D4==0)
    ///   FE7/8 28-byte struct, D16=class_card
    /// </summary>
    [Collection("SharedState")]
    public class PreviewIconHelperClassFacePortraitTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public PreviewIconHelperClassFacePortraitTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// Skip when no ROM was loaded at all (CI/local without ROM available).
        /// When a ROM IS loaded but it is not FE8U, hard-fail so misconfigured
        /// fixtures do not silently pass FE8U-specific assertions. This mirrors
        /// the <c>TryAssertFE8U</c> pattern used elsewhere in this test suite
        /// (e.g., <see cref="ClassEditorListPreviewTests"/>).
        /// PR #471 Copilot inline review fix.
        /// </summary>
        bool TryAssertFE8U()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("RomFixture not available — skipping ROM-backed assertions.");
                return false;
            }
            Assert.Equal("FE8U", _fixture.Version);
            return true;
        }

        /// <summary>
        /// Ensure CoreState.ImageService is wired so PortraitRendererCore can
        /// decode 4bpp tiles. RomFixture itself does not register one.
        /// </summary>
        static IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            if (prev == null)
                CoreState.ImageService = new SkiaImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            private readonly IImageService? _prev;
            public RestoreImageService(IImageService? prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        [Fact]
        public void LoadClassFacePortrait_ZeroId_ReturnsNull()
        {
            // portraitId 0 = no portrait, helper must short-circuit.
            // Works without a ROM since the helper checks portraitId first.
            var img = PreviewIconHelper.LoadClassFacePortrait(0);
            Assert.Null(img);
        }

        /// <summary>
        /// Class id 1 in FE8U is the first non-placeholder class entry with a
        /// non-zero portrait id (id 0 is a placeholder with no portrait).
        /// </summary>
        [Fact]
        public void LoadClassFacePortrait_FE8U_ClassId1_ReturnsImage()
        {
            if (!TryAssertFE8U()) return;
            using var _ = EnsureImageService();

            ROM rom = CoreState.ROM!;
            uint classBase = rom.p32(rom.RomInfo.class_pointer);
            uint classAddr = classBase + 1 * rom.RomInfo.class_datasize;
            uint portraitId = rom.u16(classAddr + 8);
            Assert.True(portraitId > 0, "Expected class id 1 to have non-zero portrait id");

            using var img = PreviewIconHelper.LoadClassFacePortrait(portraitId);
            Assert.NotNull(img);
            Assert.True(img!.Width > 0 && img.Height > 0,
                $"Expected non-degenerate class face portrait; got {img.Width}x{img.Height}");
        }

        [Fact]
        public void LoadClassFacePortrait_FE8U_ImageWidth_Is80px()
        {
            if (!TryAssertFE8U()) return;
            using var _ = EnsureImageService();

            ROM rom = CoreState.ROM!;
            uint classBase = rom.p32(rom.RomInfo.class_pointer);
            uint classAddr = classBase + 1 * rom.RomInfo.class_datasize;
            uint portraitId = rom.u16(classAddr + 8);

            using var img = PreviewIconHelper.LoadClassFacePortrait(portraitId);
            Assert.NotNull(img);
            // PortraitRendererCore.DrawPortraitClass uses widthTiles=10 -> 80px.
            Assert.Equal(80, img!.Width);
        }

        [Fact]
        public void LoadClassFacePortrait_FE8U_InvalidPortraitId_ReturnsNull()
        {
            if (!TryAssertFE8U()) return;
            using var _ = EnsureImageService();

            // Wildly out-of-range portrait id pushes the entry beyond ROM end.
            var img = PreviewIconHelper.LoadClassFacePortrait(0xFFFFu);
            Assert.Null(img);
        }
    }
}
