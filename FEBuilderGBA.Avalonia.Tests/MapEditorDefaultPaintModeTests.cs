using System;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public sealed class MapEditorDefaultPaintModeTests : IDisposable
{
    readonly ROM? _savedRom = CoreState.ROM;

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
    }

    [AvaloniaFact]
    public void PaintMode_DefaultsOn_AndCanBeToggled()
    {
        var view = new MapEditorView();
        try
        {
            var paintMode = view.FindControl<CheckBox>("PaintModeCheck");

            Assert.NotNull(paintMode);
            Assert.True(paintMode.IsChecked == true);

            paintMode.IsChecked = false;
            Assert.False(paintMode.IsChecked == true);

            paintMode.IsChecked = true;
            Assert.True(paintMode.IsChecked == true);
        }
        finally
        {
            view.Close();
        }
    }

    [AvaloniaFact]
    public void OpeningEditor_AttachAndListLoad_DoesNotMutateRom()
    {
        var rom = new ROM();
        rom.LoadLow("test.gba", new byte[0x200000], "NAZO");
        CoreState.ROM = rom;
        byte[] before = (byte[])rom.Data.Clone();

        var view = new MapEditorView();
        try
        {
            view.Show();
            view.UpdateLayout();

            Assert.Equal(before, rom.Data);
        }
        finally
        {
            view.Close();
        }
    }
}
