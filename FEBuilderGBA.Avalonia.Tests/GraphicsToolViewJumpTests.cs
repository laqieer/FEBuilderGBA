// SPDX-License-Identifier: GPL-3.0-or-later
// Headless tests for the new `GraphicsToolView.Jump(...)` API surface
// added in #434. Verifies that the WF `GraphicsToolForm.Jump` pixel→tile
// conversion is correct, that the addresses are formatted as hex, and
// that the imageType→IsCompressed flag toggles work.
//
// Also contains regression tests for the TSA Editor button enable-after-Jump
// pattern (#860). See GraphicsToolTsaButtonTests below.
using Avalonia.Controls;
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
    /// Regression tests for the TSA Editor button enable-after-Jump pattern.
    /// Issue #860: verifies that TSAEditorButton starts disabled (no TSA context)
    /// and is enabled after Jump() loads a valid image/TSA/palette context.
    ///
    /// The enable-on-Jump pattern is intentional and CORRECT:
    /// - axaml: <c>IsEnabled="False"</c> — correct initial state (no context yet)
    /// - Jump(): sets <c>TSAEditorButton.IsEnabled = _tsaNavReady</c> (true) once context is loaded
    /// - TSAEditor_Click(): early-returns if <c>!_tsaNavReady</c> (safety guard)
    /// - Standalone menu path (OpenGraphicsTool_Click): leaves button disabled — also correct
    ///
    /// These tests lock that behavior so a future edit that removes the enable
    /// path, or flips the axaml default to True, will be caught by CI.
    /// </summary>
    public class GraphicsToolTsaButtonTests
    {
        /// <summary>
        /// Before Jump() is called, TSAEditorButton must be disabled (no TSA context).
        /// This matches the axaml <c>IsEnabled="False"</c> initial state and the
        /// standalone menu path (OpenGraphicsTool_Click) which opens the view
        /// without a TSA context.
        ///
        /// Regression anchor for #860: if someone flips the axaml default to
        /// <c>IsEnabled="True"</c> this test will catch it.
        /// </summary>
        [AvaloniaFact]
        public void TSAEditorButton_IsDisabled_Initially_BeforeJump()
        {
            var view = new GraphicsToolView();
            var button = view.FindControl<Button>("TSAEditorButton");

            Assert.NotNull(button);
            Assert.False(button!.IsEnabled,
                "TSAEditorButton must be disabled on construction (no TSA context yet). " +
                "axaml IsEnabled=\"False\" is intentional — do NOT change to True. (#860)");
        }

        /// <summary>
        /// After Jump() is called with a valid image/TSA/palette context,
        /// TSAEditorButton must be enabled. This mirrors the real navigation path:
        /// ImageBattleBGView and ImageBGView call Open&lt;GraphicsToolView&gt;() then Jump().
        ///
        /// Regression anchor for #860: if someone removes the
        /// <c>TSAEditorButton.IsEnabled = _tsaNavReady</c> assignment from Jump(),
        /// this test will catch it.
        /// </summary>
        [AvaloniaFact]
        public void TSAEditorButton_IsEnabled_AfterJump_WithValidContext()
        {
            var view = new GraphicsToolView();
            var button = view.FindControl<Button>("TSAEditorButton");
            Assert.NotNull(button);

            // Simulate the real ImageBattleBGView callsite:
            //   .Jump(30*8, 20*8, image, 0, tsa, 1, palette, 1, 8, 0)
            view.Jump(
                width: 240, height: 160,
                image: 0x08400000u, imageType: 0,
                tsa: 0x08500000u, tsaType: 1,
                palette: 0x08600000u, paletteType: 1,
                paletteCount: 8,
                image2: 0);

            Assert.True(button!.IsEnabled,
                "TSAEditorButton must be enabled after Jump() loads a valid image/TSA/palette context. " +
                "The enable assignment 'TSAEditorButton.IsEnabled = _tsaNavReady' in Jump() is intentional. (#860)");
        }

        /// <summary>
        /// Parity assertion: the code-behind source must contain both the
        /// <c>IsEnabled="False"</c> axaml default AND the
        /// <c>TSAEditorButton.IsEnabled = _tsaNavReady</c> enable assignment,
        /// verifying neither is accidentally deleted.
        ///
        /// This is a secondary safety net — the behavioral tests above are the
        /// primary regression anchors. This test catches edits that delete the
        /// enable path without breaking the behavioral tests (which would only
        /// fail if the axaml also changed).
        /// </summary>
        [Fact]
        public void GraphicsToolView_SourceHasBothDisabledDefaultAndEnableAssignment()
        {
            // Locate the Views source file via the assembly location.
            // In CI the .cs source may not be co-located with the DLL —
            // use a source-path relative to this test assembly's location
            // (both live under the solution root in the same git checkout).
            var asm = typeof(GraphicsToolView).Assembly;
            var asmDir = System.IO.Path.GetDirectoryName(asm.Location) ?? string.Empty;

            // Walk up from the assembly output dir (obj/Debug/net10.0) to the
            // solution root, then down to the Views file.
            var dir = new System.IO.DirectoryInfo(asmDir);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                dir = dir.Parent;

            if (dir == null)
            {
                // Source tree not available (e.g. published artifact without sources).
                // Skip gracefully — the behavioral tests above are the primary guards.
                return;
            }

            var csPath = System.IO.Path.Combine(
                dir.FullName, "FEBuilderGBA.Avalonia", "Views", "GraphicsToolView.axaml.cs");
            var axamlPath = System.IO.Path.Combine(
                dir.FullName, "FEBuilderGBA.Avalonia", "Views", "GraphicsToolView.axaml");

            if (!System.IO.File.Exists(csPath) || !System.IO.File.Exists(axamlPath))
                return; // Source not present — skip.

            var csSource = System.IO.File.ReadAllText(csPath);
            var axamlSource = System.IO.File.ReadAllText(axamlPath);

            // 1. The code-behind must contain the enable assignment.
            Assert.Contains(
                "TSAEditorButton.IsEnabled = _tsaNavReady",
                csSource,
                System.StringComparison.Ordinal);

            // 2. The TSAEditorButton element must carry IsEnabled="False".
            //    Use a scoped regex so attribute order / whitespace / x:Name vs Name
            //    differences don't matter, and we get a POSITIVE assertion rather
            //    than a brittle exact-string absence check.
            //
            //    Pattern: match the opening <Button ... > (or self-closing />) tag
            //    that contains either  x:Name="TSAEditorButton"  or  Name="TSAEditorButton",
            //    then assert that same tag also contains  IsEnabled\s*=\s*"False".
            var buttonTagMatch = System.Text.RegularExpressions.Regex.Match(
                axamlSource,
                @"<Button\b[^/\>]*?(?:x:)?Name\s*=\s*""TSAEditorButton""[^/\>]*?/?>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            Assert.True(
                buttonTagMatch.Success,
                "GraphicsToolView.axaml must contain a <Button> element with " +
                "Name=\"TSAEditorButton\" (or x:Name=\"TSAEditorButton\"). " +
                "Element not found — was it renamed or removed? (#860/#861)");

            var buttonTag = buttonTagMatch.Value;

            Assert.True(
                System.Text.RegularExpressions.Regex.IsMatch(
                    buttonTag,
                    @"IsEnabled\s*=\s*""False"""),
                $"TSAEditorButton element must declare IsEnabled=\"False\" as its axaml default. " +
                $"Found tag: [{buttonTag}]. " +
                $"Do NOT change to True — Jump() enables it only after a valid context is loaded. (#860/#861)");
        }
    }

    /// <summary>
    /// Parity tests for the Graphics Tool TSA-composited preview (#1030).
    /// Verifies the new TSA Address input + TSA Type combo controls exist, that
    /// Is4bppCheck/CompressedCheck enablement is bound to the TSA-mode gate, that
    /// the VM DrawTiles references ImageTSAEditorCore.TryRenderMainImage, and
    /// that Jump sets the TSA fields.
    /// </summary>
    public class GraphicsToolTsaPreviewParityTests
    {
        /// <summary>
        /// The new TSA Address TextBox + TSA Type ComboBox controls must exist
        /// in the view, and the combo must be populated with the 5 TSA-type
        /// items (None / Compressed / Compressed Header / Raw Header / Raw).
        /// </summary>
        [AvaloniaFact]
        public void TsaAddrAndTypeControls_Exist()
        {
            var view = new GraphicsToolView();
            var tsaAddr = view.FindControl<TextBox>("TsaAddrBox");
            var tsaCombo = view.FindControl<ComboBox>("TsaTypeCombo");

            Assert.NotNull(tsaAddr);
            Assert.NotNull(tsaCombo);
            Assert.Equal(5, tsaCombo!.Items.Count);
        }

        /// <summary>
        /// Jump must populate the VM's TSA fields so existing entrypoints
        /// (ImageBattleBGView passes tsaType=1, ImageBGView passes tsaType=3)
        /// open the Graphics Tool in TSA-composited mode.
        /// </summary>
        [AvaloniaFact]
        public void Jump_SetsTsaFields()
        {
            var view = new GraphicsToolView();
            view.Jump(
                width: 240, height: 160,
                image: 0x08400000u, imageType: 0,
                tsa: 0x08500000u, tsaType: 1,
                palette: 0x08600000u, paletteType: 1,
                paletteCount: 8, image2: 0);

            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.Contains("0x08500000", vm.TsaAddressText, System.StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, vm.TsaTypeIndex);
            Assert.True(vm.TsaModeActive);
        }

        /// <summary>
        /// A null (0) TSA pointer must be preserved as an empty TSA address (NOT
        /// normalized to 0x08000000) — same null-preserving rule the image /
        /// palette population uses.
        /// </summary>
        [AvaloniaFact]
        public void Jump_ZeroTsa_LeavesTsaAddressEmpty()
        {
            var view = new GraphicsToolView();
            view.Jump(
                width: 240, height: 160,
                image: 0x08400000u, imageType: 0,
                tsa: 0u, tsaType: 0,
                palette: 0x08600000u, paletteType: 0,
                paletteCount: 8, image2: 0);

            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.Equal("", vm.TsaAddressText);
            Assert.Equal(0, vm.TsaTypeIndex);
            Assert.False(vm.TsaModeActive);
        }

        /// <summary>
        /// #1074: Jump must set the image2-join + compressed-palette context.
        /// <c>imageType == 2</c> -> <c>IsImage2Join</c> (NOT merely image2 != 0,
        /// refinement #5); <c>paletteType == 1</c> -> <c>IsCompressedPalette</c>;
        /// a nonzero <c>image2</c> -> <c>Image2AddressText</c> (hex), a 0 image2
        /// -> "".
        /// </summary>
        [AvaloniaFact]
        public void Jump_SetsImage2JoinAndCompressedPalette()
        {
            var view = new GraphicsToolView();
            view.Jump(
                width: 240, height: 160,
                image: 0x08400000u, imageType: 2,   // join mode
                tsa: 0x08500000u, tsaType: 1,
                palette: 0x08600000u, paletteType: 1, // compressed palette
                paletteCount: 8,
                image2: 0x08700000u);

            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.True(vm.IsImage2Join);
            Assert.True(vm.IsCompressedPalette);
            Assert.Contains("0x08700000", vm.Image2AddressText, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// #1074 refinement #5: IsImage2Join is gated on imageType == 2, NOT on
        /// image2 != 0. A non-join imageType that still carries a 2nd address must
        /// NOT set the join flag. paletteType != 1 leaves IsCompressedPalette
        /// false, and a 0 image2 leaves Image2AddressText empty.
        /// </summary>
        [AvaloniaFact]
        public void Jump_NonJoinImageType_DoesNotSetImage2Join_AndZeroImage2IsEmpty()
        {
            var view = new GraphicsToolView();
            view.Jump(
                width: 240, height: 160,
                image: 0x08400000u, imageType: 0,    // NOT join mode
                tsa: 0x08500000u, tsaType: 1,
                palette: 0x08600000u, paletteType: 0, // raw palette
                paletteCount: 8,
                image2: 0u);                          // null 2nd image

            var vm = (FEBuilderGBA.Avalonia.ViewModels.GraphicsToolViewViewModel)view.DataContext!;
            Assert.False(vm.IsImage2Join);
            Assert.False(vm.IsCompressedPalette);
            Assert.Equal("", vm.Image2AddressText);
        }

        /// <summary>
        /// Source parity: the AXAML binds Is4bppCheck + CompressedCheck
        /// IsEnabled to the inverse of TsaModeActive (so the TSA path is visibly
        /// fixed 4bpp + LZ77 image), and the VM DrawTiles references
        /// ImageTSAEditorCore.TryRenderMainImage.
        /// </summary>
        [Fact]
        public void Source_BindsCheckboxEnablement_And_VmUsesTryRenderMainImage()
        {
            var asm = typeof(GraphicsToolView).Assembly;
            var asmDir = System.IO.Path.GetDirectoryName(asm.Location) ?? string.Empty;
            var dir = new System.IO.DirectoryInfo(asmDir);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                dir = dir.Parent;
            if (dir == null) return; // source tree not available — skip

            var axamlPath = System.IO.Path.Combine(
                dir.FullName, "FEBuilderGBA.Avalonia", "Views", "GraphicsToolView.axaml");
            var vmPath = System.IO.Path.Combine(
                dir.FullName, "FEBuilderGBA.Avalonia", "ViewModels", "GraphicsToolViewViewModel.cs");
            if (!System.IO.File.Exists(axamlPath) || !System.IO.File.Exists(vmPath))
                return;

            var axaml = System.IO.File.ReadAllText(axamlPath);
            var vm = System.IO.File.ReadAllText(vmPath);

            // Both checkboxes gate enablement on !TsaModeActive.
            Assert.Matches(
                @"x:Name=""Is4bppCheck""[^>]*IsEnabled=""\{Binding !TsaModeActive\}""",
                axaml);
            Assert.Matches(
                @"x:Name=""CompressedCheck""[^>]*IsEnabled=""\{Binding !TsaModeActive\}""",
                axaml);

            // The TSA-composited preview routes through the existing render core.
            Assert.Contains("ImageTSAEditorCore.TryRenderMainImage", vm, System.StringComparison.Ordinal);
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
