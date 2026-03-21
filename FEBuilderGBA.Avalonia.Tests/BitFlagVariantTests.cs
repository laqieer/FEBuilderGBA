using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Headless UI tests for all BitFlag variant controls:
/// - UbyteBitFlagViewModel (8-bit)
/// - UshortBitFlagViewModel (16-bit)
/// - UwordBitFlagViewModel (32-bit)
/// - UbyteBitFlagView, UshortBitFlagView, UwordBitFlagView (Avalonia windows)
///
/// Covers: instantiation, bit get/set, value round-trips, value change events,
/// edge cases (all bits set, no bits set), byte-level aliases, Load(), and hex display.
/// </summary>

#region UbyteBitFlagViewModel Tests (8-bit)

public class UbyteBitFlagViewModelTests
{
    [AvaloniaFact]
    public void Instantiation_DefaultValues()
    {
        var vm = new UbyteBitFlagViewModel();
        Assert.Equal(0u, vm.Value);
        Assert.Equal("0x00", vm.ValueHex);
        Assert.False(vm.IsLoaded);
        Assert.Equal("Byte Bit Flags (8-bit)", vm.MessageText);
    }

    [AvaloniaFact]
    public void Load_SetsValueAndIsLoaded()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0xA5);
        Assert.Equal(0xA5u, vm.Value);
        Assert.True(vm.IsLoaded);
        Assert.Equal("0xA5", vm.ValueHex);
    }

    [AvaloniaFact]
    public void Load_MasksToByteRange()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0x1FF); // Exceeds 8-bit range
        Assert.Equal(0xFFu, vm.Value);
    }

    [AvaloniaFact]
    public void Value_MasksToByteRange()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Value = 0x1AB;
        Assert.Equal(0xABu, vm.Value);
    }

    [AvaloniaFact]
    public void NoBitsSet_AllFlagsFalse()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0x00);
        Assert.False(vm.FlagBit01);
        Assert.False(vm.FlagBit02);
        Assert.False(vm.FlagBit04);
        Assert.False(vm.FlagBit08);
        Assert.False(vm.FlagBit10);
        Assert.False(vm.FlagBit20);
        Assert.False(vm.FlagBit40);
        Assert.False(vm.FlagBit80);
    }

    [AvaloniaFact]
    public void AllBitsSet_AllFlagsTrue()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0xFF);
        Assert.True(vm.FlagBit01);
        Assert.True(vm.FlagBit02);
        Assert.True(vm.FlagBit04);
        Assert.True(vm.FlagBit08);
        Assert.True(vm.FlagBit10);
        Assert.True(vm.FlagBit20);
        Assert.True(vm.FlagBit40);
        Assert.True(vm.FlagBit80);
    }

    [AvaloniaTheory]
    [InlineData(0, true, false, false, false, false, false, false, false)]
    [InlineData(1, false, true, false, false, false, false, false, false)]
    [InlineData(2, false, false, true, false, false, false, false, false)]
    [InlineData(3, false, false, false, true, false, false, false, false)]
    [InlineData(4, false, false, false, false, true, false, false, false)]
    [InlineData(5, false, false, false, false, false, true, false, false)]
    [InlineData(6, false, false, false, false, false, false, true, false)]
    [InlineData(7, false, false, false, false, false, false, false, true)]
    public void SingleBit_MapsCorrectly(int bit, bool b0, bool b1, bool b2, bool b3,
        bool b4, bool b5, bool b6, bool b7)
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(1u << bit);
        Assert.Equal(b0, vm.FlagBit01);
        Assert.Equal(b1, vm.FlagBit02);
        Assert.Equal(b2, vm.FlagBit04);
        Assert.Equal(b3, vm.FlagBit08);
        Assert.Equal(b4, vm.FlagBit10);
        Assert.Equal(b5, vm.FlagBit20);
        Assert.Equal(b6, vm.FlagBit40);
        Assert.Equal(b7, vm.FlagBit80);
    }

    [AvaloniaFact]
    public void SetBit_UpdatesValue()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0);
        vm.FlagBit01 = true;
        Assert.Equal(0x01u, vm.Value);

        vm.FlagBit80 = true;
        Assert.Equal(0x81u, vm.Value);

        vm.FlagBit01 = false;
        Assert.Equal(0x80u, vm.Value);
    }

    [AvaloniaFact]
    public void LegacyAliases_MapCorrectly()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0xA5); // 10100101
        Assert.Equal(vm.FlagBit01, vm.Bit0);
        Assert.Equal(vm.FlagBit02, vm.Bit1);
        Assert.Equal(vm.FlagBit04, vm.Bit2);
        Assert.Equal(vm.FlagBit08, vm.Bit3);
        Assert.Equal(vm.FlagBit10, vm.Bit4);
        Assert.Equal(vm.FlagBit20, vm.Bit5);
        Assert.Equal(vm.FlagBit40, vm.Bit6);
        Assert.Equal(vm.FlagBit80, vm.Bit7);
    }

    [AvaloniaFact]
    public void LegacyAlias_SetBit_UpdatesValue()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0);
        vm.Bit0 = true;
        Assert.True(vm.FlagBit01);
        Assert.Equal(0x01u, vm.Value);
    }

    [AvaloniaFact]
    public void B40_ReturnsValue()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0xCD);
        Assert.Equal(0xCDu, vm.B40);
    }

    [AvaloniaFact]
    public void ValueHex_FormattedCorrectly()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0x0A);
        Assert.Equal("0x0A", vm.ValueHex);

        vm.Load(0xFF);
        Assert.Equal("0xFF", vm.ValueHex);

        vm.Load(0x00);
        Assert.Equal("0x00", vm.ValueHex);
    }

    [AvaloniaFact]
    public void ValueRoundTrip_AllByteValues()
    {
        var vm = new UbyteBitFlagViewModel();
        for (uint v = 0; v < 256; v++)
        {
            vm.Load(v);
            Assert.Equal(v, vm.Value);
        }
    }

    [AvaloniaFact]
    public void PropertyChanged_FiredOnSetBit()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.Load(0);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

        vm.FlagBit04 = true;

        Assert.Contains("Value", changed);
        Assert.Contains("ValueHex", changed);
        Assert.Contains("FlagBit04", changed);
    }

    [AvaloniaFact]
    public void MessageText_CanBeSet()
    {
        var vm = new UbyteBitFlagViewModel();
        vm.MessageText = "Custom Label";
        Assert.Equal("Custom Label", vm.MessageText);
    }
}

