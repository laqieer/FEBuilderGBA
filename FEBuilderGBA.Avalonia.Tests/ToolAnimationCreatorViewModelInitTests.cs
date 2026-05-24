// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for ToolAnimationCreatorViewViewModel.{Init,InitFromRom,InitFromFile}
// (#500). Covers the surface that ImageMapActionAnimationView's
// "Open in Creator" button drives, plus the file-based parity path.
//
// [Collection("SharedState")] because the ROM-based tests mutate
// `CoreState.ROM`.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ToolAnimationCreatorViewModelInitTests
{
    // ----------------------------------------------------------------
    // EditableMapActionFrame — round-trips MapActionFrame, fires PropertyChanged
    // ----------------------------------------------------------------

    [Fact]
    public void EditableFrame_PropertyChanged_FiresOnWaitSetter()
    {
        var ef = new EditableMapActionFrame();
        var fired = new List<string?>();
        ef.PropertyChanged += (_, args) => fired.Add(args.PropertyName);

        ef.Wait = 7;
        Assert.Contains(nameof(EditableMapActionFrame.Wait), fired);
    }

    [Fact]
    public void EditableFrame_ToRecord_ProjectsAllFields()
    {
        var ef = new EditableMapActionFrame
        {
            Wait = 3,
            Sound = 0x42,
            ImagePointer = 0x100,
            PalettePointer = 0x200,
            ImageName = "x.png",
        };
        var rec = ef.ToRecord();
        Assert.Equal(3u, rec.Wait);
        Assert.Equal(0x42u, rec.Sound);
        Assert.Equal(0x100u, rec.ImagePointer);
        Assert.Equal(0x200u, rec.PalettePointer);
        Assert.Equal("x.png", rec.ImageName);
    }

    [Fact]
    public void EditableFrame_EmptyImageName_ToRecord_GivesNull()
    {
        // Avoid round-tripping "" through the file format — null is the
        // canonical "no image" representation.
        var ef = new EditableMapActionFrame { ImageName = "" };
        Assert.Null(ef.ToRecord().ImageName);
    }

    // ----------------------------------------------------------------
    // Initialize — standalone open path (pre-#500 behavior preserved)
    // ----------------------------------------------------------------

    [Fact]
    public void Initialize_FlipsIsLoaded()
    {
        var vm = new ToolAnimationCreatorViewViewModel();
        Assert.False(vm.IsLoaded);
        vm.Initialize();
        Assert.True(vm.IsLoaded);
    }

    // ----------------------------------------------------------------
    // InitFromFile — file-based parity
    // ----------------------------------------------------------------

    [Fact]
    public void InitFromFile_PopulatesFramesAndName()
    {
        string path = WriteTempScript(
            "//NAME=Test Anim\n" +
            "4\tframe_a.png\n" +
            "5\tframe_b.png\t0x42\n"
        );
        try
        {
            var vm = new ToolAnimationCreatorViewViewModel();
            vm.InitFromFile(AnimationTypeEnum.MapActionAnimation, 1, "hint", path);

            Assert.True(vm.IsLoaded);
            Assert.Equal(AnimationTypeEnum.MapActionAnimation, vm.AnimationKind);
            Assert.Equal(1u, vm.AnimationId);
            Assert.Equal("hint", vm.FileHint);
            Assert.Equal("Test Anim", vm.AnimationName);
            Assert.Equal(2, vm.Frames.Count);
            Assert.Equal("2", vm.FrameCount);
            Assert.Equal(4u, vm.Frames[0].Wait);
            Assert.Equal("frame_a.png", vm.Frames[0].ImageName);
            Assert.Equal(5u, vm.Frames[1].Wait);
            Assert.Equal(0x42u, vm.Frames[1].Sound);

            // SourceFilename retained so Create_Click knows where to write.
            Assert.Equal(path, vm.SourceFilename);
            // RomAddress NOT set (this is the file path).
            Assert.Equal(0u, vm.RomAddress);
            Assert.False(vm.CanWriteBackToRom);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InitFromFile_MissingFile_DoesNotThrow_AndLeavesEmptyFrames()
    {
        // Copilot CLI plan-review pt 1 — a missing temp file at Init time
        // must NOT crash the VM; it just means the user gets an empty
        // canvas. (The Core parser throws FileNotFoundException; the VM
        // catches it and logs.)
        string bogus = Path.Combine(Path.GetTempPath(),
            "no-such-file-" + Guid.NewGuid() + ".txt");

        var vm = new ToolAnimationCreatorViewViewModel();
        var ex = Record.Exception(() =>
            vm.InitFromFile(AnimationTypeEnum.MapActionAnimation, 0, "hint", bogus));
        Assert.Null(ex);

        // VM still marked loaded so the standalone-open path is a no-op.
        Assert.True(vm.IsLoaded);
        Assert.Empty(vm.Frames);
        Assert.Equal("0", vm.FrameCount);
    }

    // ----------------------------------------------------------------
    // InitFromRom — direct-from-ROM path (Map Action Animation entry point)
    // ----------------------------------------------------------------

    [Fact]
    public void InitFromRom_PopulatesFramesFromRomTable()
    {
        // Two frames + terminator at base 0x210.
        byte[] data = new byte[0x1000];
        uint baseAddr = 0x210;
        // Row 0: wait=3, sound=0x42, img=0x08001234, pal=0x080012AB
        data[baseAddr + 0x0] = 3;
        data[baseAddr + 0x2] = 0x42; data[baseAddr + 0x3] = 0x00;
        data[baseAddr + 0x4] = 0x34; data[baseAddr + 0x5] = 0x12; data[baseAddr + 0x6] = 0x00; data[baseAddr + 0x7] = 0x08;
        data[baseAddr + 0x8] = 0xAB; data[baseAddr + 0x9] = 0x12; data[baseAddr + 0xA] = 0x00; data[baseAddr + 0xB] = 0x08;
        // Row 1: wait=4, no sound, img=0x08002000, pal=0x08002030
        data[baseAddr + 0xC] = 4;
        data[baseAddr + 0x10] = 0x00; data[baseAddr + 0x11] = 0x20; data[baseAddr + 0x12] = 0x00; data[baseAddr + 0x13] = 0x08;
        data[baseAddr + 0x14] = 0x30; data[baseAddr + 0x15] = 0x20; data[baseAddr + 0x16] = 0x00; data[baseAddr + 0x17] = 0x08;
        // Terminator follows at baseAddr + 0x18 (already zero).

        var rom = new ROM();
        rom.SwapNewROMDataDirect(data);

        ROM? prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ToolAnimationCreatorViewViewModel();
            vm.InitFromRom(AnimationTypeEnum.MapActionAnimation, 0x42, "hint-ROM", baseAddr);

            Assert.True(vm.IsLoaded);
            Assert.Equal(AnimationTypeEnum.MapActionAnimation, vm.AnimationKind);
            Assert.Equal(0x42u, vm.AnimationId);
            Assert.Equal("hint-ROM", vm.FileHint);
            Assert.Equal("hint-ROM", vm.AnimationName);
            Assert.Equal(baseAddr, vm.RomAddress);
            Assert.True(vm.CanWriteBackToRom);

            Assert.Equal(2, vm.Frames.Count);
            Assert.Equal(3u, vm.Frames[0].Wait);
            Assert.Equal(0x42u, vm.Frames[0].Sound);
            Assert.Equal(4u, vm.Frames[1].Wait);
            // SourceFilename NOT set on ROM path.
            Assert.Null(vm.SourceFilename);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void InitFromRom_ZeroAddress_DoesNotPopulateFrames()
    {
        var rom = new ROM();
        rom.SwapNewROMDataDirect(new byte[0x1000]);

        ROM? prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ToolAnimationCreatorViewViewModel();
            vm.InitFromRom(AnimationTypeEnum.MapActionAnimation, 0, "hint", 0u);

            // RomAddress == 0 → CanWriteBackToRom is false.
            Assert.False(vm.CanWriteBackToRom);
            // The safety check rejects 0 — frames stays empty.
            Assert.Empty(vm.Frames);
        }
        finally { CoreState.ROM = prevRom; }
    }

    [Fact]
    public void InitFromRom_RoundTripsThroughProjectFrames()
    {
        // Confirm the editable wrappers project cleanly back through
        // ProjectFrames so Create_Click can WriteToRom the edited rows.
        byte[] data = new byte[0x1000];
        uint baseAddr = 0x210;
        data[baseAddr + 0x0] = 1;
        data[baseAddr + 0x4] = 0x00; data[baseAddr + 0x5] = 0x10; data[baseAddr + 0x6] = 0x00; data[baseAddr + 0x7] = 0x08;
        data[baseAddr + 0x8] = 0x20; data[baseAddr + 0x9] = 0x10; data[baseAddr + 0xA] = 0x00; data[baseAddr + 0xB] = 0x08;

        var rom = new ROM();
        rom.SwapNewROMDataDirect(data);

        ROM? prevRom = CoreState.ROM;
        try
        {
            CoreState.ROM = rom;
            var vm = new ToolAnimationCreatorViewViewModel();
            vm.InitFromRom(AnimationTypeEnum.MapActionAnimation, 0, "h", baseAddr);
            // Edit the wait field via the editable wrapper.
            vm.Frames[0].Wait = 99;

            var projected = vm.ProjectFrames();
            Assert.Single(projected);
            Assert.Equal(99u, projected[0].Wait);
            // Other fields preserved.
            Assert.Equal(0x00001000u, projected[0].ImagePointer);
            Assert.Equal(0x00001020u, projected[0].PalettePointer);
        }
        finally { CoreState.ROM = prevRom; }
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    static string WriteTempScript(string content)
    {
        string path = Path.Combine(Path.GetTempPath(),
            "anim-test-" + Guid.NewGuid() + ".txt");
        File.WriteAllText(path, content);
        return path;
    }
}
