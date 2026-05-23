// SPDX-License-Identifier: GPL-3.0-or-later
// Headless tests for the new `GraphicsToolView.Jump(...)` API surface
// added in #434. Verifies that the WF `GraphicsToolForm.Jump` pixel→tile
// conversion is correct, that the addresses are formatted as hex, and
// that the imageType→IsCompressed flag toggles work.
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    public class GraphicsToolViewJumpTests
    {
        /// <summary>
        /// WF `GraphicsToolForm.Jump(width, height, ...)` uses pixels and
        /// converts to tiles via `PicWidth.Value = width / 8`. The Avalonia
        /// `Jump` must perform the same conversion before populating the
        /// VM's tile-count properties.
        /// </summary>
        [AvaloniaFact]
        public void Jump_ConvertsPixelsToTileCounts()
        {
            var view = new GraphicsToolView();
            view.Jump(
                width: 240, height: 160,
                image: 0x08400000u, imageType: 1,
                tsa: 0x08500000u, tsaType: 1,
                palette: 0x08600000u, paletteType: 1,
                paletteCount: 8,
                image2: 0);

            // Battle BG is 30 tiles wide × 20 tiles tall.
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.Equal(30, vm.TileCountX);
            Assert.Equal(20, vm.TileCountY);
        }

        /// <summary>
        /// WF semantics: `ImageOption.SelectedIndex == 0` means LZ77
        /// compressed (圧縮画像 — see `GraphicsToolForm.Draw()` line 309).
        /// Battle BG callsites pass imageType=0, so IsCompressed must be
        /// true. (Regression test for Copilot CLI review on PR #513 —
        /// the previous mapping was inverted and would have sent LZ77
        /// battle-BG graphics through `DrawTiles` as raw bytes.)
        /// </summary>
        [AvaloniaFact]
        public void Jump_ImageType0_SetsIsCompressed_BattleBgCallsite()
        {
            var view = new GraphicsToolView();
            // Exact Battle BG callsite from ImageBattleBGView.GraphicsTool_Click:
            //   .Jump(30*8, 20*8, image, 0, tsa, 1, palette, 1, 8, 0)
            view.Jump(width: 30 * 8, height: 20 * 8,
                image: 0x08400000u, imageType: 0,
                tsa: 0x08500000u, tsaType: 1,
                palette: 0x08600000u, paletteType: 1,
                paletteCount: 8, image2: 0);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.True(vm.IsCompressed,
                "imageType=0 means LZ77-compressed (WF GraphicsToolForm.Draw line 309)");
        }

        /// <summary>
        /// WF semantics: `ImageOption.SelectedIndex == 1` means raw
        /// uncompressed (無圧縮 — see `GraphicsToolForm.Image_ValueChanged`
        /// line 277). IsCompressed must be false.
        /// </summary>
        [AvaloniaFact]
        public void Jump_ImageType1_ClearsIsCompressed()
        {
            var view = new GraphicsToolView();
            view.Jump(240, 160, 0x08400000u, imageType: 1,
                0x08500000u, 1, 0x08600000u, 1, 8, 0);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.False(vm.IsCompressed);
        }

        /// <summary>
        /// WF semantics: indices 2 / 3 / 4 also mean LZ77-compressed
        /// (`GraphicsToolForm.Draw()` lines 309 + 322). Verify the
        /// helper covers each.
        /// </summary>
        [AvaloniaTheory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Jump_ImageType_2_3_4_AllCompressed(int imageType)
        {
            var view = new GraphicsToolView();
            view.Jump(240, 160, 0x08400000u, imageType,
                0x08500000u, 1, 0x08600000u, 1, 8, 0);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.True(vm.IsCompressed);
        }

        /// <summary>
        /// Addresses must be set as hex strings in the VM text properties.
        /// Battle BG hands the helper raw ROM offsets and GBA pointers
        /// interchangeably — both must be normalized to 0x08XXXXXX format.
        /// </summary>
        [AvaloniaFact]
        public void Jump_SetsImageAndPaletteAddresses_AsHexStrings()
        {
            var view = new GraphicsToolView();
            view.Jump(240, 160, 0x08400000u, 1,
                0x08500000u, 1, 0x08600000u, 1, 8, 0);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.Contains("0x08400000", vm.ImageAddressText, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("0x08600000", vm.PaletteAddressText, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Battle BG graphics are 4bpp — `Is4bpp` must be true regardless
        /// of caller. (The WF form sets the combo to 4bpp implicitly
        /// because the Battle BG callsite passes imageType=0 to
        /// the 4bpp option list.)
        /// </summary>
        [AvaloniaFact]
        public void Jump_SetsIs4bpp_True()
        {
            var view = new GraphicsToolView();
            view.Jump(240, 160, 0x08400000u, 0,
                0x08500000u, 1, 0x08600000u, 1, 8, 0);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.True(vm.Is4bpp);
        }
    }

    /// <summary>
    /// Smoke test for the `DecreaseColorTSAToolView.InitMethod(int)`
    /// surface — verifies the mode flows through to the VM.
    /// </summary>
    public class DecreaseColorTSAToolViewInitMethodTests
    {
        [AvaloniaFact]
        public void InitMethod_PersistsModeToViewModel()
        {
            var view = new DecreaseColorTSAToolView();
            view.InitMethod(2);
            // Reflect into the private VM via the public IsLoaded check —
            // the Method property must be set.
            var vmField = typeof(DecreaseColorTSAToolView).GetField(
                "_vm",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(vmField);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.DecreaseColorTSAToolViewModel)vmField!.GetValue(view)!;
            Assert.Equal(2, vm.Method);
        }
    }
}
