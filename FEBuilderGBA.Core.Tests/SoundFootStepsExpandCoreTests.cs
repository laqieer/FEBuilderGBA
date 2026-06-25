// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for SoundFootStepsExpandCore - the Core-side port of
// SoundFootStepsForm.SwitchListExpandsButton_Click (#1449): Switch2 table
// expansion + the FE8 PlaySoundStepByClass ASM hardcode fix.
//
// Synthetic FE8U/FE8J ROM bytes exercise the switch2 reads + the
// expansion mutation + the FE8-specific engine patch without a real ROM.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests;

[Collection("SharedState")]
public class SoundFootStepsExpandCoreTests
{
    const string FE8U_CODE = "BE8E01";
    const string FE8J_CODE = "BE8J01";
    const string FE6_CODE = "AFEJ01";

    static ROM MakeFe8WithSwitch2(string code, byte start = 0, byte countMinusOne = 4,
        uint tableAddr = 0x00800000u)
    {
        var bytes = new byte[0x1100000];
        var rom = new ROM();
        rom.LoadLow("synthetic.gba", bytes, code);

        uint switchAddr = rom.RomInfo.sound_foot_steps_switch2_address;
        uint ptrSlot = rom.RomInfo.sound_foot_steps_pointer;
        Assert.True(switchAddr != 0, "FE8 must define sound_foot_steps_switch2_address");
        Assert.True(ptrSlot != 0, "FE8 must define sound_foot_steps_pointer");

        bytes[switchAddr + 0] = start;
        bytes[switchAddr + 1] = 0x38;
        bytes[switchAddr + 2] = countMinusOne;
        bytes[switchAddr + 3] = 0x28;

        uint count = (uint)(countMinusOne + 1);
        for (uint i = 0; i < count; i++)
        {
            BitConverter.GetBytes(0x08000100u + i).CopyTo(bytes, tableAddr + i * 4);
        }
        BitConverter.GetBytes(tableAddr | 0x08000000u).CopyTo(bytes, ptrSlot);

        rom.LoadLow("synthetic.gba", bytes, code);
        return rom;
    }

    [Fact]
    public void IsEnabled_NoSwitch2Signature_ReturnsFalse()
    {
        // Zero-initialized synthetic ROM (NOT a real FE8 image) — the SUB/CMP
        // opcodes at the switch2 address are 0x00, outside the valid ranges, so
        // IsEnabled must report the editor is unavailable.
        var rom = new ROM();
        rom.LoadLow("no-switch2.gba", new byte[0x1100000], FE8U_CODE);
        Assert.False(SoundFootStepsExpandCore.IsEnabled(rom));
    }

    [Fact]
    public void IsEnabled_WithSignature_ReturnsTrue()
    {
        var rom = MakeFe8WithSwitch2(FE8U_CODE);
        Assert.True(SoundFootStepsExpandCore.IsEnabled(rom));
    }

    [Fact]
    public void IsEnabled_NullRom_ReturnsFalse()
    {
        Assert.False(SoundFootStepsExpandCore.IsEnabled(null!));
    }

    [Fact]
    public void ReadSwitch2_ReturnsStartAndCountPlusOne()
    {
        var rom = MakeFe8WithSwitch2(FE8U_CODE, start: 0, countMinusOne: 4);
        var s2 = SoundFootStepsExpandCore.ReadSwitch2(rom);
        Assert.NotNull(s2);
        Assert.Equal(0u, s2!.Value.Start);
        Assert.Equal(5u, s2.Value.TotalCount);
    }