#endregion

#region UshortBitFlagViewModel Tests (16-bit)

public class UshortBitFlagViewModelTests
{
    [AvaloniaFact]
    public void Instantiation_DefaultValues()
    {
        var vm = new UshortBitFlagViewModel();
        Assert.Equal(0u, vm.Value);
        Assert.Equal("0x0000", vm.ValueHex);
        Assert.False(vm.IsLoaded);
    }

    [AvaloniaFact]
    public void Load_SetsValueAndIsLoaded()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0xABCD);
        Assert.Equal(0xABCDu, vm.Value);
        Assert.True(vm.IsLoaded);
        Assert.Equal("0xABCD", vm.ValueHex);
    }

    [AvaloniaFact]
    public void Load_MasksToShortRange()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0x1FFFF); // Exceeds 16-bit range
        Assert.Equal(0xFFFFu, vm.Value);
    }

    [AvaloniaFact]
    public void Value_MasksToShortRange()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Value = 0x3ABCD;
        Assert.Equal(0xABCDu, vm.Value);
    }

    [AvaloniaFact]
    public void NoBitsSet_AllFlagsFalse()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0x0000);
        Assert.False(vm.LowBit01);
        Assert.False(vm.LowBit80);
        Assert.False(vm.HighBit01);
        Assert.False(vm.HighBit80);
    }

    [AvaloniaFact]
    public void AllBitsSet_AllFlagsTrue()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0xFFFF);
        Assert.True(vm.LowBit01);
        Assert.True(vm.LowBit02);
        Assert.True(vm.LowBit04);
        Assert.True(vm.LowBit08);
        Assert.True(vm.LowBit10);
        Assert.True(vm.LowBit20);
        Assert.True(vm.LowBit40);
        Assert.True(vm.LowBit80);
        Assert.True(vm.HighBit01);
        Assert.True(vm.HighBit02);
        Assert.True(vm.HighBit04);
        Assert.True(vm.HighBit08);
        Assert.True(vm.HighBit10);
        Assert.True(vm.HighBit20);
        Assert.True(vm.HighBit40);
        Assert.True(vm.HighBit80);
    }

    [AvaloniaFact]
    public void LowByte_BitsMapCorrectly()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0x00A5); // Low byte = 10100101
        Assert.True(vm.LowBit01);
        Assert.False(vm.LowBit02);
        Assert.True(vm.LowBit04);
        Assert.False(vm.LowBit08);
        Assert.False(vm.LowBit10);
        Assert.True(vm.LowBit20);
        Assert.False(vm.LowBit40);
        Assert.True(vm.LowBit80);
        // High byte all zeros
        Assert.False(vm.HighBit01);
        Assert.False(vm.HighBit80);
    }

    [AvaloniaFact]
    public void HighByte_BitsMapCorrectly()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0x5A00); // High byte = 01011010
        Assert.False(vm.LowBit01);
        Assert.False(vm.LowBit80);
        Assert.False(vm.HighBit01);
        Assert.True(vm.HighBit02);
        Assert.False(vm.HighBit04);
        Assert.True(vm.HighBit08);
        Assert.True(vm.HighBit10);
        Assert.False(vm.HighBit20);
        Assert.True(vm.HighBit40);
        Assert.False(vm.HighBit80);
    }

    [AvaloniaFact]
    public void SetBit_LowByte_UpdatesValue()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0);
        vm.LowBit01 = true;
        Assert.Equal(0x0001u, vm.Value);

        vm.LowBit80 = true;
        Assert.Equal(0x0081u, vm.Value);
    }

    [AvaloniaFact]
    public void SetBit_HighByte_UpdatesValue()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0);
        vm.HighBit01 = true;
        Assert.Equal(0x0100u, vm.Value);

        vm.HighBit80 = true;
        Assert.Equal(0x8100u, vm.Value);
    }

    [AvaloniaFact]
    public void SetBit_CrossByte_UpdatesCorrectly()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0);
        vm.LowBit01 = true;
        vm.HighBit01 = true;
        Assert.Equal(0x0101u, vm.Value);
    }

    [AvaloniaFact]
    public void ClearBit_UpdatesValue()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0xFFFF);
        vm.LowBit01 = false;
        Assert.Equal(0xFFFEu, vm.Value);

        vm.HighBit80 = false;
        Assert.Equal(0x7FFEu, vm.Value);
    }

    [AvaloniaFact]
    public void LegacyAliases_MapCorrectly()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0xA5C3);
        Assert.Equal(vm.LowBit01, vm.Bit0);
        Assert.Equal(vm.LowBit02, vm.Bit1);
        Assert.Equal(vm.LowBit04, vm.Bit2);
        Assert.Equal(vm.LowBit08, vm.Bit3);
        Assert.Equal(vm.LowBit10, vm.Bit4);
        Assert.Equal(vm.LowBit20, vm.Bit5);
        Assert.Equal(vm.LowBit40, vm.Bit6);
        Assert.Equal(vm.LowBit80, vm.Bit7);
        Assert.Equal(vm.HighBit01, vm.Bit8);
        Assert.Equal(vm.HighBit02, vm.Bit9);
        Assert.Equal(vm.HighBit04, vm.Bit10);
        Assert.Equal(vm.HighBit08, vm.Bit11);
        Assert.Equal(vm.HighBit10, vm.Bit12);
        Assert.Equal(vm.HighBit20, vm.Bit13);
        Assert.Equal(vm.HighBit40, vm.Bit14);
        Assert.Equal(vm.HighBit80, vm.Bit15);
    }

    [AvaloniaFact]
    public void ByteAliases_ReturnCorrectBytes()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0xABCD);
        Assert.Equal(0xCDu, vm.B40); // Low byte
        Assert.Equal(0xABu, vm.B41); // High byte
    }

    [AvaloniaFact]
    public void ValueHex_FormattedCorrectly()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0x00AB);
        Assert.Equal("0x00AB", vm.ValueHex);

        vm.Load(0xFFFF);
        Assert.Equal("0xFFFF", vm.ValueHex);
    }

    [AvaloniaFact]
    public void PropertyChanged_FiredOnSetBit()
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(0);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

        vm.HighBit40 = true;

        Assert.Contains("Value", changed);
        Assert.Contains("ValueHex", changed);
        Assert.Contains("HighBit40", changed);
    }

    [AvaloniaTheory]
    [InlineData(0, 0x0001u)]
    [InlineData(7, 0x0080u)]
    [InlineData(8, 0x0100u)]
    [InlineData(15, 0x8000u)]
    public void SingleBit_ProducesCorrectValue(int bit, uint expected)
    {
        var vm = new UshortBitFlagViewModel();
        vm.Load(expected);
        // Verify only the expected bit is set
        for (int i = 0; i < 16; i++)
        {
            bool actual = i switch
            {
                0 => vm.LowBit01, 1 => vm.LowBit02, 2 => vm.LowBit04, 3 => vm.LowBit08,
                4 => vm.LowBit10, 5 => vm.LowBit20, 6 => vm.LowBit40, 7 => vm.LowBit80,
                8 => vm.HighBit01, 9 => vm.HighBit02, 10 => vm.HighBit04, 11 => vm.HighBit08,
                12 => vm.HighBit10, 13 => vm.HighBit20, 14 => vm.HighBit40, 15 => vm.HighBit80,
                _ => false
            };
            if (i == bit)
                Assert.True(actual, $"Bit {i} should be set for value 0x{expected:X4}");
            else
                Assert.False(actual, $"Bit {i} should NOT be set for value 0x{expected:X4}");
        }
    }
}

