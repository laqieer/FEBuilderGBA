using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class PortraitImportBattleScreenRegressionTests
{
    [Fact]
    public void ImportSheet_CanonicalFe8_PreservesBattleScreenBytes_AndUndoRestoresRom()
    {
        ROM? previousRom = CoreState.ROM;
        Undo? previousUndo = CoreState.Undo;
        IImageService? previousImageService = CoreState.ImageService;
        try
        {
            byte[] data = new byte[0x01000000];
            Array.Fill(data, (byte)0xAA);
            var rom = new ROM();
            Assert.True(rom.LoadForceVersionFromBytes(
                "synthetic-fe8u.gba", data, "FE8U"));
            CoreState.ROM = rom;
            CoreState.Undo = new Undo();
            CoreState.ImageService = new SkiaImageService();

            const uint entryAddr = 0x1000;
            const uint oldFaceAddr = 0x200000;
            const uint liveD4Addr = 0x00802178;
            const uint liveD8Addr = 0x00802238;
            const uint liveD12Addr = 0x00802300;
            const uint battleStart = 0x00801C14;
            const uint battleEnd = 0x00802758;

            rom.write_fill(battleStart, battleEnd - battleStart, 0x00);
            rom.write_p32(rom.RomInfo.battle_screen_TSA1_pointer, 0x0080210C);
            rom.write_p32(rom.RomInfo.battle_screen_TSA2_pointer, 0x008021C0);
            rom.write_p32(rom.RomInfo.battle_screen_TSA3_pointer, 0x00802274);
            rom.write_p32(rom.RomInfo.battle_screen_TSA4_pointer, 0x00802348);
            rom.write_p32(rom.RomInfo.battle_screen_TSA5_pointer, 0x00802508);
            rom.write_p32(rom.RomInfo.battle_screen_palette_pointer, 0x00802558);

            byte[] oldFace = Enumerable.Repeat((byte)0x5A, 0x80).ToArray();
            oldFace[0] = 0x00;
            oldFace[1] = 0x04;
            oldFace[2] = 0x10;
            oldFace[3] = 0x00;
            byte[] oldMini = LZ77.compress(
                Enumerable.Range(0, 32).Select(i => (byte)(0x40 + i)).ToArray());
            rom.write_range(oldFaceAddr, oldFace);
            rom.write_range(liveD4Addr, oldMini);
            rom.write_p32(entryAddr, oldFaceAddr);
            rom.write_p32(entryAddr + 4, liveD4Addr);
            rom.write_p32(entryAddr + 8, liveD8Addr);
            rom.write_p32(entryAddr + 12, liveD12Addr);

            uint legacyCandidate = rom.FindFreeSpace(
                0x00800000, U.Padding4((uint)oldMini.Length));
            Assert.InRange(legacyCandidate, battleStart, battleEnd - 1);

            byte[] before = rom.Data.ToArray();
            byte[] battleBefore = rom.getBinaryData(
                battleStart, battleEnd - battleStart);
            var loadResult = MakeSheetLoadResult();

            var outcome = PortraitImportHelper.ImportSheet(
                rom,
                entryAddr,
                loadResult,
                new UndoService(),
                PortraitPaletteMode.CustomPalette,
                loadResult.GBAPalette,
                false);

            Assert.True(outcome.Success, outcome.Error);
            Assert.Equal(
                battleBefore,
                rom.getBinaryData(battleStart, battleEnd - battleStart));
            Assert.Equal(oldFace, rom.getBinaryData(oldFaceAddr, (uint)oldFace.Length));

            uint[] pointers =
            {
                rom.p32(entryAddr),
                rom.p32(entryAddr + 4),
                rom.p32(entryAddr + 8),
                rom.p32(entryAddr + 12),
            };
            Assert.Equal(4, pointers.Distinct().Count());
            Assert.All(pointers, pointer => Assert.True(
                pointer >= 0x01000000,
                $"Portrait payload 0x{pointer:X8} must be in expansion space."));
            Assert.True(LZ77.iscompress(rom.Data, pointers[0]),
                "A below-floor raw D0 target must not be inspected to preserve raw format.");
            uint d0Size = U.Padding4(LZ77.getCompressedSize(rom.Data, pointers[0]));
            uint d4Size = U.Padding4(LZ77.getCompressedSize(rom.Data, pointers[1]));
            (uint Start, uint End)[] ranges =
            {
                (pointers[0], pointers[0] + d0Size),
                (pointers[1], pointers[1] + d4Size),
                (pointers[2], pointers[2] + 32),
                (pointers[3], pointers[3] + 1536),
            };
            Assert.All(ranges, range =>
                Assert.True(range.End <= (uint)rom.Data.Length));
            for (int i = 0; i < ranges.Length; i++)
            {
                for (int j = i + 1; j < ranges.Length; j++)
                {
                    Assert.True(
                        ranges[i].End <= ranges[j].Start
                        || ranges[j].End <= ranges[i].Start,
                        $"Portrait payload ranges {i} and {j} overlap.");
                }
            }

            CoreState.Undo.RunUndo();
            Assert.Equal(before, rom.Data);
        }
        finally
        {
            CoreState.ROM = previousRom;
            CoreState.Undo = previousUndo;
            CoreState.ImageService = previousImageService;
        }
    }

    [Fact]
    public void PaletteValidation_AppendWriter_RestoresExactBytesAndUnalignedLength()
    {
        byte[] data = new byte[0x8001];
        Array.Fill(data, (byte)0xAA);
        var rom = new ROM();
        Assert.True(rom.LoadLow("synthetic-palette.gba", data, "NAZO"));
        const uint pointerAddr = 0x200;
        const uint paletteAddr = 0x1000;
        rom.write_p32(pointerAddr, paletteAddr);
        byte[] palette = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        rom.write_range(paletteAddr, palette);
        byte[] before = rom.Data.ToArray();

        var descriptor = new ImageImportValidator.PaletteEditorDescriptor
        {
            Name = "PortraitPaletteRestore",
            LoadList = () => new System.Collections.Generic.List<AddrResult>
            {
                new(pointerAddr, "Portrait", 0),
            },
            LoadItem = _ => { },
            ExportPalette = () => rom.getBinaryData(rom.p32(pointerAddr), 32),
            ImportPalette = pal =>
            {
                uint written = ImageImportCore.WritePortraitPaletteToROM(
                    rom, pal, pointerAddr);
                return written == U.NOT_FOUND ? "write failed" : null;
            },
        };

        ImageImportValidator.ValidationResult result =
            ImageImportValidator.ValidatePaletteEditor(descriptor, rom);

        Assert.NotNull(result);
        Assert.True(result.Success, result.Error);
        Assert.Equal(before.Length, rom.Data.Length);
        Assert.Equal(before, rom.Data);
    }

    [AvaloniaFact]
    public void PortraitViews_ConstructWithSafeWriterRouting()
    {
        Assert.NotNull(new ImagePortraitView());
        Assert.NotNull(new ImagePortraitFE6View());
        Assert.NotNull(new PortraitViewerView());
    }

    static ImageImportService.LoadResult MakeSheetLoadResult()
    {
        const int width = 128;
        const int height = 112;
        byte[] palette = new byte[32];
        ushort[] colors = { 0x0000, 0x001F, 0x03E0, 0x7C00 };
        for (int i = 0; i < colors.Length; i++)
        {
            palette[i * 2] = (byte)colors[i];
            palette[i * 2 + 1] = (byte)(colors[i] >> 8);
        }

        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int index = (i / 8) % 4;
            rgba[i * 4] = index == 1 ? (byte)248 : (byte)0;
            rgba[i * 4 + 1] = index == 2 ? (byte)248 : (byte)0;
            rgba[i * 4 + 2] = index == 3 ? (byte)248 : (byte)0;
            rgba[i * 4 + 3] = 255;
        }

        return new ImageImportService.LoadResult
        {
            Success = true,
            Width = width,
            Height = height,
            RGBAPixels = rgba,
            IndexedPixels = new byte[width * height],
            GBAPalette = palette,
            SourcePath = "synthetic-128x112.png",
        };
    }
}
