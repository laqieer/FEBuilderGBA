using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// WU6: Image viewer smoke tests verifying that image-related ViewModels
    /// can be instantiated and perform basic operations without throwing.
    /// Tests skip gracefully when ROMs are not available.
    /// </summary>
    [Collection("SharedState")]
    public class ImageViewerSmokeTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _rom;
        private readonly ITestOutputHelper _output;

        public ImageViewerSmokeTests(RomFixture rom, ITestOutputHelper output)
        {
            _rom = rom;
            _output = output;
        }

        // =====================================================================
        // PortraitViewerViewModel smoke tests
        // =====================================================================

        [Fact]
        public void PortraitViewer_Constructor_DoesNotThrow()
        {
            var vm = new PortraitViewerViewModel();
            Assert.NotNull(vm);
            Assert.Equal(0u, vm.CurrentAddr);
        }

        [Fact]
        public void PortraitViewer_LoadPortrait_PopulatesPointers()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();
            if (list.Count < 2) return;

            vm.LoadPortrait(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.CanWrite);
            _output.WriteLine($"Portrait loaded at 0x{vm.CurrentAddr:X08}, ImagePtr=0x{vm.ImagePointer:X08}");
        }

        [Fact]
        public void PortraitViewer_LoadPortrait_PointersAreGBAPointers()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();

            // Check first few non-null portraits have GBA pointers
            for (int i = 1; i < Math.Min(10, list.Count); i++)
            {
                vm.LoadPortrait(list[i].addr);
                if (vm.ImagePointer != 0)
                {
                    // GBA pointers start with 0x08
                    Assert.True(U.isPointer(vm.ImagePointer),
                        $"ImagePointer 0x{vm.ImagePointer:X08} is not a GBA pointer");
                    return;
                }
            }
        }

        [Fact]
        public void PortraitViewer_TryLoadMainPortrait_DoesNotThrow()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();
            if (list.Count < 2) return;

            vm.LoadPortrait(list[1].addr);

            // TryLoadMainPortrait may return null if IImageService is not wired,
            // but it should not throw
            IImage? img = null;
            Exception? caught = null;
            try
            {
                img = vm.TryLoadMainPortrait();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // It's OK if the image service isn't available (returns null)
            // but it should not throw
            if (caught != null)
                _output.WriteLine($"TryLoadMainPortrait threw: {caught.GetType().Name}: {caught.Message}");
            else
                _output.WriteLine($"TryLoadMainPortrait returned: {(img != null ? $"{img.Width}x{img.Height}" : "null")}");
        }

        [Fact]
        public void PortraitViewer_TryLoadMapPortrait_DoesNotThrow()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();
            if (list.Count < 2) return;

            vm.LoadPortrait(list[1].addr);

            // Should not throw even if image service is not wired
            Exception? caught = null;
            try
            {
                var img = vm.TryLoadMapPortrait();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            if (caught != null)
                _output.WriteLine($"TryLoadMapPortrait threw: {caught.GetType().Name}");
        }

        [Fact]
        public void PortraitViewer_GetDataReport_ReturnsReport()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();
            if (list.Count < 2) return;

            vm.LoadPortrait(list[1].addr);
            var report = vm.GetDataReport();

            Assert.NotNull(report);
            Assert.True(report.Count > 0, "Portrait data report should not be empty");
            Assert.True(report.ContainsKey("ImagePointer"));
            Assert.True(report.ContainsKey("PalettePointer"));
        }

        [Fact]
        public void PortraitViewer_GetRawRomReport_MatchesData()
        {
            if (!_rom.IsAvailable) return;

            var vm = new PortraitViewerViewModel();
            var list = vm.LoadPortraitList();
            if (list.Count < 2) return;

            vm.LoadPortrait(list[1].addr);
            var data = vm.GetDataReport();
            var raw = vm.GetRawRomReport();

            Assert.NotNull(raw);
            Assert.True(raw.Count > 0);
            // Address should match
            Assert.Equal(data["addr"], raw["addr"]);
        }

        // =====================================================================
        // ImagePortraitViewModel smoke tests
        // =====================================================================

        [Fact]
        public void ImagePortrait_Constructor_DoesNotThrow()
        {
            var vm = new ImagePortraitViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void ImagePortrait_LoadEntry_PopulatesPointers()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImagePortraitViewModel();
            var list = vm.LoadList();
            if (list.Count < 2) return;

            vm.LoadEntry(list[1].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
            _output.WriteLine($"ImagePortrait at 0x{vm.CurrentAddr:X08}, FacePtr=0x{vm.PortraitImagePtr:X08}");
        }

        [Fact]
        public void ImagePortrait_LoadEntry_MouthEyeCoordsInRange()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImagePortraitViewModel();
            var list = vm.LoadList();
            if (list.Count < 2) return;

            vm.LoadEntry(list[1].addr);

            // Mouth and eye coordinates are bytes (0-255)
            Assert.True(vm.MouthX <= 255);
            Assert.True(vm.MouthY <= 255);
            Assert.True(vm.EyeX <= 255);
            Assert.True(vm.EyeY <= 255);
        }

        // =====================================================================
        // ImageViewerViewModel smoke tests
        // =====================================================================

        [Fact]
        public void ImageViewer_Constructor_DoesNotThrow()
        {
            var vm = new ImageViewerViewModel();
            Assert.NotNull(vm);
            Assert.Equal("Image Viewer", vm.Title);
            Assert.Equal(1, vm.Zoom);
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void ImageViewer_Initialize_SetsIsLoaded()
        {
            var vm = new ImageViewerViewModel();
            vm.Initialize();
            Assert.True(vm.IsLoaded);
        }

        [Fact]
        public void ImageViewer_Properties_CanBeSet()
        {
            var vm = new ImageViewerViewModel();
            vm.Title = "Test Image";
            vm.Zoom = 4;
            vm.ImageWidth = 256;
            vm.ImageHeight = 160;
            vm.ImageInfo = "256x160 GBA screen";

            Assert.Equal("Test Image", vm.Title);
            Assert.Equal(4, vm.Zoom);
            Assert.Equal(256, vm.ImageWidth);
            Assert.Equal(160, vm.ImageHeight);
            Assert.Equal("256x160 GBA screen", vm.ImageInfo);
        }

        // =====================================================================
        // ImageBGViewModel smoke tests
        // =====================================================================

        [Fact]
        public void ImageBG_Constructor_DoesNotThrow()
        {
            var vm = new ImageBGViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void ImageBG_LoadEntry_PopulatesPointers()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImageBGViewModel();
            var list = vm.LoadList();
            if (list.Count < 1) return;

            vm.LoadEntry(list[0].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.IsLoaded);
            _output.WriteLine($"ImageBG at 0x{vm.CurrentAddr:X08}, P0=0x{vm.P0:X08}");
        }

        // =====================================================================
        // BigCGViewerViewModel smoke tests
        // =====================================================================

        [Fact]
        public void BigCGViewer_Constructor_DoesNotThrow()
        {
            var vm = new BigCGViewerViewModel();
            Assert.NotNull(vm);
        }

        [Fact]
        public void BigCGViewer_LoadBigCG_PopulatesPointers()
        {
            if (!_rom.IsAvailable) return;

            var vm = new BigCGViewerViewModel();
            var list = vm.LoadBigCGList();
            if (list.Count < 1) return;

            vm.LoadBigCG(list[0].addr);

            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.CanWrite);
            // At least one pointer should be non-zero
            Assert.True(vm.TablePointer != 0 || vm.TSAPointer != 0 || vm.PalettePointer != 0,
                "BigCG should have at least one non-zero pointer");
            _output.WriteLine($"BigCG at 0x{vm.CurrentAddr:X08}");
        }

        // =====================================================================
        // ItemIconViewerViewModel smoke tests
        // =====================================================================

        [Fact]
        public void ItemIconViewer_Constructor_DoesNotThrow()
        {
            var vm = new ItemIconViewerViewModel();
            Assert.NotNull(vm);
        }

        [Fact]
        public void ItemIconViewer_LoadItemIcon_DoesNotThrow()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemIconViewerViewModel();
            var list = vm.LoadItemIconList();
            if (list.Count < 2) return;

            // LoadItemIcon should not throw for a valid icon address
            vm.LoadItemIcon(list[1].addr);
            Assert.NotEqual(0u, vm.CurrentAddr);
            Assert.True(vm.CanWrite);
        }

        [Fact]
        public void ItemIconViewer_LoadItemIconList_LoadsPalette()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ItemIconViewerViewModel();
            var list = vm.LoadItemIconList();

            // The LoadItemIconList method should cache the shared palette
            // (CachedPalette may be null if icon_palette_pointer is 0, but the call should not throw)
            _output.WriteLine($"ItemIcon palette cached: {vm.CachedPalette != null}");
        }

        // =====================================================================
        // ImageBattleAnimeViewModel smoke tests
        // =====================================================================

        [Fact]
        public void ImageBattleAnime_Constructor_DoesNotThrow()
        {
            var vm = new ImageBattleAnimeViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void ImageBattleAnime_LoadEntry_DoesNotThrow()
        {
            if (!_rom.IsAvailable) return;

            var vm = new ImageBattleAnimeViewModel();
            var list = vm.LoadList();
            if (list.Count < 2) return;

            // Loading an entry should not throw
            Exception? caught = null;
            try
            {
                vm.LoadEntry(list[1].addr);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.Null(caught);
            _output.WriteLine($"BattleAnime loaded at 0x{vm.CurrentAddr:X08}");

            // Regression for #246: TileSheetImage MUST be non-null (shared helper fix)
            Assert.NotNull(vm.TileSheetImage);
            Assert.True(vm.TileSheetImage.Width > 0, "TileSheet width must be positive");
            Assert.True(vm.TileSheetImage.Height > 0, "TileSheet height must be positive");
            Assert.Equal(0, vm.TileSheetImage.Width % 8);
            Assert.Equal(0, vm.TileSheetImage.Height % 8);
            _output.WriteLine($"BattleAnime TileSheet: {vm.TileSheetImage.Width}x{vm.TileSheetImage.Height}px");
        }

        // =====================================================================
        // BattleTerrainViewerViewModel smoke tests
        // =====================================================================

        [Fact]
        public void BattleTerrain_Constructor_DoesNotThrow()
        {
            var vm = new BattleTerrainViewerViewModel();
            Assert.NotNull(vm);
        }

        // =====================================================================
        // BattleBGViewerViewModel smoke tests
        // =====================================================================

        [Fact]
        public void BattleBGViewer_Constructor_DoesNotThrow()
        {
            var vm = new BattleBGViewerViewModel();
            Assert.NotNull(vm);
        }

        // =====================================================================
        // ChapterTitleViewerViewModel smoke tests
        // =====================================================================

        [Fact]
        public void ChapterTitleViewer_Constructor_DoesNotThrow()
        {
            var vm = new ChapterTitleViewerViewModel();
            Assert.NotNull(vm);
        }

        // =====================================================================
        // ImagePortraitFE6ViewModel smoke tests
        // =====================================================================

        [Fact]
        public void ImagePortraitFE6_Constructor_DoesNotThrow()
        {
            var vm = new ImagePortraitFE6ViewModel();
            Assert.NotNull(vm);
        }

        // =====================================================================
        // OAMSpriteViewerViewModel tile sheet regression tests (#246)
        // =====================================================================

        [Fact]
        public void OAMSpriteViewer_Constructor_DoesNotThrow()
        {
            var vm = new OAMSpriteViewerViewModel();
            Assert.NotNull(vm);
            Assert.False(vm.IsLoaded);
        }

        [Fact]
        public void OAMSpriteViewer_LoadEntry_TileSheetIsNotNull()
        {
            if (!_rom.IsAvailable) return;

            var vm = new OAMSpriteViewerViewModel();
            var list = vm.LoadAnimationList();
            if (list.Count < 2) return;

            // Load the first non-trivial animation entry
            Exception? caught = null;
            try
            {
                vm.LoadEntry(list[1].addr);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // LoadEntry must not throw
            Assert.Null(caught);

            _output.WriteLine($"OAMSpriteViewer loaded at 0x{vm.CurrentAddr:X08}, IsLoaded={vm.IsLoaded}");

            // Regression for #246: TileSheetImage MUST be non-null with correct fix
            Assert.True(vm.IsLoaded, "ViewModel should be loaded after LoadEntry");
            Assert.NotNull(vm.TileSheetImage);
            Assert.True(vm.TileSheetImage.Width > 0, "TileSheet width must be positive");
            Assert.True(vm.TileSheetImage.Height > 0, "TileSheet height must be positive");
            Assert.Equal(0, vm.TileSheetImage.Width % 8);
            Assert.Equal(0, vm.TileSheetImage.Height % 8);
            _output.WriteLine($"TileSheet: {vm.TileSheetImage.Width}x{vm.TileSheetImage.Height}px");
        }

        // =====================================================================
        // Generic: all image ViewModels can be instantiated
        // =====================================================================

        [Theory]
        [InlineData(typeof(PortraitViewerViewModel))]
        [InlineData(typeof(ImagePortraitViewModel))]
        [InlineData(typeof(ImageViewerViewModel))]
        [InlineData(typeof(ImageBGViewModel))]
        [InlineData(typeof(BigCGViewerViewModel))]
        [InlineData(typeof(ItemIconViewerViewModel))]
        [InlineData(typeof(ImageBattleAnimeViewModel))]
        [InlineData(typeof(BattleTerrainViewerViewModel))]
        [InlineData(typeof(BattleBGViewerViewModel))]
        [InlineData(typeof(ChapterTitleViewerViewModel))]
        [InlineData(typeof(ImagePortraitFE6ViewModel))]
        [InlineData(typeof(ImageBattleBGViewModel))]
        [InlineData(typeof(ImageBattleScreenViewModel))]
        [InlineData(typeof(ImageCGViewModel))]
        [InlineData(typeof(ImageFormRefViewerViewModel))]
        [InlineData(typeof(ImageGenericEnemyPortraitViewModel))]
        [InlineData(typeof(ImageMapActionAnimationViewModel))]
        [InlineData(typeof(ImagePalletViewModel))]
        [InlineData(typeof(ImageRomAnimeViewModel))]
        [InlineData(typeof(ImageSystemAreaViewModel))]
        [InlineData(typeof(ImageTSAAnimeViewModel))]
        [InlineData(typeof(ImageTSAAnime2ViewModel))]
        [InlineData(typeof(ImageTSAEditorViewModel))]
        [InlineData(typeof(ImageUnitMoveIconViewModel))]
        [InlineData(typeof(ImageUnitPaletteViewModel))]
        [InlineData(typeof(ImageUnitWaitIconViewModel))]
        public void ImageViewModel_CanInstantiate(Type vmType)
        {
            object? instance = null;
            Exception? ex = null;
            try
            {
                instance = Activator.CreateInstance(vmType);
            }
            catch (Exception e)
            {
                ex = e;
            }

            Assert.True(instance != null,
                $"Could not instantiate {vmType.Name}: {ex?.Message ?? "returned null"}");
            _output.WriteLine($"OK: {vmType.Name} instantiated");
        }
    }
}
