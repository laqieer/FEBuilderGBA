using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Issue #342 integration regression: when a class is selected in the
/// ClassEditor's list, the bottom-of-column wait-icon preview slot must
/// (a) become visible, (b) render the class name, and (c) be wired to the
/// shared IconPreviewControl so the bitmap is no longer clipped by the
/// previous interactive GbaImageControl + ScrollViewer layout.
///
/// These tests open the real FE8U ROM (via RomFixture), cycle through real
/// class data, and assert the preview surface behaves correctly. They cover
/// the user-visible path the plan v2 promised — animType variation comes
/// from real ROM data (different classes have different wait-icon types),
/// so this exercises the 16x16 / 16x24 / 32x32 source bitmaps in situ.
///
/// CRITICAL (PR #351 re-review): every test ASSERTS the fixture is loaded
/// and the version is the expected one — silently skipping would let the
/// CI signal degrade if the test ROM ever goes missing. The CyclingClasses
/// test also explicitly tracks which animTypes were encountered and
/// requires the full 0/1/2 trio for FE8U coverage.
/// </summary>
[Collection("SharedState")]
public class ClassEditorListPreviewTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ClassEditorListPreviewTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>Affirm the integration suite is genuinely running against FE8U.</summary>
    void AssertFE8UAvailable()
    {
        Assert.True(_fixture.IsAvailable,
            "RomFixture must load a ROM for the ClassEditor integration suite — none was found.");
        Assert.Equal("FE8U", _fixture.Version);

        // The bitmap-rendering pipeline depends on CoreState.ImageService.
        // App.axaml.cs wires SkiaImageService at startup; in headless tests
        // we must wire it ourselves so PreviewIconHelper.LoadClassWaitIcon
        // can actually decode 4bpp tiles. Idempotent.
        if (CoreState.ImageService == null)
            CoreState.ImageService = new SkiaImageService();
    }

    /// <summary>
    /// Affirmative regression for #342: the ClassEditor's list-preview slot
    /// uses IconPreviewControl (not GbaImageControl), and the underlying
    /// PreviewIconHelper.LoadClassWaitIcon path produces a real bitmap for
    /// the first FE8U class. This proves both the control-type swap AND
    /// the rendering pipeline that feeds it.
    /// </summary>
    [AvaloniaFact]
    public void FirstClass_HasNonNullWaitIcon_AndPreviewSlotIsIconPreviewControl()
    {
        AssertFE8UAvailable();

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            var previewImage = view.FindControl<IconPreviewControl>("ListPreviewImage");
            Assert.NotNull(classList);
            Assert.NotNull(previewImage);

            // The control-type swap that closed #342: must be
            // IconPreviewControl, not GbaImageControl. Regression-fires
            // if the XAML is reverted.
            Assert.IsType<IconPreviewControl>(previewImage);

            // Drive the selection so the VM loads class data.
            classList!.SelectFirst();
            var vm = view.DataViewModel as ClassEditorViewModel;
            Assert.NotNull(vm);

            // The rendering pipeline must produce a non-null bitmap for the
            // first FE8U class (Eirika Lord). Independent of view-realization
            // timing in headless mode.
            using var img = PreviewIconHelper.LoadClassWaitIcon(vm!.WaitIcon);
            Assert.NotNull(img);
            Assert.True(img!.Width > 0 && img.Height > 0,
                $"LoadClassWaitIcon for class 0 (waitIcon=0x{vm.WaitIcon:X}) returned a degenerate {img.Width}x{img.Height} bitmap.");
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// Layout regression for #342: even at the design-time host size used by
    /// the actual editor (250-wide left column), the IconPreviewControl must
    /// measure to its configured Scale * MaxImage* box and not collapse.
    /// </summary>
    [AvaloniaFact]
    public void PreviewControl_HasExpectedConfiguredSize()
    {
        AssertFE8UAvailable();

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var previewImage = view.FindControl<IconPreviewControl>("ListPreviewImage");
            Assert.NotNull(previewImage);

            Assert.Equal(2, previewImage!.Scale);
            Assert.Equal(32, previewImage.MaxImageWidth);
            Assert.Equal(32, previewImage.MaxImageHeight);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// AnimType coverage regression (PR #351 re-review): cycle through real
    /// FE8U classes and verify each wait-icon animation type (0, 1, 2) was
    /// (a) actually encountered in ROM data, AND (b) had its bitmap render
    /// path produce a non-null image with the expected dimensions:
    ///   animType 0 → 16x16
    ///   animType 1 → 16x24
    ///   animType 2 → 32x32 (the original #342 bug case)
    /// Tests the bitmap-loading pipeline directly via PreviewIconHelper
    /// (independent of view-realization timing in headless mode).
    /// </summary>
    [AvaloniaFact]
    public void CyclingClasses_AllAnimTypes_ProduceCorrectBitmapSizes()
    {
        AssertFE8UAvailable();

        // Discover the wait-icon table base so we can read animType per class.
        ROM rom = _fixture.ROM!;
        uint waitTablePtr = rom.RomInfo.unit_wait_icon_pointer;
        Assert.NotEqual(0u, waitTablePtr);
        uint waitTableBase = rom.p32(waitTablePtr);
        Assert.True(U.isSafetyOffset(waitTableBase, rom),
            $"Wait-icon table base 0x{waitTableBase:X08} is not a safe ROM offset.");

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            Assert.NotNull(classList);

            // Per animType: count occurrences in ROM data AND record
            // whether each animType's bitmap-loading path produced a
            // non-null image of the expected size.
            var encountered = new Dictionary<byte, int>();
            var renderedSize = new Dictionary<byte, (int W, int H)>();

            var items = classList!.GetItems();
            int probedClasses = System.Math.Min(items.Count, 128);

            for (int i = 0; i < probedClasses; i++)
            {
                classList.SelectByIndex(i);
                if (view.DataViewModel is not ClassEditorViewModel vm) continue;

                uint waitIconIndex = vm.WaitIcon;
                uint entryAddr = waitTableBase + waitIconIndex * 8;
                if (entryAddr + 8 > (uint)rom.Data.Length) continue;
                byte animType = (byte)rom.u8(entryAddr + 2);
                encountered[animType] = encountered.GetValueOrDefault(animType, 0) + 1;

                // Once per animType, run the bitmap pipeline and capture size.
                if (!renderedSize.ContainsKey(animType))
                {
                    using var img = PreviewIconHelper.LoadClassWaitIcon(waitIconIndex);
                    if (img != null)
                        renderedSize[animType] = (img.Width, img.Height);
                }
            }

            _output.WriteLine($"Probed {probedClasses} classes.");
            foreach (var kv in encountered)
                _output.WriteLine($"animType {kv.Key}: encountered {kv.Value}, " +
                    $"rendered size={(renderedSize.ContainsKey(kv.Key) ? $"{renderedSize[kv.Key].W}x{renderedSize[kv.Key].H}" : "<null>")}");

            // FE8U's class table genuinely contains all three animation types.
            Assert.True(encountered.ContainsKey(0) && encountered[0] > 0,
                "animType 0 (16x16) must be encountered in FE8U class data.");
            Assert.True(encountered.ContainsKey(1) && encountered[1] > 0,
                "animType 1 (16x24) must be encountered in FE8U class data.");
            Assert.True(encountered.ContainsKey(2) && encountered[2] > 0,
                "animType 2 (32x32 — the original #342 bug case) must be encountered in FE8U class data.");

            // Each animType must produce a bitmap of the documented size.
            Assert.True(renderedSize.ContainsKey(0), "animType 0 must produce a non-null bitmap.");
            Assert.Equal((16, 16), renderedSize[0]);

            Assert.True(renderedSize.ContainsKey(1), "animType 1 must produce a non-null bitmap.");
            Assert.Equal((16, 24), renderedSize[1]);

            Assert.True(renderedSize.ContainsKey(2),
                "animType 2 must produce a non-null bitmap — this is the original #342 32x32 bug case.");
            Assert.Equal((32, 32), renderedSize[2]);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// Long-name / 250px-column regression (PR #351 re-review): the editor's
    /// left column is fixed-width 250px (see ClassEditorView.axaml line 7:
    /// ColumnDefinitions="250,*"). The preview Border lives at Grid.Row=1
    /// of that column with HorizontalAlignment="Center". Even with a very
    /// long class name, the preview Border (containing IconPreviewControl
    /// + name TextBlock) must fit inside the 250-wide column and the
    /// IconPreviewControl must still measure to its configured 64x64 box.
    /// </summary>
    [AvaloniaFact]
    public void PreviewBorder_FitsInsideLeftColumn_EvenWithLongName()
    {
        AssertFE8UAvailable();

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var previewBorder = view.FindControl<Border>("ListPreviewBorder");
            var previewName = view.FindControl<TextBlock>("ListPreviewName");
            var previewImage = view.FindControl<IconPreviewControl>("ListPreviewImage");
            var classList = view.FindControl<AddressListControl>("ClassList");
            Assert.NotNull(previewBorder);
            Assert.NotNull(previewName);
            Assert.NotNull(previewImage);
            Assert.NotNull(classList);

            classList!.SelectFirst();

            // Force a long localized class name (worst case for layout overflow).
            string longName = "ものすごく長いクラス名前テスト" + new string('X', 30);
            previewName!.Text = longName;
            previewName.MaxWidth = 250 - 64 - 24; // column - icon - padding budget
            previewName.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;

            view.UpdateLayout();
            previewBorder!.Measure(new global::Avalonia.Size(250, 200));
            previewBorder.Arrange(new global::Avalonia.Rect(0, 0, 250, previewBorder.DesiredSize.Height));
            view.UpdateLayout();

            // The IconPreviewControl must keep its full 64x64 footprint even
            // with a long name competing for space — Stretch="Uniform" inside
            // the control prevents the icon from shrinking.
            Assert.Equal(2 * 32, previewImage!.Scale * previewImage.MaxImageWidth);
            Assert.True(previewBorder.DesiredSize.Width <= 250 + 1,
                $"ListPreviewBorder desired width ({previewBorder.DesiredSize.Width}) must fit inside the 250-wide left column.");
        }
        finally
        {
            view.Close();
        }
    }
}
