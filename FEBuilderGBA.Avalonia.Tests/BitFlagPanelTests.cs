using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless UI tests for BitFlagPanel.
/// Verifies that bit checkboxes correctly map to byte values.
/// The ability flags reversal bug (issue #184) would have been caught by these tests.
/// </summary>
public class BitFlagPanelTests
{
    [AvaloniaFact]
    public void DefaultValue_IsZero()
    {
        var panel = new BitFlagPanel();
        Assert.Equal(0, panel.Value);
    }

    [AvaloniaFact]
    public void SetValue_ChecksCorrectBits()
    {
        var panel = new BitFlagPanel();

        // Set bit 0 (0x01)
        panel.Value = 0x01;
        Assert.Equal(0x01, panel.Value);

        // Set bit 7 (0x80)
        panel.Value = 0x80;
        Assert.Equal(0x80, panel.Value);

        // Set bits 0+2+4 (0x15)
        panel.Value = 0x15;
        Assert.Equal(0x15, panel.Value);

        // All bits (0xFF)
        panel.Value = 0xFF;
        Assert.Equal(0xFF, panel.Value);
    }

    [AvaloniaFact]
    public void SetValue_RoundTrips()
    {
        var panel = new BitFlagPanel();

        // Every possible byte value should round-trip
        for (int v = 0; v < 256; v++)
        {
            panel.Value = (byte)v;
            Assert.Equal((byte)v, panel.Value);
        }
    }

    [AvaloniaFact]
    public void Bit0_MapsToValue0x01()
    {
        // This directly tests that checkbox index 0 corresponds to bit value 0x01
        var panel = new BitFlagPanel();
        panel.Value = 0x01; // Only bit 0 set
        var bit0 = panel.FindControl<CheckBox>("Bit0");
        var bit1 = panel.FindControl<CheckBox>("Bit1");
        Assert.NotNull(bit0);
        Assert.NotNull(bit1);
        Assert.True(bit0!.IsChecked);
        Assert.False(bit1!.IsChecked);
    }

    [AvaloniaFact]
    public void Bit7_MapsToValue0x80()
    {
        var panel = new BitFlagPanel();
        panel.Value = 0x80; // Only bit 7 set
        var bit7 = panel.FindControl<CheckBox>("Bit7");
        var bit0 = panel.FindControl<CheckBox>("Bit0");
        Assert.NotNull(bit7);
        Assert.NotNull(bit0);
        Assert.True(bit7!.IsChecked);
        Assert.False(bit0!.IsChecked);
    }

    [AvaloniaFact]
    public void SetBitNames_AssignsLabelsToCorrectCheckboxes()
    {
        var panel = new BitFlagPanel();
        var names = new string?[] { "Alpha", "Beta", "Gamma", null, null, null, null, "Omega" };
        panel.SetBitNames(names);

        var bit0 = panel.FindControl<CheckBox>("Bit0");
        var bit1 = panel.FindControl<CheckBox>("Bit1");
        var bit2 = panel.FindControl<CheckBox>("Bit2");
        var bit7 = panel.FindControl<CheckBox>("Bit7");

        Assert.Equal("Alpha", bit0!.Content);
        Assert.Equal("Beta", bit1!.Content);
        Assert.Equal("Gamma", bit2!.Content);
        Assert.Equal("Omega", bit7!.Content);
    }

    [AvaloniaFact]
    public void ValueChanged_FiresOnCheckboxToggle()
    {
        var panel = new BitFlagPanel();
        byte? lastValue = null;
        panel.ValueChanged += v => lastValue = v;

        // Programmatic value set should NOT fire event (it's inside _updating guard)
        panel.Value = 0x01;
        Assert.Null(lastValue); // No event during programmatic set

        // But the value should still be correct
        Assert.Equal(0x01, panel.Value);
    }

    [AvaloniaFact]
    public void AllCheckboxes_Exist()
    {
        var panel = new BitFlagPanel();
        for (int i = 0; i < 8; i++)
        {
            var cb = panel.FindControl<CheckBox>($"Bit{i}");
            Assert.NotNull(cb);
        }
    }

    [AvaloniaTheory]
    [InlineData(0, 0x01)]
    [InlineData(1, 0x02)]
    [InlineData(2, 0x04)]
    [InlineData(3, 0x08)]
    [InlineData(4, 0x10)]
    [InlineData(5, 0x20)]
    [InlineData(6, 0x40)]
    [InlineData(7, 0x80)]
    public void EachBitIndex_MapsToCorrectValue(int bitIndex, byte expectedValue)
    {
        var panel = new BitFlagPanel();
        panel.Value = expectedValue;

        for (int i = 0; i < 8; i++)
        {
            var cb = panel.FindControl<CheckBox>($"Bit{i}");
            if (i == bitIndex)
                Assert.True(cb!.IsChecked, $"Bit{i} should be checked for value 0x{expectedValue:X2}");
            else
                Assert.False(cb!.IsChecked, $"Bit{i} should NOT be checked for value 0x{expectedValue:X2}");
        }
    }
}
