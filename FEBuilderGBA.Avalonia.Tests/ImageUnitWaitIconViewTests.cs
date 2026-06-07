// SPDX-License-Identifier: GPL-3.0-or-later
// #991 headless Avalonia tests for the ported Unit Wait Icon Editor.
//
// Covers controls present + wired, ROM-backed field/preview population, palette
// + step preview refresh, PNG/GIF export (GIF asserts exactly 3 frames), static
// PNG import (16x48 -> b2 0 + repoint, wrong-size rejected with no mutation),
// jump-to-move-icon resolution, comment save/reload. Also runs the real-pixel
// render oracle (RenderClassWaitIcon == RenderFrame(step:0) pixel-identical)
// and per-animType crop SIZES with the real SkiaSharp decoder (Core.Tests only
// has a synthetic stub, so the real-pixel coverage lives here).
//
// ROM-backed tests skip when FE8U.gba is unavailable (matching the codebase
// pattern). Control-presence tests run without a ROM.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class ImageUnitWaitIconViewTests : IClassFixture<RomFixture>
{
    readonly RomFixture _fixture;
    readonly ITestOutputHelper _output;

    public ImageUnitWaitIconViewTests(RomFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    bool TryRom()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine("RomFixture not available — skipping ROM-backed assertions.");
            return false;
        }
        if (CoreState.ImageService == null)
            CoreState.ImageService = new SkiaImageService();
        return true;
    }

    static List<(Control Control, string Id)> CollectAutomationIds(Control root)
    {
        var result = new List<(Control, string)>();
        foreach (var child in root.GetLogicalDescendants().OfType<Control>())
        {
            var id = AutomationProperties.GetAutomationId(child);
            if (!string.IsNullOrEmpty(id)) result.Add((child, id));
        }
        return result;
    }

    // First wait-icon table index that holds a valid sprite pointer.
    uint FirstValidWaitIndex(ROM rom)
    {
        uint baseAddr = rom.p32(rom.RomInfo.unit_wait_icon_pointer);
        for (uint i = 1; i < 0x100; i++)
        {
            uint a = baseAddr + i * 8;
            if (a + 8 > (uint)rom.Data.Length) break;
            if (U.isPointer(rom.u32(a + 4))) return i;
        }
        return 1;
    }

    // ====================================================================
    // Controls present + wired (no ROM needed)
    // ====================================================================
    [AvaloniaFact]
    public void Controls_Present_And_Wired()
    {
        var view = new ImageUnitWaitIconView();

        Assert.NotNull(view.FindControl<AddressListControl>("EntryList"));
        Assert.NotNull(view.FindControl<NumericUpDown>("W0Box"));
        Assert.NotNull(view.FindControl<NumericUpDown>("W2Box"));
        Assert.NotNull(view.FindControl<NumericUpDown>("P4Box"));

        var palette = view.FindControl<ComboBox>("PaletteCombo");
        Assert.NotNull(palette);
        Assert.Equal(5, palette!.Items.Count); // 自軍/友軍/敵軍/グレー/4軍

        var step = view.FindControl<NumericUpDown>("StepBox");
        Assert.NotNull(step);
        Assert.Equal(0, step!.Minimum);
        Assert.Equal(2, step.Maximum);

        Assert.NotNull(view.FindControl<GbaImageControl>("SheetImage"));
        Assert.NotNull(view.FindControl<GbaImageControl>("FrameImage"));
        Assert.NotNull(view.FindControl<TextBox>("CommentBox"));

        // All buttons present by AutomationId.
        var ids = CollectAutomationIds(view).Select(x => x.Id).ToHashSet();
        Assert.Contains("ImageUnitWaitIcon_Write_Button", ids);
        Assert.Contains("ImageUnitWaitIcon_Import_Button", ids);
        Assert.Contains("ImageUnitWaitIcon_Export_Button", ids);
        Assert.Contains("ImageUnitWaitIcon_JumpMoveIcon_Button", ids);
        Assert.Contains("ImageUnitWaitIcon_Palette_Combo", ids);
        Assert.Contains("ImageUnitWaitIcon_Step_Input", ids);
        Assert.Contains("ImageUnitWaitIcon_W0_Input", ids);
        Assert.Contains("ImageUnitWaitIcon_W2_Input", ids);
        Assert.Contains("ImageUnitWaitIcon_P4_Input", ids);
    }

    // ====================================================================
    // ROM-backed: select populates fields + previews
    // ====================================================================
    [AvaloniaFact]
    public void Select_Populates_Fields_And_Previews()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(list);
            list!.SelectFirst();

            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;
            Assert.NotNull(vm);
            Assert.True(vm!.IsLoaded);

            // Fields reflect ROM data.
            uint addr = vm.CurrentAddr;
            Assert.Equal((ushort)rom.u16(addr + 0), vm.W0);
            Assert.Equal((ushort)rom.u16(addr + 2), vm.W2);
            Assert.Equal(rom.u32(addr + 4), vm.P4);

            // Both previews render a non-null image for a valid entry.
            using IImage sheet = vm.RenderFullSheet();
            using IImage frame = vm.RenderFrame();
            // First entry might be a placeholder; pick a known-valid entry too.
            uint idx = FirstValidWaitIndex(rom);
            list.SelectByIndex((int)idx);
            using IImage sheet2 = vm.RenderFullSheet();
            using IImage frame2 = vm.RenderFrame();
            Assert.NotNull(sheet2);
            Assert.NotNull(frame2);
            Assert.True(frame2!.Width is 16 or 32);
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void PaletteCombo_And_Step_RefreshSingleFramePreview()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            uint idx = FirstValidWaitIndex(rom);
            list!.SelectByIndex((int)idx);

            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;
            Assert.NotNull(vm);

            // Drive the palette combo + step NUD; the single-frame render must
            // still produce a valid image (palette type / step flow through).
            var palette = view.FindControl<ComboBox>("PaletteCombo");
            palette!.SelectedIndex = 2; // enemy
            Assert.Equal(2, vm!.PaletteType);

            var step = view.FindControl<NumericUpDown>("StepBox");
            step!.Value = 1;
            Assert.Equal(1, vm.Step);

            using IImage frame = vm.RenderFrame();
            // A short strip may legitimately fail step 1; just assert no throw +
            // (when produced) a sane size.
            if (frame != null)
                Assert.True(frame.Width is 16 or 32);
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // ROM-backed: export
    // ====================================================================
    [AvaloniaFact]
    public void Export_Png_Writes_File()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidWaitIndex(rom));
            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;

            string path = Path.Combine(Path.GetTempPath(), $"waiticon_{System.Guid.NewGuid():N}.png");
            try
            {
                Assert.True(vm!.ExportPng(path));
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void Export_Gif_Writes_File_With_3_Frames()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidWaitIndex(rom));
            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;

            string path = Path.Combine(Path.GetTempPath(), $"waiticon_{System.Guid.NewGuid():N}.gif");
            try
            {
                Assert.True(vm!.ExportGif(path));
                Assert.True(File.Exists(path));
                byte[] bytes = File.ReadAllBytes(path);
                Assert.True(bytes.Length > 0);
                // GIF89a header.
                Assert.Equal((byte)'G', bytes[0]);
                Assert.Equal((byte)'I', bytes[1]);
                Assert.Equal((byte)'F', bytes[2]);
                // Exactly 3 image-descriptor blocks (0x2C separators).
                int imageBlocks = bytes.Count(b => b == 0x2C);
                Assert.Equal(3, imageBlocks);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // ROM-backed: import
    // ====================================================================
    [AvaloniaFact]
    public void Import_16x48_Sets_B2_0_And_Writes_Pointer()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            uint idx = FirstValidWaitIndex(rom);
            list!.SelectByIndex((int)idx);
            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;
            uint addr = vm!.CurrentAddr;

            // Drive the Core import seam directly (the View's Import_Click uses
            // an OS file dialog we can't open headless; the seam is the unit
            // under test). 16x48 indexed → b2 0 + repoint.
            byte[] px = new byte[16 * 48];
            for (int i = 0; i < px.Length; i++) px[i] = (byte)(i % 16);

            uint p4Before = rom.u32(addr + 4);
            using (ROM.BeginUndoScope(CoreState.Undo?.NewUndoData("test") ?? new Undo.UndoData()))
            {
                string err = WaitIconImportCore.Import(rom, addr, px, 16, 48);
                Assert.Equal("", err);
            }
            Assert.Equal(0u, rom.u16(addr + 2));
            Assert.True(U.isPointer(rom.u32(addr + 4)));
            Assert.NotEqual(p4Before, rom.u32(addr + 4));
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void Import_WrongSize_Rejected_NoMutation()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidWaitIndex(rom));
            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;
            uint addr = vm!.CurrentAddr;

            byte[] before = (byte[])rom.Data.Clone();
            string err = WaitIconImportCore.Import(rom, addr, new byte[24 * 24], 24, 24);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.Data); // byte-identical
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // ROM-backed: jump-to-move-icon + comment
    // ====================================================================
    [AvaloniaFact]
    public void JumpMoveIcon_Resolves_For_OwnedWaitIcon()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        // Pick a wait-icon id that an owning class references.
        uint classBase = rom.p32(rom.RomInfo.class_pointer);
        uint classSize = rom.RomInfo.class_datasize;
        uint ownedWaitId = 0;
        for (uint c = 1; c <= 128; c++)
        {
            uint a = classBase + c * classSize;
            if (a + classSize > (uint)rom.Data.Length) break;
            uint w = rom.u8(a + 6);
            if (w > 0) { ownedWaitId = w; break; }
        }
        if (ownedWaitId == 0) { _output.WriteLine("SKIP: no owned wait icon"); return; }

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)ownedWaitId);
            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;

            uint? moveIcon = vm!.ResolveMoveIconForSelection();
            Assert.NotNull(moveIcon);

            // The move-icon id is 1-BASED; the navigation target must be the
            // 0-based table entry: baseAddr + (id - 1) * 8 (Copilot review on
            // PR #993 — the off-by-one fix). id 0 ("no move icon") -> null.
            uint moveBase = rom.p32(rom.RomInfo.unit_move_icon_pointer);
            uint? target = vm.ResolveMoveIconEntryAddress();
            if (moveIcon!.Value == 0)
            {
                Assert.Null(target);
            }
            else
            {
                Assert.NotNull(target);
                uint expected = moveBase + (moveIcon.Value - 1) * 8;
                Assert.Equal(expected, target!.Value);
                // The resolved row must NOT be the naive (id * 8) 0-based read —
                // regression-fires if the off-by-one returns.
                Assert.NotEqual(moveBase + moveIcon.Value * 8, target.Value);
            }
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void ResolveMoveIconEntryAddress_IsOneBased_OffByOneRegression()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        // Find a wait icon whose RESOLVED owning class has a move-icon id >= 1
        // (derive everything from the VM resolution so the off-by-one assertion
        // uses the SAME class the View jumps to — first-match resolution may not
        // be the class we scanned).
        uint classBase = rom.p32(rom.RomInfo.class_pointer);
        uint classSize = rom.RomInfo.class_datasize;
        uint moveBase = rom.p32(rom.RomInfo.unit_move_icon_pointer);

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;
            Assert.NotNull(vm);

            var items = list!.GetItems();
            int probed = System.Math.Min(items.Count, 128);
            bool checkedAny = false;
            for (int i = 1; i < probed; i++)
            {
                list.SelectByIndex(i);
                uint? moveId = vm!.ResolveMoveIconForSelection();
                if (moveId == null || moveId.Value == 0) continue;

                uint? target = vm.ResolveMoveIconEntryAddress();
                Assert.NotNull(target);
                Assert.Equal(moveBase + (moveId.Value - 1) * 8, target!.Value);
                // Off-by-one regression guard: must NOT be the naive id*8 row.
                Assert.NotEqual(moveBase + moveId.Value * 8, target.Value);
                checkedAny = true;
                break;
            }
            if (!checkedAny) _output.WriteLine("SKIP: no wait icon resolved to a class with move id >= 1");
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void Comment_Saves_And_Reloads()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitWaitIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidWaitIndex(rom));
            var vm = view.DataViewModel as ImageUnitWaitIconViewModel;

            string text = "wait-icon-comment-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            vm!.SaveComment(text);
            vm.Comment = string.Empty;
            vm.LoadComment();
            Assert.Equal(text, vm.Comment);
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // Real-pixel render oracle (SkiaSharp decoder) — proves the extracted
    // WaitIconRenderCore is pixel-identical to the step-0 self render the
    // delegating PreviewIconHelper relies on, and per-animType crop sizes.
    // ====================================================================
    [AvaloniaFact]
    public void RenderClassWaitIcon_PixelEquals_RenderFrameStep0_RealDecoder()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;
        var svc = CoreState.ImageService;

        int compared = 0;
        for (uint idx = 1; idx < 60; idx++)
        {
            using IImage a = WaitIconRenderCore.RenderClassWaitIcon(rom, idx, svc, 0);
            using IImage b = WaitIconRenderCore.RenderFrame(rom, idx, 0, svc, 0);
            if (a == null && b == null) continue;
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a!.Width, b!.Width);
            Assert.Equal(a.Height, b.Height);
            Assert.Equal(a.GetPixelData(), b.GetPixelData());     // REAL pixels
            Assert.Equal(a.GetPaletteRGBA(), b.GetPaletteRGBA());
            compared++;
        }
        Assert.True(compared > 0, "At least one wait icon should have been compared.");
    }

    // ====================================================================
    // List termination (documented residual): the shared list-stop is the
    // first non-pointer @+4 (ListParityHelper + sibling editors). WF accepts
    // row 0 unconditionally and treats `P4==0 && flags!=0` as non-terminal;
    // this PR keeps the shared contract. This test asserts the CURRENT vanilla
    // behavior so a future change to the shared stop logic is visible.
    // ====================================================================
    [AvaloniaFact]
    public void List_StopsAtFirstNonPointerP4_VanillaBehavior()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var vm = new ImageUnitWaitIconViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);

        // Every listed row must have a pointer at +4 (the stop condition).
        foreach (var row in list)
            Assert.True(U.isPointer(rom.u32(row.addr + 4)),
                $"Listed wait-icon row 0x{row.addr:X08} must have a pointer at +4.");

        // The row immediately AFTER the last listed one is the terminator:
        // its +4 is NOT a pointer (documents the shared first-non-pointer stop).
        uint baseAddr = rom.p32(rom.RomInfo.unit_wait_icon_pointer);
        uint termAddr = baseAddr + (uint)list.Count * 8;
        if (termAddr + 8 <= (uint)rom.Data.Length)
            Assert.False(U.isPointer(rom.u32(termAddr + 4)),
                "The first unlisted row's +4 must be a non-pointer terminator.");
    }

    [AvaloniaTheory]
    [InlineData((byte)0, 16, 16)]
    [InlineData((byte)1, 16, 24)]
    [InlineData((byte)2, 32, 32)]
    public void RenderFrame_Step0_AnimType_HasExpectedSize_RealDecoder(byte animType, int w, int h)
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;
        var svc = CoreState.ImageService;

        uint baseAddr = rom.p32(rom.RomInfo.unit_wait_icon_pointer);
        uint? found = null;
        for (uint i = 1; i < 0x100; i++)
        {
            uint a = baseAddr + i * 8;
            if (a + 8 > (uint)rom.Data.Length) break;
            if (!U.isPointer(rom.u32(a + 4))) break;
            if ((byte)rom.u8(a + 2) == animType) { found = i; break; }
        }
        if (found == null) { _output.WriteLine($"SKIP: no wait icon with animType {animType}"); return; }

        using IImage img = WaitIconRenderCore.RenderFrame(rom, found.Value, 0, svc, 0);
        Assert.NotNull(img);
        Assert.Equal(w, img!.Width);
        Assert.Equal(h, img.Height);
    }
}
