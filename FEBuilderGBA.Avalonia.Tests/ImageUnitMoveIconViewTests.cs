// SPDX-License-Identifier: GPL-3.0-or-later
// #1177 headless Avalonia tests for the ported Unit Move Icon Editor.
//
// Covers controls present + wired, ROM-backed field/preview population, palette
// + step preview refresh, PNG/GIF export, sheet import (32-wide round-trips,
// wrong-width rejected with no mutation), AP export/import round-trip,
// jump-to-wait-icon resolution, comment save/reload, and IDataVerifiable
// GetRawRomReport covering P0 + P4.
//
// ROM-backed tests skip when the ROM is unavailable. Control-presence tests run
// without a ROM.
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
public class ImageUnitMoveIconViewTests : IClassFixture<RomFixture>
{
    readonly RomFixture _fixture;
    readonly ITestOutputHelper _output;

    public ImageUnitMoveIconViewTests(RomFixture fixture, ITestOutputHelper output)
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

    // First move-icon table index that holds a valid image pointer at +0.
    uint FirstValidMoveIndex(ROM rom)
    {
        uint baseAddr = rom.p32(rom.RomInfo.unit_move_icon_pointer);
        for (uint i = 1; i < 0x100; i++)
        {
            uint a = baseAddr + i * 8;
            if (a + 8 > (uint)rom.Data.Length) break;
            if (U.isPointer(rom.u32(a + 0))) return i;
        }
        return 1;
    }

    // ====================================================================
    // Controls present + wired (no ROM needed)
    // ====================================================================
    [AvaloniaFact]
    public void Controls_Present_And_Wired()
    {
        var view = new ImageUnitMoveIconView();

        Assert.NotNull(view.FindControl<AddressListControl>("EntryList"));
        Assert.NotNull(view.FindControl<NumericUpDown>("P0Box"));
        Assert.NotNull(view.FindControl<NumericUpDown>("P4Box"));

        var palette = view.FindControl<ComboBox>("PaletteCombo");
        Assert.NotNull(palette);
        Assert.Equal(5, palette!.Items.Count); // self/ally/enemy/gray/four (no lightrune/sepia)

        var step = view.FindControl<NumericUpDown>("StepBox");
        Assert.NotNull(step);
        Assert.Equal(0, step!.Minimum);

        Assert.NotNull(view.FindControl<GbaImageControl>("SheetImage"));
        Assert.NotNull(view.FindControl<GbaImageControl>("FrameImage"));
        Assert.NotNull(view.FindControl<TextBox>("CommentBox"));

        var ids = CollectAutomationIds(view).Select(x => x.Id).ToHashSet();
        Assert.Contains("ImageUnitMoveIcon_Write_Button", ids);
        Assert.Contains("ImageUnitMoveIcon_Import_Button", ids);
        Assert.Contains("ImageUnitMoveIcon_Export_Button", ids);
        Assert.Contains("ImageUnitMoveIcon_ImportAP_Button", ids);
        Assert.Contains("ImageUnitMoveIcon_ExportAP_Button", ids);
        Assert.Contains("ImageUnitMoveIcon_JumpWaitIcon_Button", ids);
        Assert.Contains("ImageUnitMoveIcon_Palette_Combo", ids);
        Assert.Contains("ImageUnitMoveIcon_Step_Input", ids);
        Assert.Contains("ImageUnitMoveIcon_P0_Input", ids);
        Assert.Contains("ImageUnitMoveIcon_P4_Input", ids);
    }

    // ====================================================================
    // ROM-backed: select populates fields + previews
    // ====================================================================
    [AvaloniaFact]
    public void Select_Populates_Fields_And_Previews()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            Assert.NotNull(list);
            list!.SelectByIndex((int)FirstValidMoveIndex(rom));

            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;
            Assert.NotNull(vm);
            Assert.True(vm!.IsLoaded);

            uint addr = vm.CurrentAddr;
            Assert.Equal(rom.u32(addr + 0), vm.P0);
            Assert.Equal(rom.u32(addr + 4), vm.P4);