    [Fact]
    public void ApplyFe8HardcodeFix_Fe8U_WritesAt0x78d84()
    {
        var rom = MakeFe8WithSwitch2(FE8U_CODE);
        Assert.False(rom.RomInfo.is_multibyte);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undoData = new Undo().NewUndoData("test", "fix");
            SoundFootStepsExpandCore.ApplyFe8HardcodeFix(rom, undoData);

            Assert.Equal(0x1Cu, rom.u8(0x78D84));
            Assert.Equal(0xE0u, rom.u8(0x78D85));
            Assert.NotEqual(0x1Cu, rom.u8(0x7B198));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ApplyFe8HardcodeFix_Fe8J_WritesAt0x7B198()
    {
        var rom = MakeFe8WithSwitch2(FE8J_CODE);
        Assert.True(rom.RomInfo.is_multibyte);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undoData = new Undo().NewUndoData("test", "fix");
            SoundFootStepsExpandCore.ApplyFe8HardcodeFix(rom, undoData);

            Assert.Equal(0x1Cu, rom.u8(0x7B198));
            Assert.Equal(0xE0u, rom.u8(0x7B199));
            Assert.NotEqual(0x1Cu, rom.u8(0x78D84));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void ApplyFe8HardcodeFix_NonFe8_IsNoOp()
    {
        var rom = new ROM();
        rom.LoadLow("fe6.gba", new byte[0x1100000], FE6_CODE);
        Assert.NotEqual(8, rom.RomInfo.version);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var undoData = new Undo().NewUndoData("test", "fix");
            SoundFootStepsExpandCore.ApplyFe8HardcodeFix(rom, undoData);

            Assert.Equal(0x00u, rom.u8(0x78D84));
            Assert.Equal(0x00u, rom.u8(0x7B198));
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void Expand_Fe8U_ExpandsTableAndAppliesHardcodeFix()
    {
        var rom = MakeFe8WithSwitch2(FE8U_CODE, start: 0, countMinusOne: 4);
        uint switchAddr = rom.RomInfo.sound_foot_steps_switch2_address;
        uint ptrSlot = rom.RomInfo.sound_foot_steps_pointer;
        uint defAddr = 0x08001234u;
        uint newCount = 20;

        var prevAppender = CoreState.AppendBinaryData;
        var prevServices = CoreState.Services;
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            uint appendAddr = 0x00900000u;
            CoreState.AppendBinaryData = (data, undo) =>
            {
                Array.Copy(data, 0, rom.Data, appendAddr, data.Length);
                rom.LoadLow("synthetic.gba", rom.Data, FE8U_CODE);
                CoreState.ROM = rom;
                // Return the RAW offset (write_p32 stores it as a GBA pointer);
                // matches the ItemUsagePointerCore test contract.
                return appendAddr;
            };
            CoreState.Services = null;

            var undoData = new Undo().NewUndoData("test", "expand");
            uint newAddr = SoundFootStepsExpandCore.Expand(rom, newCount, defAddr, undoData);

            Assert.NotEqual(U.NOT_FOUND, newAddr);
            Assert.Equal(appendAddr, newAddr);
            // The pointer slot now holds the new table address (GBA pointer).
            Assert.Equal(appendAddr | 0x08000000u, rom.u32(ptrSlot));
            Assert.Equal(0u, rom.u8(switchAddr + 0));
            Assert.Equal((byte)(newCount - 1), rom.u8(switchAddr + 2));
            // New (post-existing) slot filled with the default jump pointer.
            Assert.Equal(defAddr, rom.u32(appendAddr + 10 * 4));
            Assert.Equal(0x1Cu, rom.u8(0x78D84));
            Assert.Equal(0xE0u, rom.u8(0x78D85));
        }
        finally
        {
            CoreState.AppendBinaryData = prevAppender;
            CoreState.Services = prevServices;
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void Expand_AlreadyLargeEnough_ReturnsNotFound_NoHardcodeFix()
    {
        var rom = MakeFe8WithSwitch2(FE8U_CODE, start: 0, countMinusOne: 9);
        var prevAppender = CoreState.AppendBinaryData;
        var prevServices = CoreState.Services;
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            CoreState.AppendBinaryData = (data, undo) => 0x08900000u;
            CoreState.Services = null;

            var undoData = new Undo().NewUndoData("test", "expand-toosmall");
            uint result = SoundFootStepsExpandCore.Expand(rom, 3, 0x08001234u, undoData);

            Assert.Equal(U.NOT_FOUND, result);
            Assert.NotEqual(0x1Cu, rom.u8(0x78D84));
        }
        finally
        {
            CoreState.AppendBinaryData = prevAppender;
            CoreState.Services = prevServices;
            CoreState.ROM = prevRom;
        }
    }

    [Fact]
    public void Expand_NullRom_ReturnsNotFound()
    {
        var dummy = new ROM();
        dummy.LoadLow("dummy.gba", new byte[0x1100000], FE8U_CODE);
        var prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = dummy;
            var undoData = new Undo().NewUndoData("test", "null");
            Assert.Equal(U.NOT_FOUND, SoundFootStepsExpandCore.Expand(null!, 20, 0x08001234u, undoData));
        }
        finally { CoreState.ROM = prevRom; }
    }
}
