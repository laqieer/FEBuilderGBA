// SPDX-License-Identifier: GPL-3.0-or-later
// VM tests for the In-ROM Magic Animation editor (#1176):
//   * no-ROM guard: LoadList empty / TryLoadImage null / not loaded,
//   * with a real ROM: LoadList non-empty (from the romanime_ config), LoadEntry
//     loads a frame count + a non-null preview for the first renderable entry.
// Skips gracefully when no ROM is available (TestRomLocator.FindRom == null).
using System;
using System.Collections.Generic;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImageRomAnimeViewModelTests
    {
        readonly ITestOutputHelper _output;

        public ImageRomAnimeViewModelTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void NoRom_ListEmpty_PreviewNull_NotLoaded()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ImageRomAnimeViewModel();

                Assert.Empty(vm.LoadList());
                Assert.Null(vm.TryLoadImage());

                vm.LoadEntry(0);
                Assert.False(vm.IsLoaded);
                Assert.Equal(0, vm.FrameCount);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [AvaloniaFact]
        public void FE8U_LoadList_NonEmpty_FirstEntry_RendersFrame()
        {
            WithRom("FE8U", vm =>
            {
                List<AddrResult> list = vm.LoadList();
                Assert.NotEmpty(list);

                // Find the first entry that resolves to a renderable frame (some
                // config rows may point at not-yet-present data on a clean ROM).
                bool renderedAny = false;
                foreach (AddrResult item in list)
                {
                    vm.LoadEntry(item.addr);
                    if (!vm.IsLoaded) continue;
                    Assert.True(vm.FrameCount >= 1);

                    using IImage img = vm.TryLoadImage();
                    if (img == null) continue;
                    Assert.Equal(vm.FrameWidthPx, img.Width);
                    Assert.True(img.Height > 0 && img.Height <= 8 * 16);
                    renderedAny = true;
                    break;
                }
                Assert.True(renderedAny, "At least one romanime entry should render a non-null frame on FE8U.");
            });
        }

        [AvaloniaFact]
        public void FE8U_GetListCount_MatchesLoadList()
        {
            WithRom("FE8U", vm =>
            {
                Assert.Equal(vm.LoadList().Count, vm.GetListCount());
            });
        }

        // -----------------------------------------------------------------
        // Helper: load a specific real ROM, wire CoreState, run the body.
        // -----------------------------------------------------------------

        void WithRom(string version, Action<ImageRomAnimeViewModel> body)
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

                body(new ImageRomAnimeViewModel());
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.ImageService = savedService;
            }
        }
    }
}
