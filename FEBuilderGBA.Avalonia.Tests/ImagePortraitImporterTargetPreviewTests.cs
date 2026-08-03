// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Portrait Import Wizard's CURRENT ROM TARGET preview pane
// (#2016) — side-by-side with the existing NEW SOURCE preview (#1981).
//
// Uses a synthetic FE8U ROM (rom.LoadLow(.., "BE8E01"), same convention as
// PortraitImportJumpTests.cs) so the run is deterministic and does not
// require a real (copyrighted) game ROM. The synthetic portrait table has
// 6 enumerated entries: ids 3-5 are the three tolerated null rows before
// the fourth consecutive null terminates the table scan.
//   id 0 — valid 32x32 LZ77 mini face + palette. DrawPortraitAutoById(0)
//          always returns null by design (id==0 short-circuit), so this
//          entry exercises the mini-fallback path exclusively.
//   id 1 — "Portrait A": valid uncompressed 96x80 (256x40 sheet) full
//          render, palette index 3 = RED.
//   id 2 — "Portrait B": the SAME sheet fill/index as id 1, but a
//          SEPARATE palette with index 3 = BLUE — proves the render reads
//          the correct PER-PORTRAIT palette rather than a stale/shared one.
//   ids 3-5 — "invalid": entries left all-zero. unitFace==0 routes
//          DrawPortraitAutoById to the class-card fallback (also fails,
//          classCard==0), and the mini path's imgPtr/palPtr are 0 (not a
//          pointer) so LoadPortraitMini fails too — both paths fail
//          gracefully, proving RefreshTargetPreview clears the pane instead
//          of throwing or showing stale data.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImagePortraitImporterTargetPreviewTests
    {
        const uint PortraitPtrSlot = 0x5524;
        const uint PortraitBase = 0x00140000;
        const uint PortraitEntrySize = 28;

        const uint MiniFaceOffset0 = 0x00150000;  // id 0 — LZ77 32x32 mini face
        const uint PaletteOffset0 = 0x00151000;

        const uint SheetOffsetA = 0x00160000;     // id 1 — "Portrait A" sheet
        const uint PaletteOffsetA = 0x00170000;
        const uint MiniFaceOffsetA = 0x00171000;

        const uint SheetOffsetB = 0x00180000;     // id 2 — "Portrait B" sheet (same fill, different palette)
        const uint PaletteOffsetB = 0x00190000;

        static uint EntryAddr(uint id) => PortraitBase + id * PortraitEntrySize;

        static byte[] SolidFillTiles(int tileCount, byte colorIndex)
        {
            byte packed = (byte)((colorIndex << 4) | colorIndex);
            byte[] data = new byte[tileCount * 32];
            for (int i = 0; i < data.Length; i++) data[i] = packed;
            return data;
        }

        static void WritePaletteColor(ROM rom, uint offset, int colorIndex, byte r, byte g, byte b)
        {
            // GBA 15-bit BGR555: bits 0-4 R, 5-9 G, 10-14 B (matches
            // IImageService.GBAColorToRGBA's documented layout).
            uint gba = (uint)((r & 0x1F) | ((g & 0x1F) << 5) | ((b & 0x1F) << 10));
            rom.write_u16(offset + (uint)(colorIndex * 2), gba);
        }

        static ROM BuildRom()
        {
            var rom = new ROM();
            rom.LoadLow("synth-fe8u-2016-target-preview.gba", new byte[0x1000000], "BE8E01");

            rom.write_p32(PortraitPtrSlot, PortraitBase + 0x08000000);

            // id 0 — mini-fallback only (full render always null for id 0).
            uint addr0 = EntryAddr(0);
            rom.write_p32(addr0 + 4, MiniFaceOffset0);
            rom.write_p32(addr0 + 8, PaletteOffset0);
            byte[] miniRaw = SolidFillTiles(16, 1);   // 4x4 tiles = 32x32
            byte[] miniComp = LZ77.compress(miniRaw);
            Array.Copy(miniComp, 0, rom.Data, MiniFaceOffset0, miniComp.Length);
            WritePaletteColor(rom, PaletteOffset0, 1, 31, 31, 31); // white

            // id 1 — "Portrait A": full 96x80 render, RED.
            uint addr1 = EntryAddr(1);
            rom.write_p32(addr1 + 0, SheetOffsetA);
            rom.write_p32(addr1 + 4, MiniFaceOffsetA);
            rom.write_p32(addr1 + 8, PaletteOffsetA);
            rom.write_u32(SheetOffsetA, 0); // 4-byte header: not LZ77 (!=0x10..), not half-body (!=0x00200400)
            byte[] sheet = SolidFillTiles(32 * 5, 3); // 256x40 uncompressed sheet, color index 3
            Array.Copy(sheet, 0, rom.Data, SheetOffsetA + 4, sheet.Length);
            byte[] miniA = LZ77.compress(SolidFillTiles(16, 3));
            Array.Copy(miniA, 0, rom.Data, MiniFaceOffsetA, miniA.Length);
            WritePaletteColor(rom, PaletteOffsetA, 3, 31, 0, 0); // red

            // id 2 — "Portrait B": SAME sheet fill/index, DIFFERENT palette (BLUE).
            uint addr2 = EntryAddr(2);
            rom.write_p32(addr2 + 0, SheetOffsetB);
            rom.write_p32(addr2 + 8, PaletteOffsetB);
            rom.write_u32(SheetOffsetB, 0);
            Array.Copy(sheet, 0, rom.Data, SheetOffsetB + 4, sheet.Length);
            WritePaletteColor(rom, PaletteOffsetB, 3, 0, 0, 31); // blue

            // id 3 — "invalid": left all-zero (no writes needed — a fresh
            // synthetic ROM byte array already defaults to zero).

            return rom;
        }

        static IDisposable UseRom(ROM rom)
        {
            ROM prev = CoreState.ROM;
            NameResolver.ClearCache();
            CoreState.ROM = rom;
            NameResolver.ClearCache();
            return new Restore(prev);
        }

        sealed class Restore : IDisposable
        {
            readonly ROM _prev;
            public Restore(ROM prev) => _prev = prev;
            public void Dispose()
            {
                CoreState.ROM = _prev;
                NameResolver.ClearCache();
            }
        }

        static void CloseView(ImagePortraitImporterView view)
        {
            var target = view.FindControl<IconPreviewControl>("TargetPreviewImage");
            var targetDisplay = target?.FindControl<Image>("ImageDisplay");
            var targetBitmap = targetDisplay?.Source as WriteableBitmap;
            target?.SetImage(null);
            if (targetBitmap != null)
                AssertWriteableBitmapDisposed(targetBitmap);

            DetachAndDispose(view.FindControl<GbaImageControl>("PreviewImage"));
            DetachAndDispose(view.FindControl<GbaImageControl>("FramePreviewImage"));
            view.Close();
            Dispatcher.UIThread.RunJobs();
        }

        static void DetachAndDispose(GbaImageControl? control)
        {
            if (control == null) return;
            var display = control.FindControl<Image>("ImageDisplay");
            Assert.NotNull(display);
            var bitmap = display.Source as WriteableBitmap;
            control.SetImage(null);
            if (bitmap != null)
            {
                bitmap.Dispose();
                AssertWriteableBitmapDisposed(bitmap);
            }
        }

        static RestoreImageService EnsureImageService()
        {
            var recorder = new RecordingImageService(new SkiaImageService());
            var prev = CoreState.ImageService;
            CoreState.ImageService = recorder;
            return new RestoreImageService(prev, recorder);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService _prev;
            public RecordingImageService Recorder { get; }
            public RestoreImageService(IImageService prev, RecordingImageService recorder)
            {
                _prev = prev;
                Recorder = recorder;
            }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        sealed class ImageSnapshot
        {
            readonly byte[] _rgba;

            public int Width { get; }
            public int Height { get; }
            public bool IsIndexed { get; }
            public byte[] Rgba => (byte[])_rgba.Clone();

            public ImageSnapshot(int width, int height, bool indexed, byte[] rgba)
            {
                Width = width;
                Height = height;
                IsIndexed = indexed;
                _rgba = (byte[])rgba.Clone();
            }
        }

        sealed class RecordingImage : IImage
        {
            readonly IImage _inner;
            readonly RecordingImageService _recorder;

            public RecordingImage(IImage inner, RecordingImageService recorder)
            {
                _inner = inner;
                _recorder = recorder;
            }

            public int Width => _inner.Width;
            public int Height => _inner.Height;
            public bool IsIndexed => _inner.IsIndexed;

            public byte[] GetPixelData()
            {
                byte[] data = _inner.GetPixelData();
                _recorder.Record(Width, Height, IsIndexed, data, _inner.GetPaletteRGBA());
                return data;
            }

            public void SetPixelData(byte[] data) => _inner.SetPixelData(data);
            public byte[] GetPaletteGBA() => _inner.GetPaletteGBA();
            public void SetPaletteGBA(byte[] gbaPalette) => _inner.SetPaletteGBA(gbaPalette);
            public byte[] GetPaletteRGBA() => _inner.GetPaletteRGBA();
            public void Save(string filePath) => _inner.Save(filePath);
            public byte[] EncodePng() => _inner.EncodePng();
            public void Dispose() => _inner.Dispose();
        }

        sealed class RecordingImageService : IImageService
        {
            readonly IImageService _inner;
            readonly List<ImageSnapshot> _snapshots = new();

            public RecordingImageService(IImageService inner) => _inner = inner;
            public ImageSnapshot? LatestSnapshot => _snapshots.LastOrDefault();

            public void Reset() => _snapshots.Clear();

            public void Record(int width, int height, bool indexed, byte[] data, byte[] palette)
            {
                int pixelCount = width > 0 && height > 0 ? width * height : 0;
                byte[] rgba = new byte[pixelCount * 4];
                if (!indexed)
                {
                    Array.Copy(data, rgba, Math.Min(data.Length, rgba.Length));
                }
                else
                {
                    for (int i = 0; i < pixelCount && i < data.Length; i++)
                    {
                        int offset = data[i] * 4;
                        if (offset + 3 >= palette.Length) continue;
                        Buffer.BlockCopy(palette, offset, rgba, i * 4, 4);
                        if (data[i] == 0) rgba[i * 4 + 3] = 0;
                    }
                }
                _snapshots.Add(new ImageSnapshot(width, height, indexed, rgba));
            }

            IImage Wrap(IImage image) => new RecordingImage(image, this);

            public IImage CreateImage(int width, int height) => Wrap(_inner.CreateImage(width, height));
            public IImage CreateIndexedImage(int width, int height, byte[] gbaPalette, int paletteColorCount)
                => Wrap(_inner.CreateIndexedImage(width, height, gbaPalette, paletteColorCount));
            public IImage LoadImage(string filePath) => Wrap(_inner.LoadImage(filePath));
            public IImage LoadImageFromBytes(byte[] pngData) => Wrap(_inner.LoadImageFromBytes(pngData));
            public void GBAColorToRGBA(ushort gbaColor, out byte r, out byte g, out byte b)
                => _inner.GBAColorToRGBA(gbaColor, out r, out g, out b);
            public ushort RGBAToGBAColor(byte r, byte g, byte b) => _inner.RGBAToGBAColor(r, g, b);
            public IImage Decode4bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette)
                => Wrap(_inner.Decode4bppTiles(tileData, offset, width, height, gbaPalette));
            public IImage Decode8bppTiles(byte[] tileData, int offset, int width, int height, byte[] gbaPalette)
                => Wrap(_inner.Decode8bppTiles(tileData, offset, width, height, gbaPalette));
            public IImage Decode8bppLinear(byte[] data, int offset, int width, int height, byte[] gbaPalette)
                => Wrap(_inner.Decode8bppLinear(data, offset, width, height, gbaPalette));
            public byte[] Encode4bppTiles(IImage image) => _inner.Encode4bppTiles(image);
            public byte[] Encode8bppTiles(IImage image) => _inner.Encode8bppTiles(image);
            public byte[] GBAPaletteToRGBA(byte[] gbaPalette, int colorCount)
                => _inner.GBAPaletteToRGBA(gbaPalette, colorCount);
            public byte[] RGBAPaletteToGBA(byte[] rgbaPalette, int colorCount)
                => _inner.RGBAPaletteToGBA(rgbaPalette, colorCount);
        }

        static IDisposable UseImportState()
        {
            IAppServices prevServices = CoreState.Services;
            Undo prevUndo = CoreState.Undo;
            CoreState.Services = new HeadlessAppServices();
            CoreState.Undo = new Undo();
            return new RestoreImportState(prevServices, prevUndo);
        }

        sealed class RestoreImportState : IDisposable
        {
            readonly IAppServices _services;
            readonly Undo _undo;
            public RestoreImportState(IAppServices services, Undo undo)
            {
                _services = services;
                _undo = undo;
            }
            public void Dispose()
            {
                CoreState.Services = _services;
                CoreState.Undo = _undo;
            }
        }

        static Image GetTargetImageDisplay(ImagePortraitImporterView view)
        {
            var target = view.FindControl<IconPreviewControl>("TargetPreviewImage");
            Assert.NotNull(target);
            var img = target!.FindControl<Image>("ImageDisplay");
            Assert.NotNull(img);
            return img!;
        }

        static Image GetSourceImageDisplay(ImagePortraitImporterView view)
        {
            var source = view.FindControl<GbaImageControl>("PreviewImage");
            Assert.NotNull(source);
            var img = source!.FindControl<Image>("ImageDisplay");
            Assert.NotNull(img);
            return img!;
        }

        sealed class TargetObservation
        {
            public WriteableBitmap Source { get; }
            public ImageSnapshot? Snapshot { get; }
            public TargetObservation(WriteableBitmap source, ImageSnapshot? snapshot)
            {
                Source = source;
                Snapshot = snapshot;
            }
        }

        static ImageSnapshot ObserveTargetRefresh(ImagePortraitImporterView view,
            RecordingImageService recorder, Action action)
        {
            var display = GetTargetImageDisplay(view);
            var observations = new List<TargetObservation>();
            EventHandler<AvaloniaPropertyChangedEventArgs> handler = (_, change) =>
            {
                if (change.Property == Image.SourceProperty
                    && change.NewValue is WriteableBitmap bitmap)
                    observations.Add(new TargetObservation(bitmap, recorder.LatestSnapshot));
            };
            display.PropertyChanged += handler;
            try
            {
                recorder.Reset();
                observations.Clear();
                action();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                display.PropertyChanged -= handler;
            }

            Assert.NotEmpty(observations);
            var final = observations.Last();
            var target = Assert.IsType<WriteableBitmap>(display.Source);
            Assert.Same(final.Source, target);
            var snapshot = Assert.IsType<ImageSnapshot>(final.Snapshot);
            Assert.Equal(snapshot.Width, target.PixelSize.Width);
            Assert.Equal(snapshot.Height, target.PixelSize.Height);
            return snapshot;
        }

        static void AssertWriteableBitmapDisposed(WriteableBitmap bitmap)
        {
            Assert.ThrowsAny<Exception>(() =>
            {
                using var framebuffer = bitmap.Lock();
            });
        }

        static void AssertBitmapDisposed(Bitmap bitmap)
        {
            Assert.ThrowsAny<Exception>(() =>
            {
                using var stream = new MemoryStream();
                bitmap.Save(stream);
            });
        }

        static void AssertFullPortraitRgba(ImageSnapshot snapshot, byte r, byte g, byte b)
        {
            const int width = 96;
            const int height = 80;
            Assert.Equal(width, snapshot.Width);
            Assert.Equal(height, snapshot.Height);
            byte[] rgba = snapshot.Rgba;
            Assert.Equal(width * height * 4, rgba.Length);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    bool opaque = y >= 48 || (x >= 16 && x < 80);
                    Assert.Equal(opaque ? r : (byte)0, rgba[i]);
                    Assert.Equal(opaque ? g : (byte)0, rgba[i + 1]);
                    Assert.Equal(opaque ? b : (byte)0, rgba[i + 2]);
                    Assert.Equal(opaque ? (byte)255 : (byte)0, rgba[i + 3]);
                }
            }
        }

        static void AssertOpaqueRgba(ImageSnapshot snapshot, int width, int height,
            byte r, byte g, byte b)
        {
            Assert.Equal(width, snapshot.Width);
            Assert.Equal(height, snapshot.Height);
            byte[] rgba = snapshot.Rgba;
            Assert.Equal(width * height * 4, rgba.Length);
            for (int i = 0; i < rgba.Length; i += 4)
            {
                Assert.Equal(r, rgba[i]);
                Assert.Equal(g, rgba[i + 1]);
                Assert.Equal(b, rgba[i + 2]);
                Assert.Equal((byte)255, rgba[i + 3]);
            }
        }

        static byte[] ReadImageRgba(IImage image)
        {
            byte[] pixels = image.GetPixelData();
            if (!image.IsIndexed) return pixels;

            byte[] palette = image.GetPaletteRGBA();
            byte[] rgba = new byte[image.Width * image.Height * 4];
            for (int i = 0; i < image.Width * image.Height && i < pixels.Length; i++)
            {
                int paletteOffset = pixels[i] * 4;
                if (paletteOffset + 3 >= palette.Length) continue;
                Buffer.BlockCopy(palette, paletteOffset, rgba, i * 4, 4);
                if (pixels[i] == 0) rgba[i * 4 + 3] = 0;
            }
            return rgba;
        }

        static List<AddressListItem> GetDisplayItems(AddressListControl list)
        {
            var listBox = list.FindControl<ListBox>("AddressList");
            Assert.NotNull(listBox);
            var items = Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<AddressListItem>>(
                listBox!.ItemsSource);
            return items.ToList();
        }

        static void InvokeLoadImageFromPath(ImagePortraitImporterView view, string path)
        {
            var mi = typeof(ImagePortraitImporterView).GetMethod("LoadImageFromPath",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(mi);
            mi!.Invoke(view, new object[] { path });
        }

        static void InvokeImportClick(ImagePortraitImporterView view)
        {
            var mi = typeof(ImagePortraitImporterView).GetMethod("Import_Click",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(mi);
            mi!.Invoke(view, new object?[] { null, new RoutedEventArgs() });
        }

        static void InvokeRefreshImportedEntry(ImagePortraitImporterView view, uint address)
        {
            var mi = typeof(ImagePortraitImporterView).GetMethod("RefreshImportedEntry",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(mi);
            mi!.Invoke(view, new object[] { address });
        }

        static void InvokeRefreshBatchImportedEntries(ImagePortraitImporterView view, params uint[] addresses)
        {
            var mi = typeof(ImagePortraitImporterView).GetMethod("RefreshBatchImportedEntries",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(mi);
            mi!.Invoke(view, new object[] { addresses });
        }

        static string WriteSynthSourcePng(byte r, byte g, byte b, int width = 16, int height = 16)
        {
            string path = Path.Combine(Path.GetTempPath(), $"wizard_target_preview_{Guid.NewGuid():N}.png");
            using (var bitmap = new global::SkiaSharp.SKBitmap(width, height))
            {
                var body = new global::SkiaSharp.SKColor(r, g, b, 255);
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        bitmap.SetPixel(x, y, body);
                var background = new global::SkiaSharp.SKColor(255, 0, 255, 255);
                bitmap.SetPixel(width - 1, 0, background);
                bitmap.SetPixel(width - 1, height - 1, background);
                bitmap.SetPixel(0, 0, background);
                using var image = global::SkiaSharp.SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(
                    global::SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, encoded.ToArray());
            }
            return path;
        }

        // #2044: Avalonia.Headless 11.2.3 allocates a fresh native buffer for
        // every Lock(). Holding both locks prevents allocator reuse and proves
        // that locks do not expose one persistent backing store. Pixel tests
        // therefore observe the managed IImage conversion boundary instead;
        // best-effort screenshot encoders use one lock and assert no pixels.
        [AvaloniaFact]
        public void HeadlessFramebuffer_RelockAllocatesDistinctLiveBuffers()
        {
            using var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96),
                PixelFormat.Rgba8888, AlphaFormat.Premul);
            using var firstLock = bitmap.Lock();
            using var secondLock = bitmap.Lock();

            Assert.NotEqual(firstLock.Address, secondLock.Address);
        }

        // ------------------------------------------------------------------
        // Distinct portraits render distinct pixels (per-slot palette read,
        // not a shared/cached render).
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void SelectingDifferentSlots_RendersDistinctTargetPixels()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (var imageService = EnsureImageService())
            {
                var recorder = imageService.Recorder;
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.Equal(6, list.ItemCount);

                    var targetImg = GetTargetImageDisplay(view);
                    ImageSnapshot snapshotA = ObserveTargetRefresh(view, recorder,
                        () => Assert.True(list.SelectAddress(EntryAddr(1))));
                    var bitmapA = Assert.IsType<WriteableBitmap>(targetImg.Source);
                    AssertFullPortraitRgba(snapshotA, 248, 0, 0);

                    ImageSnapshot snapshotB = ObserveTargetRefresh(view, recorder,
                        () => Assert.True(list.SelectAddress(EntryAddr(2))));
                    var bitmapB = Assert.IsType<WriteableBitmap>(targetImg.Source);
                    AssertFullPortraitRgba(snapshotB, 0, 0, 248);
                    Assert.NotEqual(snapshotA.Rgba, snapshotB.Rgba);
                    Assert.NotSame(bitmapA, bitmapB);
                    AssertWriteableBitmapDisposed(bitmapA);
                }
                finally { CloseView(view); }
            }
        }

        // ------------------------------------------------------------------
        // ID 0 falls back to the 32x32 mini portrait (full render always
        // fails for id 0 by design).
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void SelectingId0_FallsBackToMiniPreview()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (var imageService = EnsureImageService())
            {
                var recorder = imageService.Recorder;
                var view = new ImagePortraitImporterView();
                try
                {
                    ImageSnapshot snapshot = ObserveTargetRefresh(view, recorder, view.Show);
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.NotNull(list.SelectedItem);
                    Assert.Equal(EntryAddr(0), list.SelectedItem!.addr);
                    var bmp = Assert.IsType<WriteableBitmap>(GetTargetImageDisplay(view).Source);
                    // Mini portraits are 32x32; the full-render path is
                    // 96x80 — the size alone proves the fallback fired.
                    Assert.Equal(32, bmp.PixelSize.Width);
                    Assert.Equal(32, bmp.PixelSize.Height);
                    AssertOpaqueRgba(snapshot, 32, 32, 248, 248, 248);
                }
                finally { CloseView(view); }
            }
        }

        // ------------------------------------------------------------------
        // Tag-vs-position: a singleton list row at display/original index 0
        // whose tag is portrait id 2 must still render portrait 2.
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void TargetPreview_UsesRowTag_NotListPosition()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (var imageService = EnsureImageService())
            {
                var recorder = imageService.Recorder;
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    var target = GetTargetImageDisplay(view);
                    var initial = Assert.IsType<WriteableBitmap>(target.Source);
                    Assert.Equal(32, initial.PixelSize.Width);
                    Assert.Equal(32, initial.PixelSize.Height);
                    using (initial.Lock()) { }

                    ImageSnapshot actual = ObserveTargetRefresh(view, recorder, () =>
                        list.SetItems(new List<AddrResult>
                        {
                            new AddrResult(EntryAddr(2), "custom row", 2),
                        }));
                    Assert.NotNull(list.SelectedItem);
                    Assert.Equal((uint)2, list.SelectedItem!.tag);
                    Assert.Equal(EntryAddr(2), list.SelectedItem.addr);

                    var bmp = Assert.IsType<WriteableBitmap>(target.Source);
                    Assert.NotSame(initial, bmp);
                    AssertWriteableBitmapDisposed(initial);
                    Assert.Equal(96, bmp.PixelSize.Width);
                    Assert.Equal(80, bmp.PixelSize.Height);
                    AssertFullPortraitRgba(actual, 0, 0, 248);
                }
                finally { CloseView(view); }
            }
        }

        // ------------------------------------------------------------------
        // Changing the TARGET selection must never touch the SOURCE pane:
        // the nested Image.Source object reference must be identical before
        // and after.
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void SelectingDifferentTarget_LeavesSourceImageReferenceIdentical()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (EnsureImageService())
            {
                var view = new ImagePortraitImporterView();
                string tmpPng = WriteSynthSourcePng(10, 20, 30);
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();

                    InvokeLoadImageFromPath(view, tmpPng);

                    var sourceImg = GetSourceImageDisplay(view);
                    var sourceRefBefore = sourceImg.Source;
                    Assert.NotNull(sourceRefBefore);

                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(list.SelectAddress(EntryAddr(2)));
                    Dispatcher.UIThread.RunJobs();

                    Assert.Same(sourceRefBefore, sourceImg.Source);
                }
                finally
                {
                    CloseView(view);
                    try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
                }
            }
        }

        // ------------------------------------------------------------------
        // Deselect / filter-excludes-selection / an unrenderable slot must
        // all clear the TARGET preview (never leave a stale bitmap on screen).
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void Deselecting_ClearsTargetPreview()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (EnsureImageService())
            {
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(GetTargetImageDisplay(view).Source is WriteableBitmap);

                    list.Deselect();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Null(GetTargetImageDisplay(view).Source);
                }
                finally { CloseView(view); }
            }
        }

        [AvaloniaFact]
        public void FilterExcludingSelection_ClearsTargetPreview()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (EnsureImageService())
            {
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(GetTargetImageDisplay(view).Source is WriteableBitmap);

                    // A search term matching nothing forces the selection out
                    // from under the wizard without any explicit Deselect
                    // call — exactly the RefreshDisplay-transient-null path
                    // #2016 adds SelectedItemChanged coverage for.
                    list.ApplySearchFilter("no such row zzz");
                    Dispatcher.UIThread.RunJobs();

                    Assert.Null(GetTargetImageDisplay(view).Source);
                }
                finally { CloseView(view); }
            }
        }

        [AvaloniaFact]
        public void SelectingUnrenderableSlot_ClearsTargetPreview_NoThrow()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (EnsureImageService())
            {
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(GetTargetImageDisplay(view).Source is WriteableBitmap);

                    // id 3 is all-zero — both the full render (class-card
                    // fallback) and the mini fallback must fail gracefully.
                    Assert.True(list.SelectAddress(EntryAddr(3)));
                    Dispatcher.UIThread.RunJobs();

                    Assert.Null(GetTargetImageDisplay(view).Source);
                }
                finally { CloseView(view); }
            }
        }

        // ------------------------------------------------------------------
        // A ROM mutation (simulating a successful Import) followed by the
        // icon-preserving list refresh must keep the SAME slot selected,
        // update the TARGET preview to reflect the NEW ROM bytes, and never
        // touch the SOURCE pane.
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void PostRomMutation_TargetedRefresh_UpdatesTargetKeepsSourceAndSelection()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (var imageService = EnsureImageService())
            {
                var recorder = imageService.Recorder;
                var view = new ImagePortraitImporterView();
                string tmpPng = WriteSynthSourcePng(40, 50, 60);
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();

                    InvokeLoadImageFromPath(view, tmpPng);
                    var sourceRefBefore = GetSourceImageDisplay(view).Source;
                    Assert.NotNull(sourceRefBefore);

                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    ImageSnapshot pixelsBefore = ObserveTargetRefresh(view, recorder,
                        () => Assert.True(list.SelectAddress(EntryAddr(1))));
                    var target = GetTargetImageDisplay(view);
                    var targetBefore = Assert.IsType<WriteableBitmap>(target.Source);
                    AssertFullPortraitRgba(pixelsBefore, 248, 0, 0);

                    // Simulate what a successful Import just wrote: slot 1's
                    // palette now points at the "Portrait B" (BLUE) palette
                    // instead of its original RED one.
                    rom.write_p32(EntryAddr(1) + 8, PaletteOffsetB);

                    // Exercise the same one-row refresh seam used by the
                    // successful Import_Click path.
                    ImageSnapshot pixelsAfter = ObserveTargetRefresh(view, recorder,
                        () => InvokeRefreshImportedEntry(view, EntryAddr(1)));
                    var targetAfter = Assert.IsType<WriteableBitmap>(target.Source);

                    // Selection preserved.
                    Assert.NotNull(list.SelectedItem);
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);

                    // Target preview picked up the mutated palette (new pixels).
                    Assert.NotSame(targetBefore, targetAfter);
                    AssertWriteableBitmapDisposed(targetBefore);
                    AssertFullPortraitRgba(pixelsAfter, 0, 0, 248);
                    Assert.NotEqual(pixelsBefore.Rgba, pixelsAfter.Rgba);

                    // Source pane untouched by the refresh.
                    Assert.Same(sourceRefBefore, GetSourceImageDisplay(view).Source);
                }
                finally
                {
                    CloseView(view);
                    try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
                }
            }
        }

        [AvaloniaFact]
        public void BatchRefresh_UpdatesImportedSelectedTargetAndPreservesSourceFilterSelection()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (var imageService = EnsureImageService())
            {
                var recorder = imageService.Recorder;
                var view = new ImagePortraitImporterView();
                string tmpPng = WriteSynthSourcePng(40, 50, 60);
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();

                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    ImageSnapshot targetBefore = ObserveTargetRefresh(view, recorder,
                        () => Assert.True(list.SelectAddress(EntryAddr(1))));
                    var target = GetTargetImageDisplay(view);
                    var targetBitmapBefore = Assert.IsType<WriteableBitmap>(target.Source);
                    list.ApplySearchFilter("0x01");
                    InvokeLoadImageFromPath(view, tmpPng);

                    object sourceBefore = GetSourceImageDisplay(view).Source!;
                    object iconBefore = GetDisplayItems(list).Single().Icon!;

                    rom.write_p32(EntryAddr(1) + 8, PaletteOffsetB);
                    ImageSnapshot targetAfter = ObserveTargetRefresh(view, recorder,
                        () => InvokeRefreshBatchImportedEntries(view, EntryAddr(1)));
                    var targetBitmapAfter = Assert.IsType<WriteableBitmap>(target.Source);

                    Assert.Equal("0x01", list.FindControl<TextBox>("SearchBox")!.Text);
                    Assert.Single(GetDisplayItems(list));
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);
                    Assert.NotSame(iconBefore, GetDisplayItems(list).Single().Icon);
                    Assert.NotSame(targetBitmapBefore, targetBitmapAfter);
                    AssertWriteableBitmapDisposed(targetBitmapBefore);
                    AssertFullPortraitRgba(targetBefore, 248, 0, 0);
                    AssertFullPortraitRgba(targetAfter, 0, 0, 248);
                    Assert.NotEqual(targetBefore.Rgba, targetAfter.Rgba);
                    Assert.Same(sourceBefore, GetSourceImageDisplay(view).Source);
                }
                finally
                {
                    CloseView(view);
                    try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
                }
            }
        }

        // ------------------------------------------------------------------
        // The real Import_Click success path must refresh the written row and
        // target preview while preserving the source, selection, and filter.
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void ImportClick_Success_RefreshesIconAndTarget_PreservesSourceSelectionAndFilter()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (var imageService = EnsureImageService())
            using (UseImportState())
            {
                var recorder = imageService.Recorder;
                var view = new ImagePortraitImporterView();
                // A full 128x112 sheet rewrites D4 as well as D0/D8, so the
                // post-import list thumbnail remains mini-renderable.
                string tmpPng = WriteSynthSourcePng(0, 220, 0, 128, 112);
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    ImageSnapshot targetPixelsBefore = ObserveTargetRefresh(view, recorder,
                        () => Assert.True(list.SelectAddress(EntryAddr(1))));
                    list.ApplySearchFilter("0x01");
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);
                    Assert.Single(GetDisplayItems(list));
                    var iconBefore = Assert.IsType<Bitmap>(GetDisplayItems(list)[0].Icon);
                    using IImage thumbnailBefore = PreviewIconHelper.LoadPortraitMini(1);
                    Assert.NotNull(thumbnailBefore);
                    byte[] thumbnailPixelsBefore = ReadImageRgba(thumbnailBefore);

                    InvokeLoadImageFromPath(view, tmpPng);
                    var sourceBefore = GetSourceImageDisplay(view).Source;
                    var target = GetTargetImageDisplay(view);
                    object targetSourceBefore = Assert.IsType<WriteableBitmap>(
                        target.Source);
                    uint sheetBefore = rom.p32(EntryAddr(1));
                    uint miniBefore = rom.p32(EntryAddr(1) + 4);

                    ImageSnapshot targetPixelsAfter = ObserveTargetRefresh(view, recorder,
                        () => InvokeImportClick(view));

                    var status = view.FindControl<TextBlock>("StatusLabel");
                    Assert.NotNull(status);
                    Assert.StartsWith("Imported into ", status!.Text);
                    var searchBox = list.FindControl<TextBox>("SearchBox");
                    var listBox = list.FindControl<ListBox>("AddressList");
                    Assert.Equal("0x01", searchBox!.Text);
                    Assert.Equal(1, listBox!.ItemCount);
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);
                    var iconAfter = Assert.IsType<Bitmap>(GetDisplayItems(list).Single().Icon);
                    Assert.NotSame(iconBefore, iconAfter);
                    AssertBitmapDisposed(iconBefore);
                    using IImage thumbnailAfter = PreviewIconHelper.LoadPortraitMini(1);
                    Assert.NotNull(thumbnailAfter);
                    Assert.NotEqual(
                        thumbnailPixelsBefore, ReadImageRgba(thumbnailAfter));
                    Assert.NotEqual(sheetBefore, rom.p32(EntryAddr(1)));
                    Assert.NotEqual(miniBefore, rom.p32(EntryAddr(1) + 4));
                    byte[] miniTiles = LZ77.decompress(
                        rom.Data, rom.p32(EntryAddr(1) + 4));
                    Assert.NotNull(miniTiles);
                    Assert.Equal(32 * 32 / 2, miniTiles.Length);
                    uint paletteAfter = rom.p32(EntryAddr(1) + 8);
                    bool hasGreen = false;
                    for (uint i = 0; i < 16; i++)
                    {
                        uint color = rom.u16(paletteAfter + i * 2);
                        uint red = color & 0x1F;
                        uint green = (color >> 5) & 0x1F;
                        uint blue = (color >> 10) & 0x1F;
                        if (green >= 20 && red <= 2 && blue <= 2)
                        {
                            hasGreen = true;
                            break;
                        }
                    }
                    Assert.True(
                        hasGreen,
                        "Imported target palette should contain the source's green body color. "
                        + Convert.ToHexString(rom.getBinaryData(paletteAfter, 32)));
                    var targetAfter = Assert.IsType<WriteableBitmap>(target.Source);
                    Assert.NotSame(targetSourceBefore, targetAfter);
                    Assert.Equal(96, targetAfter.PixelSize.Width);
                    Assert.Equal(80, targetAfter.PixelSize.Height);
                    AssertWriteableBitmapDisposed((WriteableBitmap)targetSourceBefore);
                    Assert.NotEqual(targetPixelsBefore.Rgba, targetPixelsAfter.Rgba);
                    Assert.Same(sourceBefore, GetSourceImageDisplay(view).Source);
                }
                finally
                {
                    CloseView(view);
                    try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
                }
            }
        }

        // ------------------------------------------------------------------
        // A failed Import_Click must leave ROM bytes, list icons, and both
        // preview panes untouched.
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void ImportClick_Failure_DoesNotRefreshOrMutateTargetState()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (EnsureImageService())
            using (UseImportState())
            {
                var view = new ImagePortraitImporterView();
                string tmpPng = WriteSynthSourcePng(20, 200, 20);
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    Dispatcher.UIThread.RunJobs();

                    InvokeLoadImageFromPath(view, tmpPng);
                    object sourceBefore = GetSourceImageDisplay(view).Source!;
                    object targetBefore = GetTargetImageDisplay(view).Source!;
                    object iconBefore = GetDisplayItems(list)[1].Icon!;
                    uint sheetBefore = rom.p32(EntryAddr(1));

                    InvokeImportClick(view);
                    Dispatcher.UIThread.RunJobs();

                    Assert.Equal(sheetBefore, rom.p32(EntryAddr(1)));
                    Assert.Same(iconBefore, GetDisplayItems(list)[1].Icon);
                    Assert.Same(targetBefore, GetTargetImageDisplay(view).Source);
                    Assert.Same(sourceBefore, GetSourceImageDisplay(view).Source);
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);
                }
                finally
                {
                    CloseView(view);
                    try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
                }
            }
        }
    }
}
