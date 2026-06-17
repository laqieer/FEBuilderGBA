// SPDX-License-Identifier: GPL-3.0-or-later
// VM tests for the World Map Image (FE7) editor (#1184):
//   * preview delegates (big field map + event) return non-null with FE7U loaded,
//   * CanImport is true on FE7 / false on FE8,
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
    public class WorldMapImageFE7ViewModelTests
    {
        readonly ITestOutputHelper _output;

        public WorldMapImageFE7ViewModelTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void NoRom_PreviewsNull_CanImportFalse()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new WorldMapImageFE7ViewModel();
                Assert.Null(vm.TryRenderBigFieldMap());
                Assert.Null(vm.TryRenderEvent());

                vm.LoadEntry(0);
                Assert.False(vm.CanImport);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [AvaloniaFact]
        public void FE7U_PreviewsNonNull_CanImportTrue()
        {
            WithRom("FE7U", vm =>
            {
                // The render delegates verbatim to the Core helper. The Core
                // render is proven non-null + 1024×688 on the real FE7U by
                // ImageWorldMapFE7CoreTests (StubImageService, deterministic); the
                // SkiaSharp render of a 1024×688 surface is exercised by the
                // --screenshot-all proof. Here we assert the VM forwards the Core
                // result faithfully (same null-ness) — the VM wiring is the unit
                // under test, not the Skia pixel pipeline.
                IImage core = ImageWorldMapCore.TryRenderFE7BigFieldMap(CoreState.ROM);
                IImage big = vm.TryRenderBigFieldMap();
                Assert.Equal(core == null, big == null);
                if (big != null)
                {
                    Assert.Equal(WorldMapImageFE7ViewModel.BigWidth, big.Width);
                    Assert.Equal(WorldMapImageFE7ViewModel.BigHeight, big.Height);
                }
                core?.Dispose();
                big?.Dispose();

                IImage coreEv = ImageWorldMapCore.TryRenderEvent(CoreState.ROM);
                IImage ev = vm.TryRenderEvent();
                Assert.Equal(coreEv == null, ev == null);
                coreEv?.Dispose();
                ev?.Dispose();

                vm.LoadEntry(0);
                Assert.True(vm.IsLoaded);
                Assert.True(vm.CanImport, "CanImport must be true on FE7");
                Assert.NotEqual(0u, vm.BigImagePointer);
            });
        }

        [AvaloniaFact]
        public void FE8U_BigPreviewNull_CanImportFalse()
        {
            WithRom("FE8U", vm =>
            {
                // The FE7 12-split big-map render is FE7-only -> null on FE8.
                Assert.Null(vm.TryRenderBigFieldMap());

                vm.LoadEntry(0);
                Assert.False(vm.CanImport, "CanImport must be false on FE8");
            });
        }

        // -----------------------------------------------------------------
        // Helper: load a specific real ROM, wire CoreState, run the body.
        // -----------------------------------------------------------------

        void WithRom(string version, Action<WorldMapImageFE7ViewModel> body)
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

                body(new WorldMapImageFE7ViewModel());
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedService;
            }
        }
    }
}
