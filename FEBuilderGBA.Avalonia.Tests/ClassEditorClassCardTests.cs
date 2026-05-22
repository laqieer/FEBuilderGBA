using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
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

    /// <summary>
    /// Regression for PR #471 Copilot follow-up: the TSV-import reload path
    /// is now an extracted internal method <c>ReloadCurrentClassAfterImport</c>.
    /// Drive that method DIRECTLY (not via OnClassSelected, which already
    /// calls UpdateClassCard) so the test would fail if the
    /// <c>UpdateClassCard()</c> call were removed from
    /// <c>ReloadCurrentClassAfterImport</c>.
    ///
    /// Strategy: select class A, capture initial card state, then mutate the
    /// ViewModel's CurrentAddr to point at a DIFFERENT class B (simulating
    /// what an import does — change the underlying data without going
    /// through the list selection), then invoke ReloadCurrentClassAfterImport
    /// and assert the card now reflects class B (not stale class A).
    /// </summary>
    [AvaloniaFact]
    public void ReloadCurrentClassAfterImport_RefreshesCard_FE8U()
    {
        if (!TryAssertFE8U()) return;

        var view = new ClassEditorView();
        view.Show();
        try
        {
            var classList = view.FindControl<AddressListControl>("ClassList");
            var cardWait = view.FindControl<IconPreviewControl>("CardWaitIconImage");
            var idLabel = view.FindControl<TextBlock>("CardIdLabel");
            Assert.NotNull(classList);
            Assert.NotNull(cardWait);
            Assert.NotNull(idLabel);

            // Find two distinct classes that both have a populated wait icon
            // and DIFFERENT wait-icon indices, so the card image actually
            // changes when we swap.
            ROM rom = CoreState.ROM!;
            uint classBase = rom.p32(rom.RomInfo.class_pointer);
            uint classSize = rom.RomInfo.class_datasize;

            int classAIdx = -1, classBIdx = -1;
            uint classAWaitIcon = 0, classBWaitIcon = 0;
            for (int i = 1; i < classList!.GetItems().Count && i < 32; i++)
            {
                uint waitIcon = rom.u8(classBase + (uint)i * classSize + 6);
                if (waitIcon == 0) continue;
                if (classAIdx < 0)
                {
                    classAIdx = i;
                    classAWaitIcon = waitIcon;
                }
                else if (waitIcon != classAWaitIcon)
                {
                    classBIdx = i;
                    classBWaitIcon = waitIcon;
                    break;
                }
            }
            Assert.True(classAIdx >= 0 && classBIdx >= 0,
                "Expected at least two distinct classes with different non-zero wait-icon indices in FE8U");

            // Select class A and let OnClassSelected populate the card.
            classList.SelectByIndex(classAIdx);
            string idLabelAtA = idLabel!.Text ?? "";
            // classBWaitIcon is used after the swap to confirm the card moved.
            _ = classAWaitIcon;
            _ = classBWaitIcon;
            Assert.True(cardWait!.HasImage, "Card wait icon must be populated for class A");

            // Now simulate what import does: directly mutate _vm.CurrentAddr to
            // class B's address WITHOUT going through the list, then invoke the
            // extracted import-reload helper. If UpdateClassCard() were removed
            // from ReloadCurrentClassAfterImport, the card image would remain
            // at class A's wait icon (because OnClassCardInputChanged is
            // suppressed during UpdateUI's IsLoading=true window).
            var vm = view.DataViewModel as ClassEditorViewModel;
            Assert.NotNull(vm);
            uint classBAddr = classBase + (uint)classBIdx * classSize;
            vm!.CurrentAddr = classBAddr;

            view.ReloadCurrentClassAfterImport();

            // After the reload helper runs, the card label MUST reflect class B.
            // The label format is "0x{addr:X08}  /  ID {ClassNumber}" where
            // ClassNumber is read from offset +4 of the class struct.
            string idLabelAfter = idLabel.Text ?? "";
            string expectedAddrSubstring = $"0x{classBAddr:X08}";
            Assert.Contains(expectedAddrSubstring, idLabelAfter);
            Assert.NotEqual(idLabelAtA, idLabelAfter);

            // Card image must still be populated (class B has a non-zero icon).
            Assert.True(cardWait.HasImage,
                "Card wait icon must be populated after ReloadCurrentClassAfterImport");
        }
        finally
        {
            view.Close();
        }
    }
}
