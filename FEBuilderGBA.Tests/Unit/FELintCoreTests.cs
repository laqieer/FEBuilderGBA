using System;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for FELintCore — core lint types and pointer validation
    /// extracted to FEBuilderGBA.Core (Unit 9).
    /// </summary>
    public class FELintCoreTests
    {
        private ROM CreateTestROM(int size = 0x100)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[size], "NAZO");
            return rom;
        }

        // ---- Type enum ----

        [Fact]
        public void Type_Enum_HasExpectedValues()
        {
            Assert.Equal(0, (int)FELintCore.Type.EVENT_COND_TURN);
            Assert.True(Enum.IsDefined(typeof(FELintCore.Type), FELintCore.Type.FELINT_SYSTEM_ERROR));
        }

        [Fact]
        public void Type_Enum_AllMembersMatchOriginalOrder()
        {
            // Verify key ordinal positions match the original FELint.Type
            Assert.Equal(10, (int)FELintCore.Type.EVENTSCRIPT);
            Assert.Equal(11, (int)FELintCore.Type.EVENTUNITS);
            Assert.Equal(12, (int)FELintCore.Type.MAPSETTING);
            Assert.Equal(35, (int)FELintCore.Type.ITEM);
        }

        // ---- ErrorSt ----

        [Fact]
        public void ErrorSt_Constructor_SetsProperties()
        {
            var err = new FELintCore.ErrorSt(
                FELintCore.Type.ITEM, 0x100, "test error", 0x200);
            Assert.Equal(FELintCore.Type.ITEM, err.DataType);
            Assert.Equal(0x100u, err.Addr);
            Assert.Equal("test error", err.ErrorMessage);
            Assert.Equal(0x200u, err.Tag);
        }

        [Fact]
        public void ErrorSt_DefaultTag_IsNotFound()
        {
            var err = new FELintCore.ErrorSt(
                FELintCore.Type.CLASS, 0x50, "msg");
            Assert.Equal(U.NOT_FOUND, err.Tag);
        }

        // ---- SYSTEM_MAP_ID ----

        [Fact]
        public void SYSTEM_MAP_ID_MatchesOriginal()
        {
            Assert.Equal(0xEEEEEEEEu, FELintCore.SYSTEM_MAP_ID);
        }

        // ---- IsAligned4 ----

        [Fact]
        public void IsAligned4_ReturnsTrueForAligned()
        {
            Assert.True(FELintCore.IsAligned4(0));
            Assert.True(FELintCore.IsAligned4(4));
            Assert.True(FELintCore.IsAligned4(0x100));
        }

        [Fact]
        public void IsAligned4_ReturnsFalseForMisaligned()
        {
            Assert.False(FELintCore.IsAligned4(1));
            Assert.False(FELintCore.IsAligned4(2));
            Assert.False(FELintCore.IsAligned4(3));
            Assert.False(FELintCore.IsAligned4(0x101));
        }

        // ---- IsValidPointer ----

        [Fact]
        public void IsValidPointer_NullIsValid()
        {
            ROM rom = CreateTestROM();
            Assert.True(FELintCore.IsValidPointer(rom, 0));
        }

        [Fact]
        public void IsValidPointer_ValidPointer()
        {
            ROM rom = CreateTestROM(0x100);
            Assert.True(FELintCore.IsValidPointer(rom, 0x08000010)); // offset 0x10
        }

        [Fact]
        public void IsValidPointer_OutOfRange()
        {
            ROM rom = CreateTestROM(0x100);
            Assert.False(FELintCore.IsValidPointer(rom, 0x08001000)); // offset 0x1000 > 0x100
        }

        // ---- CheckPointer ----

        [Fact]
        public void CheckPointer_NullPointer_ReturnsNull()
        {
            // ROM with zeroed data — u32 at offset 0 reads 0 (null pointer)
            ROM rom = CreateTestROM(0x100);
            var result = FELintCore.CheckPointer(rom, FELintCore.Type.ITEM, 0, "test context");
            Assert.Null(result);
        }

        [Fact]
        public void CheckPointer_AddressOutOfRange_ReturnsError()
        {
            ROM rom = CreateTestROM(0x100);
            var result = FELintCore.CheckPointer(rom, FELintCore.Type.UNIT, 0x200, "out of range");
            Assert.NotNull(result);
            Assert.Equal(FELintCore.Type.UNIT, result.DataType);
            Assert.Contains("out of ROM range", result.ErrorMessage);
        }

        [Fact]
        public void CheckPointer_InvalidGBAPointer_ReturnsError()
        {
            // Write a non-GBA-pointer value into the ROM
            byte[] data = new byte[0x100];
            data[0] = 0x01; data[1] = 0x00; data[2] = 0x00; data[3] = 0x00; // value = 1 (not a GBA pointer)
            ROM rom = new ROM();
            rom.LoadLow("test.gba", data, "NAZO");

            var result = FELintCore.CheckPointer(rom, FELintCore.Type.CLASS, 0, "bad pointer");
            Assert.NotNull(result);
            Assert.Contains("Not a valid GBA pointer", result.ErrorMessage);
        }

        [Fact]
        public void CheckPointer_ValidPointerInRange_ReturnsNull()
        {
            // Write a valid GBA pointer 0x08000010 at offset 0
            byte[] data = new byte[0x100];
            uint pointer = 0x08000010;
            data[0] = (byte)(pointer & 0xFF);
            data[1] = (byte)((pointer >> 8) & 0xFF);
            data[2] = (byte)((pointer >> 16) & 0xFF);
            data[3] = (byte)((pointer >> 24) & 0xFF);
            ROM rom = new ROM();
            rom.LoadLow("test.gba", data, "NAZO");

            var result = FELintCore.CheckPointer(rom, FELintCore.Type.EVENTSCRIPT, 0, "valid pointer");
            Assert.Null(result);
        }

        [Fact]
        public void CheckPointer_PointerTargetOutOfRange_ReturnsError()
        {
            // Write a valid GBA pointer that points beyond ROM size
            byte[] data = new byte[0x100];
            uint pointer = 0x08001000; // offset 0x1000 > 0x100
            data[0] = (byte)(pointer & 0xFF);
            data[1] = (byte)((pointer >> 8) & 0xFF);
            data[2] = (byte)((pointer >> 16) & 0xFF);
            data[3] = (byte)((pointer >> 24) & 0xFF);
            ROM rom = new ROM();
            rom.LoadLow("test.gba", data, "NAZO");

            var result = FELintCore.CheckPointer(rom, FELintCore.Type.MAPSETTING, 0, "out of range target");
            Assert.NotNull(result);
            Assert.Contains("Pointer target out of range", result.ErrorMessage);
        }

        // ---- CheckPointerAligned4 ----

        [Fact]
        public void CheckPointerAligned4_NullPointer_ReturnsNull()
        {
            ROM rom = CreateTestROM(0x100);
            var result = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.ITEM, 0, "aligned null");
            Assert.Null(result);
        }

        [Fact]
        public void CheckPointerAligned4_AlignedPointer_ReturnsNull()
        {
            byte[] data = new byte[0x100];
            uint pointer = 0x08000010; // offset 0x10, aligned
            data[0] = (byte)(pointer & 0xFF);
            data[1] = (byte)((pointer >> 8) & 0xFF);
            data[2] = (byte)((pointer >> 16) & 0xFF);
            data[3] = (byte)((pointer >> 24) & 0xFF);
            ROM rom = new ROM();
            rom.LoadLow("test.gba", data, "NAZO");

            var result = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.EVENTSCRIPT, 0, "aligned");
            Assert.Null(result);
        }

        [Fact]
        public void CheckPointerAligned4_MisalignedPointer_ReturnsError()
        {
            byte[] data = new byte[0x100];
            uint pointer = 0x08000011; // offset 0x11, NOT aligned
            data[0] = (byte)(pointer & 0xFF);
            data[1] = (byte)((pointer >> 8) & 0xFF);
            data[2] = (byte)((pointer >> 16) & 0xFF);
            data[3] = (byte)((pointer >> 24) & 0xFF);
            ROM rom = new ROM();
            rom.LoadLow("test.gba", data, "NAZO");

            var result = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.CLASS, 0, "misaligned");
            Assert.NotNull(result);
            Assert.Contains("not 4-byte aligned", result.ErrorMessage);
        }

        [Fact]
        public void CheckPointerAligned4_InvalidPointer_ReturnsPointerError()
        {
            ROM rom = CreateTestROM(0x100);
            var result = FELintCore.CheckPointerAligned4(rom, FELintCore.Type.UNIT, 0x200, "bad addr");
            Assert.NotNull(result);
            Assert.Contains("out of ROM range", result.ErrorMessage);
        }
    }
}