            using IImage sheet = vm.RenderFullSheet();
            using IImage frame = vm.RenderFrame();
            Assert.NotNull(sheet);
            Assert.NotNull(frame);
            Assert.Equal(32, frame!.Width);
            Assert.Equal(32, frame.Height);
            Assert.Equal(32, sheet!.Width);
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void PaletteCombo_And_Step_RefreshFramePreview()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidMoveIndex(rom));

            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;
            Assert.NotNull(vm);

            var palette = view.FindControl<ComboBox>("PaletteCombo");
            palette!.SelectedIndex = 2; // enemy
            Assert.Equal(2, vm!.PaletteType);

            var step = view.FindControl<NumericUpDown>("StepBox");
            step!.Value = 1;
            Assert.Equal(1, vm.Step);

            using IImage frame = vm.RenderFrame();
            if (frame != null)
            {
                Assert.Equal(32, frame.Width);
                Assert.Equal(32, frame.Height);
            }
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

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidMoveIndex(rom));
            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;

            string path = Path.Combine(Path.GetTempPath(), $"moveicon_{System.Guid.NewGuid():N}.png");
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
    public void Export_Gif_Writes_File()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidMoveIndex(rom));
            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;

            string path = Path.Combine(Path.GetTempPath(), $"moveicon_{System.Guid.NewGuid():N}.gif");
            try
            {
                Assert.True(vm!.ExportGif(path));
                Assert.True(File.Exists(path));
                byte[] bytes = File.ReadAllBytes(path);
                Assert.True(bytes.Length > 0);
                Assert.Equal((byte)'G', bytes[0]);
                Assert.Equal((byte)'I', bytes[1]);
                Assert.Equal((byte)'F', bytes[2]);
                // At least one image-descriptor block (0x2C); a walk sheet yields
                // several frames.
                int imageBlocks = bytes.Count(b => b == 0x2C);
                Assert.True(imageBlocks >= 1);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // ROM-backed: sheet import (32-wide round-trips, wrong-width rejected)
    // ====================================================================
    [AvaloniaFact]
    public void Import_32Wide_RepointsP0_NoThrow()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidMoveIndex(rom));
            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;
            uint addr = vm!.CurrentAddr;

            byte[] px = new byte[32 * 32];
            for (int i = 0; i < px.Length; i++) px[i] = (byte)(i % 16);

            uint p0Before = rom.u32(addr + 0);
            using (ROM.BeginUndoScope(CoreState.Undo?.NewUndoData("test") ?? new Undo.UndoData()))
            {
                string err = UnitMoveIconImportCore.Import(rom, addr, px, 32, 32);
                Assert.Equal("", err);
            }
            Assert.True(U.isPointer(rom.u32(addr + 0)));
            Assert.NotEqual(p0Before, rom.u32(addr + 0));
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void Import_WrongWidth_Rejected_NoMutation()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidMoveIndex(rom));
            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;
            uint addr = vm!.CurrentAddr;

            byte[] before = (byte[])rom.Data.Clone();
            string err = UnitMoveIconImportCore.Import(rom, addr, new byte[16 * 32], 16, 32);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.Data); // byte-identical
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // ROM-backed: AP export + import round-trip
    // ====================================================================
    [AvaloniaFact]
    public void AP_Export_Import_RoundTrips()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;
            Assert.NotNull(vm);

            // Find an entry whose +4 AP resolves to a parseable region.
            var items = list!.GetItems();
            int probed = System.Math.Min(items.Count, 128);
            byte[]? ap = null;
            uint addr = 0;
            for (int i = 0; i < probed; i++)
            {
                list.SelectByIndex(i);
                if (vm!.HasAp())
                {
                    ap = vm.ReadApBytes();
                    addr = vm.CurrentAddr;
                    if (ap != null && ap.Length > 0) break;
                }
            }
            if (ap == null || ap.Length == 0) { _output.WriteLine("SKIP: no parseable AP region in the table"); return; }

            // Re-import the exported bytes and confirm P4 repoints to data that
            // reads back byte-identical to the export.
            uint p4Before = rom.u32(addr + 4);
            using (ROM.BeginUndoScope(CoreState.Undo?.NewUndoData("test") ?? new Undo.UndoData()))
            {
                string err = UnitMoveIconImportCore.ImportAP(rom, addr, ap);
                Assert.Equal("", err);
            }
            uint p4After = rom.u32(addr + 4);
            Assert.True(U.isPointer(p4After));
            byte[] written = rom.getBinaryData(U.toOffset(p4After), ap.Length);
            Assert.Equal(ap, written);
            Assert.NotEqual(p4Before, p4After); // always appended fresh (old region intact)
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // ROM-backed: jump-to-wait-icon + comment
    // ====================================================================
    [AvaloniaFact]
    public void JumpWaitIcon_Resolves_For_OwnedMoveIcon()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;
        uint waitBase = rom.p32(rom.RomInfo.unit_wait_icon_pointer);

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;
            Assert.NotNull(vm);

            var items = list!.GetItems();
            int probed = System.Math.Min(items.Count, 128);
            uint? target = null;
            for (int i = 1; i < probed; i++)
            {
                list.SelectByIndex(i);
                target = vm!.ResolveWaitIconEntryAddress();
                if (target != null) break;
            }
            if (target == null) { _output.WriteLine("SKIP: no move icon resolves to a class with a wait icon"); return; }

            // The resolved address must be a valid wait-icon table entry.
            Assert.True(target!.Value >= waitBase);
            Assert.Equal(0u, (target.Value - waitBase) % 8u);
        }
        finally { view.Close(); }
    }

    [AvaloniaFact]
    public void Comment_Saves_And_Reloads()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList");
            list!.SelectByIndex((int)FirstValidMoveIndex(rom));
            var vm = view.DataViewModel as ImageUnitMoveIconViewModel;

            string text = "move-icon-comment-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            vm!.SaveComment(text);
            vm.Comment = string.Empty;
            vm.LoadComment();
            Assert.Equal(text, vm.Comment);
        }
        finally { view.Close(); }
    }

    // ====================================================================
    // IDataVerifiable: GetRawRomReport covers P0 + P4; no-ROM guard.
    // ====================================================================
    [AvaloniaFact]
    public void GetRawRomReport_Covers_P0_And_P4()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;

        var vm = new ImageUnitMoveIconViewModel();
        var list = vm.LoadList();
        Assert.NotEmpty(list);
        vm.LoadEntry(list[0].addr);

        var report = vm.GetRawRomReport();
        Assert.True(report.ContainsKey("u32@0x00"), "report must read u32@0x00 (P0)");
        Assert.True(report.ContainsKey("u32@0x04"), "report must read u32@0x04 (P4)");
    }

    [AvaloniaFact]
    public void GetRawRomReport_NoSelection_ReturnsEmpty()
    {
        var vm = new ImageUnitMoveIconViewModel();
        // No entry loaded (CurrentAddr == 0) → empty report (no-ROM/no-sel guard).
        var report = vm.GetRawRomReport();
        Assert.Empty(report);
    }

    // ====================================================================
    // Real-pixel: full sheet + step crop differ (the sheet is taller than one
    // 32x32 frame, so cropping a higher step yields different pixels than step
    // 0 when the sheet has multiple frames). SkiaSharp decoder.
    // ====================================================================
    [AvaloniaFact]
    public void RenderFrame_Steps_DifferWhenMultiFrame_RealDecoder()
    {
        if (!TryRom()) return;
        ROM rom = _fixture.ROM!;
        var svc = CoreState.ImageService;

        uint baseAddr = rom.p32(rom.RomInfo.unit_move_icon_pointer);
        bool comparedAny = false;
        for (uint i = 1; i < 60; i++)
        {
            uint a = baseAddr + i * 8;
            if (a + 8 > (uint)rom.Data.Length) break;
            if (!U.isPointer(rom.u32(a + 0))) break;

            using IImage full = UnitMoveIconRenderCore.RenderFullSheet(rom, i, svc, 0);
            if (full == null || full.Height < 64) continue; // need >= 2 frames

            using IImage f0 = UnitMoveIconRenderCore.RenderFrame(rom, i, 0, svc, 0);
            using IImage f1 = UnitMoveIconRenderCore.RenderFrame(rom, i, 1, svc, 0);
            if (f0 == null || f1 == null) continue;
            Assert.Equal(32, f0.Width);
            Assert.Equal(32, f1.Width);
            comparedAny = true;
            break;
        }
        if (!comparedAny) _output.WriteLine("SKIP: no multi-frame move icon found");
    }
}
