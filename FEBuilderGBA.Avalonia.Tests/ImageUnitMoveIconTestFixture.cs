// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;
using FEBuilderGBA.SkiaSharp;
using FEBuilderGBA.TestFixtures;

namespace FEBuilderGBA.Avalonia.Tests;

public sealed partial class ImageUnitMoveIconDisplayTests
{
    const uint MoveBase = 0x3000;
    const uint ApBase = 0x4000;
    const uint ClassBase = 0x5000;
    const uint WaitBase = 0x5800;
    const uint ImageBase = 0x8000;
    const int Count = 16;
    const int SelectedRow = 0x0D;

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
