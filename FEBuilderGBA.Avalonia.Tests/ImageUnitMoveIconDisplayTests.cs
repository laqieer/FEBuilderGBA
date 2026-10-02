// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;
using FEBuilderGBA.TestFixtures;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public sealed class ImageUnitMoveIconDisplayTests
{
    const uint MoveBase = 0x3000;
    const uint ApBase = 0x4000;
    const uint ClassBase = 0x5000;
    const uint WaitBase = 0x5800;
    const uint ImageBase = 0x8000;
    const int Count = 16;
    const int SelectedRow = 0x0D;

    [Fact]
    public void LoadList_DisplaysOwningClassHexIds_WithoutChangingRowIdentity()
    {
        using var state = new TestState();
        ROM rom = CreateRom();
        byte[] before = SHA256.HashData(rom.Data);
        var vm = new ImageUnitMoveIconViewModel();
        var items = vm.LoadList();

        Assert.Equal(Count, items.Count);
        for (int row = 0; row < Count; row++)
        {
            Assert.Equal(MoveBase + (uint)row * 8, items[row].addr);
            Assert.Equal((uint)row, items[row].tag);
            Assert.Equal($"{row + 1:X2} {(row == SelectedRow ? "AA" : "A")}", items[row].name);
        }
        Assert.Equal("01 A", items[0].name);
        Assert.Equal("0E AA", items[SelectedRow].name);
        Assert.Equal(2u, rom.u16(ClassBase + 0x0E * rom.RomInfo.class_datasize));
        Assert.Equal("AA", NameResolver.GetClassName(0x0E));
        Assert.Equal("A", NameResolver.GetClassName(0x0D));
        Assert.Equal(0u, vm.CurrentAddr);
        Assert.Equal(0, vm.CurrentIndex);
        Assert.Equal(before, SHA256.HashData(rom.Data));
    }

    [Fact]
    public void LoadList_DoesNotAppendAnArtificialMoveIconSuffix()
    {
        using var state = new TestState();
        CreateRom();

        var items = new ImageUnitMoveIconViewModel().LoadList();

        Assert.Equal(Count, items.Count);
        Assert.All(items, item => Assert.DoesNotContain("MoveIcon", item.name));
    }

    [Fact]
    public void ReferenceList_MatchesDisplayLabels_AddressesAndTags()
    {
        using var state = new TestState();
        ROM rom = CreateRom();
        byte[] before = SHA256.HashData(rom.Data);
        var vm = new ImageUnitMoveIconViewModel();
        var items = vm.LoadList();
        var reference = ListParityHelper.BuildReferenceList("ImageUnitMoveIconView");

        Assert.NotNull(reference);
        Assert.Equal(Count, reference.Count);
        Assert.Equal(items.Select(item => (item.addr, item.name, item.tag)),
            reference.Select(item => (item.addr, item.name, item.tag)));
        var comparison = ListParityHelper.CompareLists("ImageUnitMoveIconView", items, reference);
        Assert.True(comparison.IsMatch);
        Assert.Equal(Count, comparison.TextMatches);
        Assert.Equal("01 A", reference[0].name);
        Assert.Equal("0E AA", reference[SelectedRow].name);
        Assert.Equal(before, SHA256.HashData(rom.Data));
    }

    [AvaloniaFact]
    public void SelectingDisplayed0E_KeepsRow0D_RenderApAndWaitIconIdentity()
    {
        using var state = new TestState();
        ROM rom = CreateRom();
        byte[] before = SHA256.HashData(rom.Data);
        var view = new ImageUnitMoveIconView();
        view.Show();
        try
        {
            var list = view.FindControl<AddressListControl>("EntryList")!;
            var vm = Assert.IsType<ImageUnitMoveIconViewModel>(view.DataViewModel);
            Assert.Equal(Count, list.ItemCount);
            Assert.Equal(0, vm.CurrentIndex);
            Assert.Equal(MoveBase, vm.CurrentAddr);

            list.SelectByIndex(SelectedRow);

            Assert.True(vm.IsLoaded);
            Assert.Equal(SelectedRow, vm.CurrentIndex);
            Assert.Equal(MoveBase + SelectedRow * 8, vm.CurrentAddr);
            Assert.Equal((uint)SelectedRow, list.SelectedItem!.tag);
            Assert.Equal(vm.CurrentAddr, list.SelectedItem.addr);
            Assert.Equal(U.toPointer(ImageBase + SelectedRow * 0x400), vm.P0);
            Assert.Equal(U.toPointer(ApBase + SelectedRow * 0x20), vm.P4);
            Assert.Equal("0x00003068", view.FindControl<TextBlock>("AddrLabel")!.Text);
            Assert.Equal((decimal)vm.P0, view.FindControl<NumericUpDown>("P0Box")!.Value);
            Assert.Equal((decimal)vm.P4, view.FindControl<NumericUpDown>("P4Box")!.Value);
            Assert.Equal(0, vm.Step);
            Assert.Equal(0x0Eu, ClassFormCore.GetClassMoveIcon(rom, 0x0E));
            Assert.Equal(WaitBase + SelectedRow * 8, vm.ResolveWaitIconEntryAddress());
            Assert.True(vm.HasAp());
            Assert.False(vm.IsApShared());
            Assert.Equal(ApBytes(SelectedRow), vm.ReadApBytes());

            byte[] expected = Pixels(SelectedRow);
            using IImage sheet = vm.RenderFullSheet();
            using IImage frame = vm.RenderFrame();
            Assert.NotNull(sheet);
            Assert.NotNull(frame);
            Assert.Equal((32, 64), (sheet.Width, sheet.Height));
            Assert.Equal((32, 32), (frame.Width, frame.Height));
            Assert.Equal(expected, sheet.GetPixelData());
            Assert.Equal(expected.Take(32 * 32), frame.GetPixelData());
            vm.Step = 1;
            using IImage secondFrame = vm.RenderFrame();
            Assert.NotNull(secondFrame);
            Assert.Equal(expected.Skip(32 * 32), secondFrame.GetPixelData());

            list.SelectByIndex(0);
            Assert.Equal(0, vm.CurrentIndex);
            Assert.Equal(MoveBase, vm.CurrentAddr);
            list.SelectByIndex(SelectedRow);
            Assert.Equal(SelectedRow, vm.CurrentIndex);
            Assert.Equal(0, vm.Step);
            Assert.Equal(before, SHA256.HashData(rom.Data));
            Assert.Equal("0E AA", list.SelectedItem!.name);
        }
        finally
        {
            view.Close();
        }
    }

    [Fact]
    public void SelectedRow_ExportImportAndWrite_KeepTheZeroBasedEntryAddress()
    {
        using var state = new TestState();
        ROM rom = CreateRom();
        var vm = new ImageUnitMoveIconViewModel();
        vm.LoadEntry(vm.LoadList()[SelectedRow].addr);
        uint selectedAddr = MoveBase + SelectedRow * 8;
        byte[] tableBefore = rom.getBinaryData(MoveBase, Count * 8);
        uint originalP0 = vm.P0;
        uint originalP4 = vm.P4;
        string path = Path.Combine(AppContext.BaseDirectory, $"move-icon-display-{Guid.NewGuid():N}.png");
        try
        {
            Assert.True(vm.ExportPng(path));
            using IImage exported = new SkiaImageService().LoadImage(path);
            using IImage selectedSheet = vm.RenderFullSheet();
            Assert.NotNull(selectedSheet);
            Assert.Equal((32, 64), (exported.Width, exported.Height));
            Assert.Equal(GifEncoderCore.IndexedToRgba(Pixels(SelectedRow),
                selectedSheet.GetPaletteRGBA(), 32, 64), exported.GetPixelData());
            Assert.Equal(originalP0, vm.P0);
            Assert.Equal(originalP4, vm.P4);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        byte[] importedPixels = Pixels(3);
        using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("Synthetic move icon import")))
            Assert.Equal("", UnitMoveIconImportCore.Import(rom, vm.CurrentAddr, importedPixels, 32, 64));
        vm.ReloadEntry();
        Assert.NotEqual(originalP0, vm.P0);
        Assert.Equal(originalP4, vm.P4);
        using (IImage imported = vm.RenderFullSheet())
        {
            Assert.NotNull(imported);
            Assert.Equal(importedPixels, imported.GetPixelData());
        }

        byte[] importedAp = ApBytes(0x27);
        using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("Synthetic move icon AP import")))
            Assert.Equal("", UnitMoveIconImportCore.ImportAP(rom, vm.CurrentAddr, importedAp));
        vm.ReloadEntry();
        Assert.NotEqual(originalP4, vm.P4);
        Assert.Equal(importedAp, vm.ReadApBytes());
        Assert.Equal(SelectedRow, vm.CurrentIndex);
        Assert.Equal(selectedAddr, vm.CurrentAddr);

        vm.P0 = rom.u32(MoveBase);
        vm.P4 = rom.u32(MoveBase + 4);
        using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("Synthetic move icon write")))
            vm.Write();
        Assert.Equal(vm.P0, rom.u32(selectedAddr));
        Assert.Equal(vm.P4, rom.u32(selectedAddr + 4));
        Assert.Equal(SelectedRow, vm.CurrentIndex);
        Assert.Equal($"0x{selectedAddr:X08}", vm.GetRawRomReport()["addr"]);
        Assert.Equal($"0x{vm.P0:X08}", vm.GetRawRomReport()["u32@0x00"]);
        Assert.Equal($"0x{vm.P4:X08}", vm.GetRawRomReport()["u32@0x04"]);
        for (int row = 0; row < Count; row++)
            if (row != SelectedRow)
                Assert.Equal(tableBefore.Skip(row * 8).Take(8),
                    rom.getBinaryData(MoveBase + (uint)row * 8, 8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    public void List_PreservesFirstInvalidImageTermination_And256RowCap(int count)
    {
        using var state = new TestState();
        ROM rom = CreateRom();
        using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("Synthetic table termination")))
        {
            for (uint row = 0; row <= count + 1; row++)
            {
                uint addr = MoveBase + row * 8;
                if (row == count)
                    rom.write_u32(addr, 0x12345678);
                else
                    rom.write_p32(addr, ImageBase);
                rom.write_u32(addr + 4, 0x12345678);
            }
        }

        var vm = new ImageUnitMoveIconViewModel();
        var items = vm.LoadList();
        var reference = ListParityHelper.BuildReferenceList("ImageUnitMoveIconView");
        Assert.NotNull(reference);
        Assert.Equal(count, items.Count);
        Assert.Equal(count, vm.DataCount);
        Assert.Equal(count, reference.Count);
        Assert.Equal(Enumerable.Range(0, count).Select(row => (MoveBase + (uint)row * 8, (uint)row)),
            items.Select(item => (item.addr, item.tag)));
        Assert.True(ListParityHelper.CompareLists("ImageUnitMoveIconView", items, reference).IsMatch);
    }

    [Fact]
    public void List_PreservesTheFullEightByteEntryBoundsCheck()
    {
        using var state = new TestState();
        ROM rom = CreateRom();
        uint baseAddr = (uint)rom.Data.Length - 12;
        using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("Synthetic end-of-ROM table")))
        {
            rom.write_p32(rom.RomInfo.unit_move_icon_pointer, baseAddr);
            rom.write_p32(baseAddr, ImageBase);
            rom.write_p32(baseAddr + 4, ApBase);
            rom.write_p32(baseAddr + 8, ImageBase);
        }

        var items = new ImageUnitMoveIconViewModel().LoadList();
        var item = Assert.Single(items);
        Assert.Equal(baseAddr, item.addr);
        Assert.Equal(0u, item.tag);
        var reference = ListParityHelper.BuildReferenceList("ImageUnitMoveIconView");
        Assert.NotNull(reference);
        Assert.True(ListParityHelper.CompareLists("ImageUnitMoveIconView", items, reference).IsMatch);
    }

    [Fact]
    public void LoadList_WithoutRom_PreservesTheEmptyListGuard()
    {
        using var state = new TestState();
        CoreStateTestState.ClearRom();
        Assert.Empty(new ImageUnitMoveIconViewModel().LoadList());
    }

    static ROM CreateRom()
    {
        var rom = new ROM();
        Assert.True(rom.LoadFromBytes("zipdb-proof.gba", SyntheticFe8URom.Create(), out _));
        Assert.Equal("FE8U", rom.RomInfo.VersionToFilename);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        using (ROM.BeginUndoScope(CoreState.Undo.NewUndoData("Synthetic move icon fixture")))
        {
            uint textTable = rom.p32(rom.RomInfo.text_pointer);
            rom.write_p32(textTable + 8, 0x2008);
            rom.write_u8(0x2008, 3); // Huffman AA followed by the zero leaf.
            rom.write_p32(rom.RomInfo.unit_move_icon_pointer, MoveBase);
            rom.write_p32(rom.RomInfo.class_pointer, ClassBase);
            rom.write_p32(rom.RomInfo.unit_wait_icon_pointer, WaitBase);
            for (uint row = 0; row < Count; row++)
            {
                uint addr = MoveBase + row * 8;
                uint classAddr = ClassBase + (row + 1) * rom.RomInfo.class_datasize;
                rom.write_u16(classAddr, row == SelectedRow ? 2u : 1u);
                rom.write_u8(classAddr + 4, row + 1);
                rom.write_u8(classAddr + 6, row);
                uint imageAddr = ImageBase + row * 0x400;
                byte[] compressed = LZ77.compress(ImageImportCore.EncodeDirectTiles4bpp(Pixels((int)row), 32, 64));
                for (uint i = 0; i < compressed.Length; i++)
                    rom.write_u8(imageAddr + i, compressed[i]);
                rom.write_p32(addr, imageAddr);
                byte[] ap = ApBytes((int)row);
                uint apAddr = ApBase + row * 0x20;
                for (uint i = 0; i < ap.Length; i++)
                    rom.write_u8(apAddr + i, ap[i]);
                rom.write_p32(addr + 4, apAddr);
            }
            for (uint color = 0; color < 16; color++)
                rom.write_u16(rom.RomInfo.unit_icon_palette_address + color * 2,
                    color | ((31 - color) << 5) | (color << 10));
        }
        NameResolver.ClearCache();
        RomFileService.InitializeLoadedRom(rom);
        Assert.NotNull(CoreState.FETextEncoder);
        Assert.Equal("A", FETextDecode.Direct(1));
        Assert.Equal("AA", FETextDecode.Direct(2));
        return rom;
    }

    static byte[] Pixels(int row)
    {
        byte[] pixels = new byte[32 * 64];
        Array.Fill(pixels, (byte)(row % 15 + 1), 0, 32 * 32);
        Array.Fill(pixels, (byte)((row + 1) % 15 + 1), 32 * 32, 32 * 32);
        return pixels;
    }

    static byte[] ApBytes(int marker)
    {
        byte[] ap = new byte[24];
        U.write_u16(ap, 0, 4);
        U.write_u16(ap, 2, 6);
        U.write_u16(ap, 4, 4);
        U.write_u16(ap, 6, 10);
        U.write_u16(ap, 8, 1);
        U.write_u16(ap, 10, (uint)marker);
        U.write_u16(ap, 14, (uint)marker);
        U.write_u16(ap, 16, 1);
        return ap;
    }

    sealed class TestState : IDisposable
    {
        readonly Dictionary<PropertyInfo, object?> core = typeof(CoreState)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.CanRead && property.CanWrite)
            .ToDictionary(property => property, property => property.GetValue(null));
        readonly Dictionary<PropertyInfo, object?> detection = typeof(PatchDetectionService)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite)
            .ToDictionary(property => property, property => property.GetValue(PatchDetectionService.Instance));

        public TestState()
        {
            CoreStateTestState.ClearRom();
            CoreState.CommentCache = new HeadlessEtcCache();
            CoreState.LintCache = new HeadlessEtcCache();
            CoreState.WorkSupportCache = new HeadlessEtcCache();
            CoreState.ResourceCache = null!;
            CoreState.SystemTextEncoder = null!;
            CoreState.FETextEncoder = null!;
            CoreState.TextEscape = null!;
            CoreState.FlagCache = null!;
            CoreState.ExportFunction = null!;
            CoreState.EventScript = null!;
            CoreState.ProcsScript = null!;
            CoreState.AIScript = null!;
            CoreState.Config = new Config();
            CoreState.Services = new HeadlessAppServices();
            CoreState.BaseDirectory = AppContext.BaseDirectory;
            CoreState.TextEncoding = TextEncodingEnum.Auto;
            CoreState.ImageService = new SkiaImageService();
        }

        public void Dispose()
        {
            foreach (var pair in core) pair.Key.SetValue(null, pair.Value);
            foreach (var pair in detection) pair.Key.SetValue(PatchDetectionService.Instance, pair.Value);
            NameResolver.ClearCache();
            PatchDetection.ClearAllCaches();
            MagicSplitUtil.ClearCache();
        }
    }
}
