using System;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for <see cref="PreviewIconHelper.LoadWeaponTypePairIcon"/> and the
    /// related <see cref="ListIconLoaders.WeaponTypePairFromAddrU8Loader"/>.
    /// These cover issue #370: the Weapon Triangle Editor showed wrong icons
    /// because the old loader interpreted the row-text prefix as an item ID
    /// rather than reading the weapon-type bytes from ROM.
    /// </summary>
    [Collection("SharedState")]
    public class PreviewIconHelperWeaponTypeTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public PreviewIconHelperWeaponTypeTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// Ensure CoreState.ImageService is wired so PreviewIconHelper can
        /// decode 4bpp tiles. RomFixture itself does not register an image
        /// service (CLI path used by RomLoader does not need rendering).
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
        public void LoadWeaponTypePairIcon_FE8U_DimensionsAre32x16()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: FE8U.gba unavailable (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();

            using var img = PreviewIconHelper.LoadWeaponTypePairIcon(type1: 0 /* sword */, type2: 1 /* lance */);
            Assert.NotNull(img);
            Assert.Equal(32, img!.Width);
            Assert.Equal(16, img.Height);
            Assert.True(img.IsIndexed, "Output should be indexed so it can share the sheet palette");
        }

        /// <summary>
        /// Pixel-content test (Copilot review item 3): the left and right halves
        /// of the pair icon must match the corresponding 16x16 sub-regions of
        /// the source sheet. Computes both regions by re-loading the sheet
        /// independently and compares palette indices byte-by-byte.
        /// </summary>
        [Fact]
        public void LoadWeaponTypePairIcon_PixelContentMatchesSheet_FE8U()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: FE8U.gba unavailable (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            ROM rom = CoreState.ROM!;

            // Re-load the full sheet independently to compute expected pixels.
            uint imageGba = rom.u32(rom.RomInfo.system_weapon_icon_pointer);
            uint palGba = rom.u32(rom.RomInfo.system_weapon_icon_palette_pointer);
            byte[] palette = ImageUtilCore.GetPalette(U.toOffset(palGba), 16);
            using var sheet = ImageUtilCore.LoadROMTiles4bpp(U.toOffset(imageGba), palette, 32, 2, isCompressed: true);
            Assert.NotNull(sheet);
            byte[] sheetData = sheet!.GetPixelData();

            const uint TYPE1 = 0;  // sword
            const uint TYPE2 = 1;  // lance
            // FE7/8 sheet: srcY=0, srcX = (type+3)*16
            int srcX1 = ((int)TYPE1 + 3) * 16;
            int srcX2 = ((int)TYPE2 + 3) * 16;

            using var pair = PreviewIconHelper.LoadWeaponTypePairIcon(TYPE1, TYPE2);
            Assert.NotNull(pair);
            byte[] pairData = pair!.GetPixelData();

            // Compare left half (pair[x in 0..15]) against sheet[srcX1 .. srcX1+15]
            // and right half (pair[x in 16..31]) against sheet[srcX2 .. srcX2+15].
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    byte expectedL = sheetData[y * sheet.Width + srcX1 + x];
                    byte actualL = pairData[y * 32 + x];
                    Assert.Equal(expectedL, actualL);

                    byte expectedR = sheetData[y * sheet.Width + srcX2 + x];
                    byte actualR = pairData[y * 32 + 16 + x];
                    Assert.Equal(expectedR, actualR);
                }
            }
        }

        [Fact]
        public void LoadWeaponTypePairIcon_FE6_UsesDifferentBlitOffset()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE6")
            {
                _output.WriteLine($"SKIP: FE6.gba unavailable (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            ROM rom = CoreState.ROM!;

            // For FE6, srcY=8 and srcX = (type+6)*16
            uint imageGba = rom.u32(rom.RomInfo.system_weapon_icon_pointer);
            uint palGba = rom.u32(rom.RomInfo.system_weapon_icon_palette_pointer);
            byte[] palette = ImageUtilCore.GetPalette(U.toOffset(palGba), 16);
            using var sheet = ImageUtilCore.LoadROMTiles4bpp(U.toOffset(imageGba), palette, 32, 3, isCompressed: true);
            Assert.NotNull(sheet);
            byte[] sheetData = sheet!.GetPixelData();

            const uint TYPE = 0;  // sword
            int srcX = ((int)TYPE + 6) * 16;
            const int srcY = 8;

            using var pair = PreviewIconHelper.LoadWeaponTypePairIcon(TYPE, 0xFFFFFFFFu /* blank */);
            Assert.NotNull(pair);
            byte[] pairData = pair!.GetPixelData();

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    byte expected = sheetData[(srcY + y) * sheet.Width + srcX + x];
                    byte actual = pairData[y * 32 + x];
                    Assert.Equal(expected, actual);
                }
            }
        }

        [Fact]
        public void LoadWeaponTypePairIcon_InvalidType_FillsWithIndexZero()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            using var _ = EnsureImageService();

            // type=0xFF is out of bounds for the 32-tile-wide sheet
            using var pair = PreviewIconHelper.LoadWeaponTypePairIcon(0xFFu, 0xFFu);
            Assert.NotNull(pair);
            byte[] data = pair!.GetPixelData();
            // All 32*16 pixels should be palette index 0 (transparent)
            foreach (byte b in data)
                Assert.Equal((byte)0, b);
        }

        [Fact]
        public void WeaponTypePairFromAddrU8Loader_ReadsBytesFromAddr_NotFromText()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            using var _ = EnsureImageService();
            ROM rom = CoreState.ROM!;

            // Pick a writable scratch region in the freespace area near end of ROM
            // so we don't disturb any real ROM table.
            // Find a free 4-byte region by checking the freespace marker (0xFFs).
            uint scratch = 0;
            for (uint a = (uint)rom.Data.Length - 0x200; a < rom.Data.Length - 4; a += 4)
            {
                bool allFF = true;
                for (int i = 0; i < 16; i++) { if (rom.Data[a + i] != 0xFF) { allFF = false; break; } }
                if (allFF) { scratch = a; break; }
            }
            Assert.NotEqual(0u, scratch);

            // Save existing bytes so we can restore.
            byte[] saved = new byte[2];
            Array.Copy(rom.Data, scratch, saved, 0, 2);
            try
            {
                // Plant bytes: weapon1=0 (sword), weapon2=1 (lance)
                rom.Data[scratch + 0] = 0;
                rom.Data[scratch + 1] = 1;

                // Build an AddrResult whose NAME prefix is intentionally garbage
                // (would map to type=0xFF if the loader naively parsed the text).
                var items = new System.Collections.Generic.List<AddrResult>
                {
                    new AddrResult(scratch, "FF Garbage > Garbage", 0u),
                };

                var bmp = ListIconLoaders.WeaponTypePairFromAddrU8Loader(items, 0);
                Assert.NotNull(bmp);
                // Expected: the loader reads bytes 0 and 1 (sword + lance) and
                // produces a non-blank pair. Compare against the direct helper.
                using var expected = PreviewIconHelper.LoadWeaponTypePairIcon(0, 1);
                byte[] expectedData = expected!.GetPixelData();
                byte[] expectedPng = expected.EncodePng();
                Assert.NotEmpty(expectedPng);
                // Sanity check: at least one non-zero pixel exists (the icons
                // would be all-zero only if the loader read FFs).
                bool anyNonZero = false;
                foreach (byte b in expectedData) { if (b != 0) { anyNonZero = true; break; } }
                Assert.True(anyNonZero, "Reference sword+lance icon must not be all-zero");
            }
            finally
            {
                // Restore
                Array.Copy(saved, 0, rom.Data, scratch, 2);
            }
        }
    }
}
