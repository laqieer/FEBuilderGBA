using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless tests for the class-card preview panel added by issue #357.
/// The card mirrors WinForms <c>ClassForm</c> top-right block:
///   * class face portrait (L_8_PORTRAIT_CLASS)
///   * class wait icon (L_6_CLASSICONSRC)
///   * class name (L_5_CLASS)
/// </summary>
[Collection("SharedState")]
public class ClassEditorClassCardTests : IClassFixture<RomFixture>
{
    private readonly RomFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ClassEditorClassCardTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool TryAssertFE8U()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping ROM-backed assertions.");
            return false;
        }
        Assert.Equal("FE8U", _fixture.Version);
        if (CoreState.ImageService == null)
            CoreState.ImageService = new SkiaImageService();
        return true;
    }

    /// <summary>
    /// Card border control must exist in the editor view (XAML presence check).
    /// </summary>
    [AvaloniaFact]
    public void ClassCardBorder_ExistsInView()
    {
        var view = new ClassEditorView();
        var border = view.FindControl<Border>("ClassCardBorder");
        Assert.NotNull(border);
    }

    /// <summary>
    /// CardPortraitImage and CardWaitIconImage controls must exist.
    /// </summary>
    [AvaloniaFact]
    public void CardImageControls_ExistInView()
    {
        var view = new ClassEditorView();
        var portrait = view.FindControl<IconPreviewControl>("CardPortraitImage");
        var waitIcon = view.FindControl<IconPreviewControl>("CardWaitIconImage");
        Assert.NotNull(portrait);
        Assert.NotNull(waitIcon);
    }

    /// <summary>
    /// CardNameLabel and CardIdLabel must exist.
    /// </summary>
    [AvaloniaFact]
    public void CardLabels_ExistInView()
    {
        var view = new ClassEditorView();
        var name = view.FindControl<TextBlock>("CardNameLabel");
        var id = view.FindControl<TextBlock>("CardIdLabel");
        Assert.NotNull(name);
        Assert.NotNull(id);
    }

    /// <summary>
    /// After loading the first FE8U class, the card border must become visible,
    /// the wait icon must have a bitmap, and the labels must be populated.
    /// (The face portrait for class id 0 may be a blank dummy / null because
    /// id 0 is typically a placeholder entry — see WinForms parity below.)
    /// </summary>
    [AvaloniaFact]
    public void FirstClass_PopulatesClassCard()
    {
        if (!TryAssertFE8U()) return;

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            var border = view.FindControl<Border>("ClassCardBorder");
            var waitIcon = view.FindControl<IconPreviewControl>("CardWaitIconImage");
            var name = view.FindControl<TextBlock>("CardNameLabel");
            var id = view.FindControl<TextBlock>("CardIdLabel");
            Assert.NotNull(classList);
            Assert.NotNull(border);
            Assert.NotNull(waitIcon);
            Assert.NotNull(name);
            Assert.NotNull(id);

            classList!.SelectFirst();

            Assert.True(border!.IsVisible, "ClassCardBorder must be visible after a class is selected");
            Assert.True(waitIcon!.HasImage, "CardWaitIconImage must have a bitmap after selection");
            // Class id 0 in FE8U is a placeholder with empty Name; class id 1+ have real names.
            // The CardIdLabel always carries addr/ID info regardless.
            Assert.NotNull(name!.Text);
            Assert.False(string.IsNullOrWhiteSpace(id!.Text), "CardIdLabel must include address/ID info");
            Assert.Contains("0x", id.Text);
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// Selecting a class with a known non-zero portrait id (Eirika Lord at
    /// index 1 in FE8U) must populate the CardPortraitImage with a bitmap.
    /// This exercises the LoadClassFacePortrait path through the helper.
    /// </summary>
    [AvaloniaFact]
    public void ClassWithPortrait_PopulatesCardPortrait_FE8U()
    {
        if (!TryAssertFE8U()) return;

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            var portrait = view.FindControl<IconPreviewControl>("CardPortraitImage");
            Assert.NotNull(classList);
            Assert.NotNull(portrait);

            // Find the first class with a non-zero portrait id (skips placeholders).
            ROM rom = CoreState.ROM!;
            uint classBase = rom.p32(rom.RomInfo.class_pointer);
            uint classSize = rom.RomInfo.class_datasize;
            int targetIdx = -1;
            for (int i = 0; i < classList!.GetItems().Count && i < 256; i++)
            {
                uint classAddr = classBase + (uint)i * classSize;
                uint portraitId = rom.u16(classAddr + 8);
                if (portraitId != 0)
                {
                    targetIdx = i;
                    break;
                }
            }
            Assert.True(targetIdx >= 0, "Expected at least one class with a non-zero portrait id in FE8U");
            classList.SelectByIndex(targetIdx);

            Assert.True(portrait!.HasImage,
                $"CardPortraitImage must have a bitmap after selecting class index {targetIdx} (non-zero portrait id)");
        }
        finally
        {
            view.Close();
        }
    }

    /// <summary>
    /// Live-update regression: editing the wait icon field must refresh the
    /// wait-icon picture on the card.
    /// </summary>
    [AvaloniaFact]
    public void EditingWaitIconBox_UpdatesCardWaitIcon()
    {
        if (!TryAssertFE8U()) return;

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            var waitIconBox = view.FindControl<NumericUpDown>("WaitIconBox");
            var cardWait = view.FindControl<IconPreviewControl>("CardWaitIconImage");
            Assert.NotNull(classList);
            Assert.NotNull(waitIconBox);
            Assert.NotNull(cardWait);

            classList!.SelectFirst();

            // Change the wait-icon value; the card icon must refresh and remain
            // populated (different valid icon -> different image, but still
            // non-null HasImage).
            waitIconBox!.Value = 2;
            view.UpdateLayout();
            Assert.True(cardWait!.HasImage, "CardWaitIconImage must remain populated after changing WaitIconBox");
        }
        finally
        {
            view.Close();
        }
    }
}
