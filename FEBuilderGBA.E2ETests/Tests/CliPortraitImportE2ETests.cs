using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FEBuilderGBA.E2ETests.Helpers;
using Xunit;

namespace FEBuilderGBA.E2ETests.Tests;

public class CliPortraitImportE2ETests
{
    const int CanonicalFe8Length = 0x01000000;
    const int MaxRomLength = 0x02000000;
    const int PortraitTable = 0x1000;
    const int PortraitEntrySize = 28;
    const int BattleStart = 0x00801C14;
    const int BattleLength = 0xB44;

    static string CliExe => AppRunner.FindCliExePath();

    [Fact]
    public void ImportPortrait_CanonicalFe8_PreservesBattleRegion()
    {
        string dir = NewTempDir();
        try
        {
            string romPath = Path.Combine(dir, "single.gba");
            string pngPath = Path.Combine(dir, "portrait.png");
            File.WriteAllBytes(romPath, BuildSyntheticFe8Rom(
                CanonicalFe8Length, portraitCount: 1));
            WritePortraitPng(pngPath);
            byte[] before = File.ReadAllBytes(romPath);
            Assert.Equal(
                0x00802178,
                FindLegacyFreeRun(before, 0x00800000, 32));

            var result = AppRunner.Run(
                CliExe,
                $"--import-portrait --rom=\"{romPath}\" --portrait-id=0 "
                + $"--in=\"{pngPath}\" --force-version=FE8U",
                timeoutMs: 60_000);

            Assert.Equal(0, result.ExitCode);
            byte[] after = File.ReadAllBytes(romPath);
            Assert.Equal(
                Slice(before, BattleStart, BattleLength),
                Slice(after, BattleStart, BattleLength));
            uint d0 = ReadPointer(after, PortraitTable);
            uint d8 = ReadPointer(after, PortraitTable + 8);
            Assert.True(d0 >= CanonicalFe8Length);
            Assert.True(d8 > d0);
            Assert.Equal(0x00100400u, BitConverter.ToUInt32(after, (int)d0));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(1073741824u)]
    public void ImportPortrait_OutOfTableOrWrappingId_IsRejectedWithoutChangingRom(
        uint portraitId)
    {
        string dir = NewTempDir();
        try
        {
            string romPath = Path.Combine(dir, "wrapped-id.gba");
            string pngPath = Path.Combine(dir, "portrait.png");
            byte[] before = BuildSyntheticFe8Rom(
                CanonicalFe8Length, portraitCount: 1);
            Array.Clear(
                before,
                PortraitTable + PortraitEntrySize,
                PortraitEntrySize * 4);
            File.WriteAllBytes(romPath, before);
            WritePortraitPng(pngPath);

            var result = AppRunner.Run(
                CliExe,
                $"--import-portrait --rom=\"{romPath}\" --portrait-id={portraitId} "
                + $"--in=\"{pngPath}\" --force-version=FE8U",
                timeoutMs: 60_000);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("out of range", result.Stderr);
            Assert.Equal(before, File.ReadAllBytes(romPath));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void ImportPortraitAll_SecondPaletteFailure_DoesNotPoisonLaterSuccess()
    {
        string dir = NewTempDir();
        try
        {
            string pngPath = Path.Combine(dir, "portrait.png");
            WritePortraitPng(pngPath);
            string smallPngPath = Path.Combine(dir, "small.png");
            WritePortraitPng(smallPngPath, 8, 8);

            string baselinePath = Path.Combine(dir, "baseline.gba");
            File.WriteAllBytes(baselinePath, BuildSyntheticFe8Rom(
                CanonicalFe8Length, portraitCount: 1));
            var baselineResult = AppRunner.Run(
                CliExe,
                $"--import-portrait --rom=\"{baselinePath}\" --portrait-id=0 "
                + $"--in=\"{pngPath}\" --force-version=FE8U",
                timeoutMs: 60_000);
            Assert.Equal(0, baselineResult.ExitCode);
            byte[] baselineAfter = File.ReadAllBytes(baselinePath);
            uint baselineD0 = ReadPointer(baselineAfter, PortraitTable);
            uint baselineD8 = ReadPointer(baselineAfter, PortraitTable + 8);
            int tileAppend = checked((int)(baselineD8 - baselineD0));
            int itemAppend = baselineAfter.Length - CanonicalFe8Length;
            Assert.True(tileAppend > 0);
            Assert.True(itemAppend > tileAppend);

            string smallBaselinePath = Path.Combine(dir, "small-baseline.gba");
            File.WriteAllBytes(smallBaselinePath, BuildSyntheticFe8Rom(
                CanonicalFe8Length, portraitCount: 1));
            var smallBaselineResult = AppRunner.Run(
                CliExe,
                $"--import-portrait --rom=\"{smallBaselinePath}\" --portrait-id=0 "
                + $"--in=\"{smallPngPath}\" --force-version=FE8U",
                timeoutMs: 60_000);
            Assert.Equal(0, smallBaselineResult.ExitCode);
            int smallItemAppend =
                File.ReadAllBytes(smallBaselinePath).Length - CanonicalFe8Length;
            Assert.InRange(smallItemAppend, 1, tileAppend + 16);

            int initialLength = MaxRomLength
                - itemAppend
                - tileAppend
                - 17;
            Assert.NotEqual(0, initialLength % 4);
            string batchRom = Path.Combine(dir, "batch.gba");
            byte[] batchBefore = BuildSyntheticFe8Rom(
                initialLength, portraitCount: 3);
            File.WriteAllBytes(batchRom, batchBefore);

            string batchDir = Path.Combine(dir, "batch");
            Directory.CreateDirectory(batchDir);
            File.Copy(pngPath, Path.Combine(batchDir, "0.png"));
            File.Copy(pngPath, Path.Combine(batchDir, "1.png"));
            File.Copy(smallPngPath, Path.Combine(batchDir, "2.png"));

            var result = AppRunner.Run(
                CliExe,
                $"--import-portrait-all --rom=\"{batchRom}\" "
                + $"--dir=\"{batchDir}\" --force-version=FE8U",
                timeoutMs: 120_000);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Imported portrait 0", result.Stdout);
            Assert.Contains("Imported portrait 2", result.Stdout);
            Assert.Contains("Skip 1.png", result.Stderr);

            byte[] after = File.ReadAllBytes(batchRom);
            Assert.Equal(
                initialLength + 1 + itemAppend + smallItemAppend,
                after.Length);
            Assert.NotEqual(
                ReadPointer(batchBefore, PortraitTable),
                ReadPointer(after, PortraitTable));
            Assert.NotEqual(
                ReadPointer(batchBefore, PortraitTable + 8),
                ReadPointer(after, PortraitTable + 8));
            Assert.Equal(
                ReadPointer(batchBefore, PortraitTable + PortraitEntrySize),
                ReadPointer(after, PortraitTable + PortraitEntrySize));
            Assert.Equal(
                ReadPointer(batchBefore, PortraitTable + PortraitEntrySize + 8),
                ReadPointer(after, PortraitTable + PortraitEntrySize + 8));
            Assert.NotEqual(
                ReadPointer(batchBefore, PortraitTable + PortraitEntrySize * 2),
                ReadPointer(after, PortraitTable + PortraitEntrySize * 2));
            Assert.NotEqual(
                ReadPointer(batchBefore, PortraitTable + PortraitEntrySize * 2 + 8),
                ReadPointer(after, PortraitTable + PortraitEntrySize * 2 + 8));
            Assert.Equal(
                Slice(batchBefore, BattleStart, BattleLength),
                Slice(after, BattleStart, BattleLength));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    static byte[] BuildSyntheticFe8Rom(int length, int portraitCount)
    {
        byte[] data = new byte[length];
        Array.Fill(data, (byte)0xAA);
        data[0xAC] = (byte)'B';
        data[0xAD] = (byte)'E';
        data[0xAE] = (byte)'8';
        data[0xAF] = (byte)'E';
        data[0xB0] = (byte)'0';
        data[0xB1] = (byte)'1';
        WritePointer(data, 0x5524, PortraitTable);

        for (int id = 0; id < portraitCount; id++)
        {
            int entry = PortraitTable + id * PortraitEntrySize;
            Array.Clear(data, entry, PortraitEntrySize);
            int oldFace = 0x2000 + id * 0x100;
            WritePointer(data, entry, oldFace);
            WritePointer(data, entry + 8, 0x00802238 + id * 0x40);
            BitConverter.GetBytes(0x00100400u).CopyTo(data, oldFace);
        }

        for (int i = 0; i < BattleLength; i++)
            data[BattleStart + i] = (byte)((i * 37 + 11) & 0xFF);
        Array.Fill(data, (byte)0x00, 0x00802178, 0x40);
        return data;
    }

    static void WritePortraitPng(string path, int width = 96, int height = 80)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        Color[] colors =
        {
            Color.Transparent,
            Color.FromArgb(255, 248, 0, 0),
            Color.FromArgb(255, 0, 248, 0),
            Color.FromArgb(255, 0, 0, 248),
        };
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                bitmap.SetPixel(x, y, colors[(x / 8 + y / 8) % colors.Length]);
        bitmap.Save(path, ImageFormat.Png);
    }

    static uint ReadPointer(byte[] data, int offset)
        => BitConverter.ToUInt32(data, offset) - 0x08000000u;

    static void WritePointer(byte[] data, int offset, int target)
        => BitConverter.GetBytes((uint)target + 0x08000000u)
            .CopyTo(data, offset);

    static byte[] Slice(byte[] data, int offset, int length)
    {
        byte[] result = new byte[length];
        Array.Copy(data, offset, result, 0, length);
        return result;
    }

    static int FindLegacyFreeRun(byte[] data, int start, int size)
    {
        int limit = data.Length - size;
        for (int candidate = (start + 3) & ~3; candidate < limit; candidate += 4)
        {
            byte fill = data[candidate];
            if (fill != 0x00 && fill != 0xFF) continue;
            bool match = true;
            for (int i = 1; i < size; i++)
            {
                if (data[candidate + i] == fill) continue;
                match = false;
                break;
            }
            if (match) return candidate;
        }
        return -1;
    }

    static string NewTempDir()
    {
        string path = Path.Combine(
            Path.GetTempPath(), "febuilder_portrait_2032_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }
}
