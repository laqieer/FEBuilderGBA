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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
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
            CoreState.ROM = rom;
            return new Restore(prev);
        }

        sealed class Restore : IDisposable
        {
            readonly ROM _prev;
            public Restore(ROM prev) => _prev = prev;
            public void Dispose() => CoreState.ROM = _prev;
        }

        static IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            CoreState.ImageService = new SkiaImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService _prev;
            public RestoreImageService(IImageService prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
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

        static byte[] ReadPixels(WriteableBitmap bmp)
        {
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            int stride = w * 4;
            byte[] buf = new byte[stride * h];
            using (var fb = bmp.Lock())
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(IntPtr.Add(fb.Address, y * fb.RowBytes), buf, y * stride, stride);
            }
            return buf;
        }

        static byte[] FirstOpaquePixel(WriteableBitmap bmp)
        {
            byte[] pixels = ReadPixels(bmp);
            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                if (pixels[i + 3] != 0)
                    return new[] { pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3] };
            }
            Assert.Fail("Expected at least one opaque preview pixel.");
            return Array.Empty<byte>();
        }

        static System.Collections.Generic.List<AddressListItem> GetDisplayItems(AddressListControl list)
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

        static string WriteSynthSourcePng(byte r, byte g, byte b, int width = 16, int height = 16)
        {
            string path = Path.Combine(Path.GetTempPath(), $"wizard_target_preview_{Guid.NewGuid():N}.png");
            using (IImage synth = CoreState.ImageService.CreateImage(width, height))
            {
                byte[] rgba = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    rgba[i * 4 + 0] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 255;
                }
                // Portrait import color-keys the top-left pixel. Keep it
                // distinct so the requested body color remains opaque.
                rgba[0] = 255; rgba[1] = 0; rgba[2] = 255; rgba[3] = 255;
                synth.SetPixelData(rgba);
                synth.Save(path);
            }
            return path;
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
            using (EnsureImageService())
            {
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.Equal(6, list.ItemCount);

                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    Dispatcher.UIThread.RunJobs();
                    var targetImg = GetTargetImageDisplay(view);
                    var bitmapA = Assert.IsType<WriteableBitmap>(targetImg.Source);
                    byte[] pixelsA = ReadPixels(bitmapA);
                    FirstOpaquePixel(bitmapA);

                    Assert.True(list.SelectAddress(EntryAddr(2)));
                    Dispatcher.UIThread.RunJobs();
                    var bitmapB = Assert.IsType<WriteableBitmap>(targetImg.Source);
                    byte[] pixelsB = ReadPixels(bitmapB);
                    FirstOpaquePixel(bitmapB);

                    Assert.NotEqual(pixelsA, pixelsB);
                }
                finally { view.Close(); }
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
            using (EnsureImageService())
            {
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;

                    Assert.True(list.SelectAddress(EntryAddr(0)));
                    Dispatcher.UIThread.RunJobs();

                    var bmp = Assert.IsType<WriteableBitmap>(GetTargetImageDisplay(view).Source);
                    // Mini portraits are 32x32; the full-render path is
                    // 96x80 — the size alone proves the fallback fired.
                    Assert.Equal(32, bmp.PixelSize.Width);
                    Assert.Equal(32, bmp.PixelSize.Height);
                    using IImage expectedImage = PreviewIconHelper.LoadPortraitMini(0);
                    using WriteableBitmap expected = IconBitmapBuilder.FromImage(expectedImage)!;
                    Assert.Equal(ReadPixels(expected), ReadPixels(bmp));
                }
                finally { view.Close(); }
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
            using (EnsureImageService())
            {
                var view = new ImagePortraitImporterView();
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.True(list.SelectAddress(EntryAddr(2)));
                    Dispatcher.UIThread.RunJobs();
                    byte[] expected = ReadPixels(
                        Assert.IsType<WriteableBitmap>(GetTargetImageDisplay(view).Source));

                    list.SetItems(new System.Collections.Generic.List<AddrResult>
                    {
                        new AddrResult(EntryAddr(2), "custom row", 2),
                    });
                    Dispatcher.UIThread.RunJobs();
                    var bmp = Assert.IsType<WriteableBitmap>(GetTargetImageDisplay(view).Source);
                    Assert.Equal(96, bmp.PixelSize.Width);
                    Assert.Equal(80, bmp.PixelSize.Height);
                    Assert.Equal(expected, ReadPixels(bmp));
                }
                finally { view.Close(); }
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
                    view.Close();
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
                finally { view.Close(); }
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
                finally { view.Close(); }
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
                finally { view.Close(); }
            }
        }

        // ------------------------------------------------------------------
        // A ROM mutation (simulating a successful Import) followed by the
        // icon-preserving list refresh must keep the SAME slot selected,
        // update the TARGET preview to reflect the NEW ROM bytes, and never
        // touch the SOURCE pane.
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void PostRomMutation_RefreshPreservingSelection_UpdatesTargetKeepsSourceAndSelection()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (EnsureImageService())
            {
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
                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    Dispatcher.UIThread.RunJobs();
                    var targetImg = GetTargetImageDisplay(view);
                    byte[] pixelsBefore = ReadPixels(Assert.IsType<WriteableBitmap>(targetImg.Source));

                    // Simulate what a successful Import just wrote: slot 1's
                    // palette now points at the "Portrait B" (BLUE) palette
                    // instead of its original RED one.
                    rom.write_p32(EntryAddr(1) + 8, PaletteOffsetB);

                    // Same production seam Import_Click's post-success
                    // refresh uses (AddressListControl.SetItemsWithIconsPreserveSelection),
                    // exercised directly here rather than through the full
                    // pixel-quantize-and-write Import pipeline.
                    var items = new System.Collections.Generic.List<AddrResult>
                    {
                        new AddrResult(EntryAddr(0), "0x00", 0),
                        new AddrResult(EntryAddr(1), "0x01", 1),
                        new AddrResult(EntryAddr(2), "0x02", 2),
                        new AddrResult(EntryAddr(3), "0x03", 3),
                    };
                    list.SetItemsWithIconsPreserveSelection(items, i => ListIconLoaders.PortraitLoader(items, i), EntryAddr(1));
                    Dispatcher.UIThread.RunJobs();

                    // Selection preserved.
                    Assert.NotNull(list.SelectedItem);
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);

                    // Target preview picked up the mutated palette (new pixels).
                    byte[] pixelsAfter = ReadPixels(Assert.IsType<WriteableBitmap>(targetImg.Source));
                    Assert.NotEqual(pixelsBefore, pixelsAfter);

                    // Source pane untouched by the refresh.
                    Assert.Same(sourceRefBefore, GetSourceImageDisplay(view).Source);
                }
                finally
                {
                    view.Close();
                    try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
                }
            }
        }

        // ------------------------------------------------------------------
        // The real Import_Click success path must re-read list icons and the
        // target preview while preserving the source, selection, and filter.
        // ------------------------------------------------------------------
        [AvaloniaFact]
        public void ImportClick_Success_RefreshesIconAndTarget_PreservesSourceSelectionAndFilter()
        {
            var rom = BuildRom();
            using (UseRom(rom))
            using (EnsureImageService())
            using (UseImportState())
            {
                var view = new ImagePortraitImporterView();
                string tmpPng = WriteSynthSourcePng(0, 220, 0, 96, 80);
                try
                {
                    view.Show();
                    Dispatcher.UIThread.RunJobs();
                    var list = view.FindControl<AddressListControl>("EntryList")!;
                    Assert.True(list.SelectAddress(EntryAddr(1)));
                    list.ApplySearchFilter("0x01");
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);
                    Assert.Single(GetDisplayItems(list));
                    Assert.NotNull(GetDisplayItems(list)[0].Icon);

                    InvokeLoadImageFromPath(view, tmpPng);
                    var sourceBefore = GetSourceImageDisplay(view).Source;
                    var target = GetTargetImageDisplay(view);
                    byte[] targetPixelsBefore = ReadPixels(
                        Assert.IsType<WriteableBitmap>(target.Source));
                    uint sheetBefore = rom.p32(EntryAddr(1));

                    InvokeImportClick(view);
                    Dispatcher.UIThread.RunJobs();

                    var searchBox = list.FindControl<TextBox>("SearchBox");
                    var listBox = list.FindControl<ListBox>("AddressList");
                    Assert.Equal("0x01", searchBox!.Text);
                    Assert.Equal(1, listBox!.ItemCount);
                    Assert.Equal(EntryAddr(1), list.SelectedItem!.addr);
                    Assert.Null(GetDisplayItems(list).Single().Icon);
                    Assert.NotEqual(sheetBefore, rom.p32(EntryAddr(1)));
                    var targetAfter = Assert.IsType<WriteableBitmap>(target.Source);
                    Assert.Equal(96, targetAfter.PixelSize.Width);
                    Assert.Equal(80, targetAfter.PixelSize.Height);
                    Assert.NotEqual(targetPixelsBefore, ReadPixels(targetAfter));
                    Assert.Same(sourceBefore, GetSourceImageDisplay(view).Source);
                }
                finally
                {
                    view.Close();
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
                    view.Close();
                    try { if (File.Exists(tmpPng)) File.Delete(tmpPng); } catch { }
                }
            }
        }
    }
}
