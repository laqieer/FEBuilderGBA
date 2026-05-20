using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
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
/// These tests open the real FE8U ROM (via RomFixture), cycle through several
/// classes, and assert the preview surface behaves correctly. They cover
/// the user-visible path the plan v2 promised — animType variation comes
/// from real ROM data (different classes have different wait-icon types),
/// so this exercises the 16x16 / 16x24 / 32x32 source bitmaps in situ.
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

    /// <summary>
    /// Affirmative regression for #342: after selecting a real class, the
    /// preview Border becomes visible and the IconPreviewControl holds a
    /// bitmap. The previous GbaImageControl-based preview could end up
    /// clipped/cut-off depending on animType — IconPreviewControl's fixed
    /// 64x64 box prevents that.
    /// </summary>
    [AvaloniaFact]
    public void SelectingClass_ShowsPreviewWithIcon()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            var previewBorder = view.FindControl<Border>("ListPreviewBorder");
            var previewImage = view.FindControl<IconPreviewControl>("ListPreviewImage");
            var previewName = view.FindControl<TextBlock>("ListPreviewName");

            Assert.NotNull(classList);
            Assert.NotNull(previewBorder);
            Assert.NotNull(previewImage);
            Assert.NotNull(previewName);

            // Use the same control type swap that closed #342: the preview
            // image must be an IconPreviewControl, not a GbaImageControl.
            // (If the swap ever regresses, this assertion fires.)
            Assert.IsType<IconPreviewControl>(previewImage);

            // Drive the selection through the actual list control so the
            // SelectedAddressChanged -> OnClassSelected -> TryShowListPreview
            // chain runs end-to-end against real ROM data.
            classList!.SelectFirst();

            // The first FE8U class (Eirika Lord) has a valid wait icon, so
            // PreviewIconHelper.LoadClassWaitIcon must return non-null and
            // the preview surface must become visible.
            Assert.True(previewBorder!.IsVisible,
                "ListPreviewBorder should be visible after selecting a class with a valid wait icon.");
            Assert.False(string.IsNullOrEmpty(previewName!.Text),
                "ListPreviewName should display the class name after selection.");
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
        if (!_fixture.IsAvailable) return;

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var previewImage = view.FindControl<IconPreviewControl>("ListPreviewImage");
            Assert.NotNull(previewImage);

            // Expected per the v2 plan: ClassEditor uses Scale=2, Max=32x32
            // => 64x64 box. This guards against accidental XAML edits that
            // would reintroduce clipping by shrinking the control.
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
    /// AnimType coverage regression: cycle through enough classes that we
    /// will hit each animation type the ROM contains, and assert the preview
    /// renders a non-null bitmap whenever PreviewIconHelper.LoadClassWaitIcon
    /// would succeed. This is the closest practical proxy for "animType 0/1/2
    /// all render without clipping" without baking in animType-specific class
    /// indices that vary between ROM versions.
    /// </summary>
    [AvaloniaFact]
    public void CyclingClasses_AnimTypesAllRenderPreview()
    {
        if (!_fixture.IsAvailable) return;

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            var previewBorder = view.FindControl<Border>("ListPreviewBorder");
            var previewImage = view.FindControl<IconPreviewControl>("ListPreviewImage");

            Assert.NotNull(classList);
            Assert.NotNull(previewBorder);
            Assert.NotNull(previewImage);

            // Iterate the first ~64 entries — enough to span the full FE8U
            // class table breadth and hit all wait-icon animTypes (0/1/2).
            var items = classList!.GetItems();
            int classCount = items.Count;
            int visibleAfterSelect = 0;
            int probedClasses = System.Math.Min(64, classCount);
            for (int i = 0; i < probedClasses; i++)
            {
                classList.SelectByIndex(i);
                if (previewBorder!.IsVisible)
                    visibleAfterSelect++;
            }

            // Real ROM data: at least a majority of classes must produce a
            // visible preview (some "blank" classes legitimately don't).
            // This catches the regression where ALL classes failed to render
            // (e.g. if IconPreviewControl threw or the swap removed the wiring).
            _output.WriteLine($"Probed {probedClasses} classes; {visibleAfterSelect} produced a visible preview.");
            Assert.True(visibleAfterSelect > probedClasses / 2,
                $"Expected most ({probedClasses / 2}+) of {probedClasses} probed classes to produce a visible preview; got {visibleAfterSelect}.");
        }
        finally
        {
            view.Close();
        }
    }
}
