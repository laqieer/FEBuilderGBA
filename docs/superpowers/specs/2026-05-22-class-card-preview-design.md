# Class Card Preview for Avalonia Class Editor — Design Doc

**Date:** 2026-05-22
**Issue:** [#357](https://github.com/laqieer/FEBuilderGBA/issues/357)
**Status:** Plan v2 approved by Copilot CLI review

## Background

WinForms `ClassForm` renders a "class card" preview block at the top-right of the editor (designer coords x=724-888, y=27-152):

- **L_5_CLASS** (TextBoxEx, 75x20 @ 724,27) — class name (read-only, NAMEPOINTER-wired)
- **L_8_PORTRAIT_CLASS** (InterpolatedPictureBox, 112x101 @ 724,51) — class face portrait, StretchImage + Bicubic
- **L_6_CLASSICONSRC** (InterpolatedPictureBox, 43x43 @ 845,109) — class wait icon (map sprite)

Populated by `InputFormRef` auto-wiring rules:
- `PORTRAIT:CLASS` linktype calls `ImagePortraitForm.DrawPortraitClass(face_id)`
- `CLASSICONSRC` linktype calls `ImageUnitWaitIconFrom.DrawWaitUnitIconBitmap(waitIconId, 0, false)`

Avalonia `ClassEditorView` currently has the wait-icon preview in the LEFT sidebar (next to the address list) but lacks the consolidated "card" with the class face portrait. Issue #357 screenshot confirms the Avalonia editor lacks the class-card visual entirely.

## Goal

Add a class-card preview panel at the top of the Avalonia `ClassEditorView` right pane that mirrors WinForms layout: class name + class face portrait (left) + class wait icon (right). Updates live as the selected class changes or as the user edits `PortraitIdBox` / `WaitIconBox`.

## Version Differences (FE6 vs FE7/8)

`RomInfo.portrait_datasize` differs by version:

| Version | Size | Layout |
| --- | --- | --- |
| FE6 (16-byte struct) | 16 | D0=unit_face, D4=map_face, D8=palette, no D12/D16 |
| FE7/8 (28-byte struct) | 28 | D0=unit_face, D4=map_face, D8=palette, D12=mouth, D16=class_card |

WinForms branches via `ImagePortraitFE6Form.DrawPortraitClassFE6(id)`:
- Reads D0/D4/D8
- Renders D0 with palette via `DrawPortraitClass` only when D4 == 0 (entry is a pure class card)
- Returns blank otherwise

## Design

### Helper — `PreviewIconHelper.LoadClassFacePortrait(uint portraitId)`

New static method in `FEBuilderGBA.Avalonia/Services/PreviewIconHelper.cs`. Returns `IImage?` (null on any error).

Pseudocode:

```csharp
public static IImage? LoadClassFacePortrait(uint portraitId)
{
    ROM rom = CoreState.ROM;
    if (rom?.RomInfo == null || portraitId == 0) return null;
    try
    {
        // p32: deref pointer-table location -> portrait-table base
        uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
        if (!U.isSafetyOffset(baseAddr)) return null;

        uint dataSize = rom.RomInfo.portrait_datasize;
        if (dataSize == 0) return null;
        uint addr = baseAddr + portraitId * dataSize;
        if (addr + dataSize > (uint)rom.Data.Length) return null;

        if (rom.RomInfo.version == 6)
        {
            // FE6: 16-byte struct, mirror DrawPortraitClassFE6
            uint unitFace = rom.u32(addr + 0);
            uint mapFace  = rom.u32(addr + 4);
            uint palette  = rom.u32(addr + 8);
            if (mapFace != 0) return null;
            if (!U.isPointer(unitFace) || !U.isPointer(palette)) return null;
            return PortraitRendererCore.DrawPortraitClass(unitFace, palette);
        }
        else
        {
            // FE7/8: 28-byte struct, D16 = class card
            uint palette   = rom.u32(addr + 8);
            uint classCard = rom.u32(addr + 16);
            if (!U.isPointer(classCard) || !U.isPointer(palette)) return null;
            return PortraitRendererCore.DrawPortraitClass(classCard, palette);
        }
    }
    catch { return null; }
}
```

### UI — `ClassEditorView.axaml`

Insert above the existing "Identity / Misc" expander, inside the right-pane `StackPanel`:

```xml
<!-- Class Card Preview (issue #357) -->
<Border Name="ClassCardBorder" IsVisible="False"
        BorderBrush="Gray" BorderThickness="1" Padding="8" CornerRadius="4"
        Background="{DynamicResource SystemControlBackgroundAltHighBrush}"
        Margin="0,4,0,2">
  <Grid ColumnDefinitions="Auto,*,Auto" RowDefinitions="Auto">
    <controls:IconPreviewControl
        AutomationProperties.AutomationId="ClassEditor_CardPortrait_Image"
        Name="CardPortraitImage" Grid.Column="0"
        Scale="2" MaxImageWidth="80" MaxImageHeight="80" Margin="0,0,12,0" />
    <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="4">
      <TextBlock Text="Class Card" FontWeight="Bold" FontSize="14" />
      <TextBlock AutomationProperties.AutomationId="ClassEditor_CardName_Label"
                 Name="CardNameLabel" FontSize="13" />
      <TextBlock AutomationProperties.AutomationId="ClassEditor_CardId_Label"
                 Name="CardIdLabel" Foreground="Gray" FontSize="11" />
    </StackPanel>
    <controls:IconPreviewControl
        AutomationProperties.AutomationId="ClassEditor_CardWaitIcon_Image"
        Name="CardWaitIconImage" Grid.Column="2"
        Scale="2" MaxImageWidth="32" MaxImageHeight="32" VerticalAlignment="Center" />
  </Grid>
</Border>
```

The existing left-sidebar `ListPreviewBorder` (wait-icon-only) stays — that's the list-row preview, not the editor card.

### Code-behind — `ClassEditorView.axaml.cs`

Add `UpdateClassCard()` method invoked by:
- `UpdateUI()` after data load (selection changed)
- `PortraitIdBox.ValueChanged` (live update when user edits portrait field)
- `WaitIconBox.ValueChanged` (live update when user edits wait icon field)

```csharp
void UpdateClassCard()
{
    try
    {
        uint portraitId = (uint)(PortraitIdBox.Value ?? 0);
        uint waitIcon   = (uint)(WaitIconBox.Value ?? 0);

        var facePic = PreviewIconHelper.LoadClassFacePortrait(portraitId);
        var waitPic = PreviewIconHelper.LoadClassWaitIcon(waitIcon);

        CardPortraitImage.SetImage(facePic);
        CardWaitIconImage.SetImage(waitPic);

        CardNameLabel.Text = _vm.Name;
        CardIdLabel.Text   = $"0x{_vm.CurrentAddr:X08}  /  ID {_vm.ClassNumber}";
        ClassCardBorder.IsVisible = true;

        facePic?.Dispose();
        waitPic?.Dispose();
    }
    catch
    {
        ClassCardBorder.IsVisible = false;
    }
}
```

## Tests

### `PreviewIconHelperClassFacePortraitTests` (FEBuilderGBA.Avalonia.Tests)

Helper-level tests using `RomFixture`:

1. `LoadClassFacePortrait_NullRom_ReturnsNull`
2. `LoadClassFacePortrait_ZeroId_ReturnsNull`
3. `LoadClassFacePortrait_FE8U_FirstClass_ReturnsImage` (uses `RomFixture`)
4. `LoadClassFacePortrait_FE8U_ImageWidth_Is80px` (matches 10-tile stride from `PortraitRendererCore.DrawPortraitClass`)

### `ClassEditorClassCardTests` (FEBuilderGBA.Avalonia.Tests)

Headless view tests using `AvaloniaFact` + `RomFixture`:

1. `ClassCardBorder_IsPresent_AfterFirstClassLoad`
2. `CardPortraitImage_HasImage_AfterFirstClassLoad`
3. `CardWaitIconImage_HasImage_AfterFirstClassLoad`
4. `EditingWaitIconId_UpdatesCardWaitIconImage`

## Files

- `FEBuilderGBA.Avalonia/Services/PreviewIconHelper.cs` — add `LoadClassFacePortrait`
- `FEBuilderGBA.Avalonia/Views/ClassEditorView.axaml` — add `ClassCardBorder`
- `FEBuilderGBA.Avalonia/Views/ClassEditorView.axaml.cs` — add `UpdateClassCard()` + wire change handlers
- `FEBuilderGBA.Avalonia.Tests/PreviewIconHelperClassFacePortraitTests.cs` — new
- `FEBuilderGBA.Avalonia.Tests/ClassEditorClassCardTests.cs` — new
- `docs/superpowers/specs/2026-05-22-class-card-preview-design.md` — this doc
- `README.md` — note the new preview

## Out of scope

- Click-to-jump from portrait/wait-icon (already covered by existing `Portrait_Link` hyperlink)
- Animation playback (legacy GUI shows still images)
- FE-Repo browser integration (covered by existing Portrait Viewer)

## Acceptance

- Avalonia Class Editor shows a class-card preview at the top of the right pane for every selected class
- Preview updates live when `PortraitIdBox` or `WaitIconBox` change
- Works for FE6/FE7/FE8 (helpers branch by version)
- Screenshot in PR shows the populated card for a known class