#endregion

#region UwordBitFlagViewModel Tests (32-bit)

public class UwordBitFlagViewModelTests
{
    [AvaloniaFact]
    public void Instantiation_DefaultValues()
    {
        var vm = new UwordBitFlagViewModel();
        Assert.Equal(0u, vm.Value);
        Assert.Equal("0x00000000", vm.ValueHex);
        Assert.False(vm.IsLoaded);
    }

    [AvaloniaFact]
    public void Load_SetsValueAndIsLoaded()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0xDEADBEEF);
        Assert.Equal(0xDEADBEEFu, vm.Value);
        Assert.True(vm.IsLoaded);
        Assert.Equal("0xDEADBEEF", vm.ValueHex);
    }

    [AvaloniaFact]
    public void Load_FullRange_NoMasking()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0xFFFFFFFF);
        Assert.Equal(0xFFFFFFFFu, vm.Value);
    }

    [AvaloniaFact]
    public void NoBitsSet_AllFlagsFalse()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0x00000000);
        Assert.False(vm.Byte0Bit01);
        Assert.False(vm.Byte0Bit80);
        Assert.False(vm.Byte1Bit01);
        Assert.False(vm.Byte1Bit80);
        Assert.False(vm.Byte2Bit01);
        Assert.False(vm.Byte2Bit80);
        Assert.False(vm.Byte3Bit01);
        Assert.False(vm.Byte3Bit80);
    }

    [AvaloniaFact]
    public void AllBitsSet_AllFlagsTrue()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0xFFFFFFFF);
        Assert.True(vm.Byte0Bit01);
        Assert.True(vm.Byte0Bit80);
        Assert.True(vm.Byte1Bit01);
        Assert.True(vm.Byte1Bit80);
        Assert.True(vm.Byte2Bit01);
        Assert.True(vm.Byte2Bit80);
        Assert.True(vm.Byte3Bit01);
        Assert.True(vm.Byte3Bit80);
    }

    [AvaloniaTheory]
    [InlineData(0, 0x00000001u)]
    [InlineData(7, 0x00000080u)]
    [InlineData(8, 0x00000100u)]
    [InlineData(15, 0x00008000u)]
    [InlineData(16, 0x00010000u)]
    [InlineData(23, 0x00800000u)]
    [InlineData(24, 0x01000000u)]
    [InlineData(31, 0x80000000u)]
    public void SingleBit_ProducesCorrectValue(int bit, uint expected)
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0);
        // Set bit via the appropriate property
        switch (bit)
        {
            case 0: vm.Byte0Bit01 = true; break;
            case 7: vm.Byte0Bit80 = true; break;
            case 8: vm.Byte1Bit01 = true; break;
            case 15: vm.Byte1Bit80 = true; break;
            case 16: vm.Byte2Bit01 = true; break;
            case 23: vm.Byte2Bit80 = true; break;
            case 24: vm.Byte3Bit01 = true; break;
            case 31: vm.Byte3Bit80 = true; break;
        }
        Assert.Equal(expected, vm.Value);
    }

    [AvaloniaFact]
    public void Byte0_BitsMapCorrectly()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0x000000A5); // Byte 0 = 10100101
        Assert.True(vm.Byte0Bit01);
        Assert.False(vm.Byte0Bit02);
        Assert.True(vm.Byte0Bit04);
        Assert.False(vm.Byte0Bit08);
        Assert.False(vm.Byte0Bit10);
        Assert.True(vm.Byte0Bit20);
        Assert.False(vm.Byte0Bit40);
        Assert.True(vm.Byte0Bit80);
    }

    [AvaloniaFact]
    public void Byte1_BitsMapCorrectly()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0x00005A00); // Byte 1 = 01011010
        Assert.False(vm.Byte1Bit01);
        Assert.True(vm.Byte1Bit02);
        Assert.False(vm.Byte1Bit04);
        Assert.True(vm.Byte1Bit08);
        Assert.True(vm.Byte1Bit10);
        Assert.False(vm.Byte1Bit20);
        Assert.True(vm.Byte1Bit40);
        Assert.False(vm.Byte1Bit80);
    }

    [AvaloniaFact]
    public void Byte2_BitsMapCorrectly()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0x00C30000); // Byte 2 = 11000011
        Assert.True(vm.Byte2Bit01);
        Assert.True(vm.Byte2Bit02);
        Assert.False(vm.Byte2Bit04);
        Assert.False(vm.Byte2Bit08);
        Assert.False(vm.Byte2Bit10);
        Assert.False(vm.Byte2Bit20);
        Assert.True(vm.Byte2Bit40);
        Assert.True(vm.Byte2Bit80);
    }

    [AvaloniaFact]
    public void Byte3_BitsMapCorrectly()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0x3C000000); // Byte 3 = 00111100
        Assert.False(vm.Byte3Bit01);
        Assert.False(vm.Byte3Bit02);
        Assert.True(vm.Byte3Bit04);
        Assert.True(vm.Byte3Bit08);
        Assert.True(vm.Byte3Bit10);
        Assert.True(vm.Byte3Bit20);
        Assert.False(vm.Byte3Bit40);
        Assert.False(vm.Byte3Bit80);
    }

    [AvaloniaFact]
    public void SetBit_AcrossAllBytes()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0);

        vm.Byte0Bit01 = true;
        vm.Byte1Bit01 = true;
        vm.Byte2Bit01 = true;
        vm.Byte3Bit01 = true;
        Assert.Equal(0x01010101u, vm.Value);
    }

    [AvaloniaFact]
    public void ClearBit_UpdatesValue()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0xFFFFFFFF);
        vm.Byte0Bit01 = false;
        Assert.Equal(0xFFFFFFFEu, vm.Value);

        vm.Byte3Bit80 = false;
        Assert.Equal(0x7FFFFFFEu, vm.Value);
    }

    [AvaloniaFact]
    public void LegacyAliases_MapCorrectly()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0xDEADBEEF);
        // Byte 0 legacy
        Assert.Equal(vm.Byte0Bit01, vm.Bit0);
        Assert.Equal(vm.Byte0Bit02, vm.Bit1);
        Assert.Equal(vm.Byte0Bit04, vm.Bit2);
        Assert.Equal(vm.Byte0Bit08, vm.Bit3);
        Assert.Equal(vm.Byte0Bit10, vm.Bit4);
        Assert.Equal(vm.Byte0Bit20, vm.Bit5);
        Assert.Equal(vm.Byte0Bit40, vm.Bit6);
        Assert.Equal(vm.Byte0Bit80, vm.Bit7);
        // Byte 1 legacy
        Assert.Equal(vm.Byte1Bit01, vm.Bit8);
        Assert.Equal(vm.Byte1Bit02, vm.Bit9);
        Assert.Equal(vm.Byte1Bit04, vm.Bit10);
        Assert.Equal(vm.Byte1Bit08, vm.Bit11);
        Assert.Equal(vm.Byte1Bit10, vm.Bit12);
        Assert.Equal(vm.Byte1Bit20, vm.Bit13);
        Assert.Equal(vm.Byte1Bit40, vm.Bit14);
        Assert.Equal(vm.Byte1Bit80, vm.Bit15);
        // Byte 2 legacy
        Assert.Equal(vm.Byte2Bit01, vm.Bit16);
        Assert.Equal(vm.Byte2Bit02, vm.Bit17);
        Assert.Equal(vm.Byte2Bit04, vm.Bit18);
        Assert.Equal(vm.Byte2Bit08, vm.Bit19);
        Assert.Equal(vm.Byte2Bit10, vm.Bit20);
        Assert.Equal(vm.Byte2Bit20, vm.Bit21);
        Assert.Equal(vm.Byte2Bit40, vm.Bit22);
        Assert.Equal(vm.Byte2Bit80, vm.Bit23);
        // Byte 3 legacy
        Assert.Equal(vm.Byte3Bit01, vm.Bit24);
        Assert.Equal(vm.Byte3Bit02, vm.Bit25);
        Assert.Equal(vm.Byte3Bit04, vm.Bit26);
        Assert.Equal(vm.Byte3Bit08, vm.Bit27);
        Assert.Equal(vm.Byte3Bit10, vm.Bit28);
        Assert.Equal(vm.Byte3Bit20, vm.Bit29);
        Assert.Equal(vm.Byte3Bit40, vm.Bit30);
        Assert.Equal(vm.Byte3Bit80, vm.Bit31);
    }

    [AvaloniaFact]
    public void ByteAliases_ReturnCorrectBytes()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0xDEADBEEF);
        Assert.Equal(0xEFu, vm.B40); // Byte 0
        Assert.Equal(0xBEu, vm.B41); // Byte 1
        Assert.Equal(0xADu, vm.B42); // Byte 2
        Assert.Equal(0xDEu, vm.B43); // Byte 3
    }

    [AvaloniaFact]
    public void ValueHex_FormattedCorrectly()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0x0000ABCD);
        Assert.Equal("0x0000ABCD", vm.ValueHex);

        vm.Load(0xFFFFFFFF);
        Assert.Equal("0xFFFFFFFF", vm.ValueHex);

        vm.Load(0);
        Assert.Equal("0x00000000", vm.ValueHex);
    }

    [AvaloniaFact]
    public void PropertyChanged_FiredOnSetBit()
    {
        var vm = new UwordBitFlagViewModel();
        vm.Load(0);
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

        vm.Byte2Bit40 = true;

        Assert.Contains("Value", changed);
        Assert.Contains("ValueHex", changed);
        Assert.Contains("Byte2Bit40", changed);
    }

    [AvaloniaFact]
    public void PropertyChanged_FiredOnLoad()
    {
        var vm = new UwordBitFlagViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

        vm.Load(0xABCD1234);

        Assert.Contains("Value", changed);
        Assert.Contains("ValueHex", changed);
        Assert.Contains("IsLoaded", changed);
        Assert.Contains("Byte0Bit01", changed);
        Assert.Contains("Byte3Bit80", changed);
    }

    [AvaloniaFact]
    public void MessageText_CanBeSet()
    {
        var vm = new UwordBitFlagViewModel();
        vm.MessageText = "Class Ability Flags";
        Assert.Equal("Class Ability Flags", vm.MessageText);
    }
}

