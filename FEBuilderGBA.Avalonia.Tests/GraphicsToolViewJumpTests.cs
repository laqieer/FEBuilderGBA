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
        /// imageType==1 means LZ77-compressed in the WF semantics. The
        /// Avalonia VM's IsCompressed must flip accordingly.
        /// </summary>
        [AvaloniaFact]
        public void Jump_ImageType1_SetsIsCompressed()
        {
            var view = new GraphicsToolView();
            view.Jump(240, 160, 0x08400000u, imageType: 1,
                0x08500000u, 1, 0x08600000u, 1, 8, 0);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.True(vm.IsCompressed);
        }

        /// <summary>
        /// imageType==0 means raw (uncompressed) in WF — IsCompressed
        /// must be false.
        /// </summary>
        [AvaloniaFact]
        public void Jump_ImageType0_ClearsIsCompressed()
        {
            var view = new GraphicsToolView();
            view.Jump(240, 160, 0x08400000u, imageType: 0,
                0x08500000u, 1, 0x08600000u, 1, 8, 0);
            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.False(vm.IsCompressed);
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
