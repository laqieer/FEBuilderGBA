// SPDX-License-Identifier: GPL-3.0-or-later
// VM tests for the World Map Image (FE6) editor (#1183):
//   * the five zoom preview delegates return non-null with FE6 loaded,
//   * CanRender is true on FE6 / false on FE8,
//   * WritePointers round-trips (edit a field -> WritePointers -> read back the
//     slot -> restore),
//   * the no-ROM guard returns null / false without crashing.
using System;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class WorldMapImageFE6ViewModelTests
    {
        readonly ITestOutputHelper _output;

        public WorldMapImageFE6ViewModelTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void NoRom_PreviewsNull_CanRenderFalse()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new WorldMapImageFE6ViewModel();
                Assert.Null(vm.TryRenderZoomOut());
                Assert.Null(vm.TryRenderZoomNW());
                Assert.Null(vm.TryRenderZoomNE());
                Assert.Null(vm.TryRenderZoomSW());
                Assert.Null(vm.TryRenderZoomSE());

                vm.LoadEntry(0);
                Assert.False(vm.CanRender);
                Assert.False(vm.IsLoaded);
                // WritePointers on a null ROM must not throw.
                vm.WritePointers(null);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [AvaloniaFact]
        public void FE6_PreviewsNonNull_CanRenderTrue()
        {
            WithRom("FE6", vm =>
            {
                // Each render delegates verbatim to the Core helper, proven non-null
                // + 240x160 on the real FE6 by ImageWorldMapFE6CoreTests. Assert the
                // VM forwards the Core result faithfully (same null-ness).
                foreach (Func<IImage> render in new Func<IImage>[]
                {
                    vm.TryRenderZoomOut, vm.TryRenderZoomNW, vm.TryRenderZoomNE,
                    vm.TryRenderZoomSW, vm.TryRenderZoomSE
                })
                {
                    IImage img = render();
                    Assert.NotNull(img);
                    Assert.Equal(WorldMapImageFE6ViewModel.ZoomWidth, img.Width);
                    Assert.Equal(WorldMapImageFE6ViewModel.ZoomHeight, img.Height);
                    img.Dispose();
                }

                vm.LoadEntry(0);
                Assert.True(vm.IsLoaded);
                Assert.True(vm.CanRender, "CanRender must be true on FE6");
                Assert.True(vm.CanImportFull, "CanImportFull must be true on FE6");
                Assert.True(vm.CanImportNW && vm.CanImportNE && vm.CanImportSW && vm.CanImportSE);
                Assert.NotEqual(0u, vm.ImagePtrFull);
                Assert.NotEqual(0u, vm.PalettePtrFull);
            });
        }

        [AvaloniaFact]
        public void FE8_PreviewsNull_CanRenderFalse()
        {
            WithRom("FE8U", vm =>
            {
                // The FE6 256-liner render is FE6-only -> null on FE8.
                Assert.Null(vm.TryRenderZoomOut());

                vm.LoadEntry(0);
                Assert.False(vm.CanRender, "CanRender must be false on FE8");
                Assert.False(vm.CanImportFull, "CanImportFull must be false on FE8");
            });
        }

        [AvaloniaFact]
        public void WritePointers_RoundTrips()
        {
            WithRom("FE6", vm =>
            {
                ROM rom = CoreState.ROM;
                var savedUndo = CoreState.Undo;
                CoreState.Undo = new Undo();
                try
                {
                    vm.LoadEntry(0);
                    uint imgSlot = rom.RomInfo.worldmap_big_image_pointer; // slot 0 (full)
                    uint original = rom.p32(imgSlot);

                    // Edit the field to a known offset, write, read back the slot.
                    uint edited = 0x00345678u;
                    vm.ImagePtrFull = edited;

                    var undo = CoreState.Undo.NewUndoData("test");
                    vm.WritePointers(undo);
                    CoreState.Undo.Push(undo);

                    Assert.Equal(edited, rom.p32(imgSlot));

                    // Restore the original via undo.
                    CoreState.Undo.RunUndo();
                    Assert.Equal(original, rom.p32(imgSlot));
                }
                finally { CoreState.Undo = savedUndo; }
            });
        }

        // -----------------------------------------------------------------
        // Helper: load a specific real ROM, wire CoreState, run the body.
        // -----------------------------------------------------------------

        void WithRom(string version, Action<WorldMapImageFE6ViewModel> body)
        {
            string? path = TestRomLocator.FindRom(version);
            if (path == null)
            {
                _output.WriteLine($"{version}.gba not available — skipping.");
                return;
            }

            var savedRom = CoreState.ROM;
            var savedService = CoreState.ImageService;
            try
            {
                var rom = new ROM();
                if (!rom.Load(path, out string _))
                {
                    _output.WriteLine($"{version}.gba failed to load — skipping.");
                    return;
                }
                CoreState.ROM = rom;
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new SkiaImageService();

                body(new WorldMapImageFE6ViewModel());
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedService;
            }
        }
    }
}