#endregion

#region UbyteBitFlagView Tests (8-bit Window)

public class UbyteBitFlagViewTests
{
    [AvaloniaFact]
    public void Instantiation_CreatesWindow()
    {
        var view = new UbyteBitFlagView();
        Assert.NotNull(view);
        Assert.Equal("Byte Bit Flags", view.ViewTitle);
    }

    [AvaloniaFact]
    public void AllCheckboxes_Exist()
    {
        var view = new UbyteBitFlagView();
        for (int i = 0; i < 8; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.NotNull(cb);
        }
    }

    [AvaloniaFact]
    public void HexInput_Exists()
    {
        var view = new UbyteBitFlagView();
        var nud = view.FindControl<NumericUpDown>("B40");
        Assert.NotNull(nud);
    }

    [AvaloniaFact]
    public void DefaultState_AllUnchecked()
    {
        var view = new UbyteBitFlagView();
        for (int i = 0; i < 8; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.False(cb!.IsChecked, $"Bit{i}Box should start unchecked");
        }
    }

    [AvaloniaFact]
    public void SelectFirstItem_ResetsToZero()
    {
        var view = new UbyteBitFlagView();
        // Trigger a non-zero state manually by checking a checkbox
        var bit0 = view.FindControl<CheckBox>("Bit0Box");
        Assert.NotNull(bit0);
        bit0!.IsChecked = true;

        view.SelectFirstItem();

        for (int i = 0; i < 8; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.False(cb!.IsChecked, $"Bit{i}Box should be unchecked after SelectFirstItem");
        }
    }

    [AvaloniaFact]
    public void IsLoaded_TrueAfterConstruction()
    {
        var view = new UbyteBitFlagView();
        Assert.True(view.IsLoaded);
    }
}

#endregion

#region UshortBitFlagView Tests (16-bit Window)

public class UshortBitFlagViewTests
{
    [AvaloniaFact]
    public void Instantiation_CreatesWindow()
    {
        var view = new UshortBitFlagView();
        Assert.NotNull(view);
        Assert.Equal("Short Bit Flags", view.ViewTitle);
    }

    [AvaloniaFact]
    public void AllCheckboxes_Exist()
    {
        var view = new UshortBitFlagView();
        for (int i = 0; i < 16; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.NotNull(cb);
        }
    }

    [AvaloniaFact]
    public void HexInputs_Exist()
    {
        var view = new UshortBitFlagView();
        var b40 = view.FindControl<NumericUpDown>("B40");
        var b41 = view.FindControl<NumericUpDown>("B41");
        Assert.NotNull(b40);
        Assert.NotNull(b41);
    }

    [AvaloniaFact]
    public void DefaultState_AllUnchecked()
    {
        var view = new UshortBitFlagView();
        for (int i = 0; i < 16; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.False(cb!.IsChecked, $"Bit{i}Box should start unchecked");
        }
    }

    [AvaloniaFact]
    public void SelectFirstItem_ResetsToZero()
    {
        var view = new UshortBitFlagView();
        var bit8 = view.FindControl<CheckBox>("Bit8Box");
        Assert.NotNull(bit8);
        bit8!.IsChecked = true;

        view.SelectFirstItem();

        for (int i = 0; i < 16; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.False(cb!.IsChecked, $"Bit{i}Box should be unchecked after SelectFirstItem");
        }
    }

    [AvaloniaFact]
    public void IsLoaded_TrueAfterConstruction()
    {
        var view = new UshortBitFlagView();
        Assert.True(view.IsLoaded);
    }
}

#endregion

#region UwordBitFlagView Tests (32-bit Window)

public class UwordBitFlagViewTests
{
    [AvaloniaFact]
    public void Instantiation_CreatesWindow()
    {
        var view = new UwordBitFlagView();
        Assert.NotNull(view);
        Assert.Equal("Word Bit Flags", view.ViewTitle);
    }

    [AvaloniaFact]
    public void AllCheckboxes_Exist()
    {
        var view = new UwordBitFlagView();
        for (int i = 0; i < 32; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.NotNull(cb);
        }
    }

    [AvaloniaFact]
    public void HexInputs_Exist()
    {
        var view = new UwordBitFlagView();
        var b40 = view.FindControl<NumericUpDown>("B40");
        var b41 = view.FindControl<NumericUpDown>("B41");
        var b42 = view.FindControl<NumericUpDown>("B42");
        var b43 = view.FindControl<NumericUpDown>("B43");
        Assert.NotNull(b40);
        Assert.NotNull(b41);
        Assert.NotNull(b42);
        Assert.NotNull(b43);
    }

    [AvaloniaFact]
    public void DefaultState_AllUnchecked()
    {
        var view = new UwordBitFlagView();
        for (int i = 0; i < 32; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.False(cb!.IsChecked, $"Bit{i}Box should start unchecked");
        }
    }

    [AvaloniaFact]
    public void SelectFirstItem_ResetsToZero()
    {
        var view = new UwordBitFlagView();
        var bit24 = view.FindControl<CheckBox>("Bit24Box");
        Assert.NotNull(bit24);
        bit24!.IsChecked = true;

        view.SelectFirstItem();

        for (int i = 0; i < 32; i++)
        {
            var cb = view.FindControl<CheckBox>($"Bit{i}Box");
            Assert.False(cb!.IsChecked, $"Bit{i}Box should be unchecked after SelectFirstItem");
        }
    }

    [AvaloniaFact]
    public void IsLoaded_TrueAfterConstruction()
    {
        var view = new UwordBitFlagView();
        Assert.True(view.IsLoaded);
    }
}

#endregion
